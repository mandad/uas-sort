// src/UasSort.Core/Cleanup/Photos/PhotoExifCache.cs
using System.Globalization;

namespace UasSort.Core.Cleanup;

public sealed record PhotoExifRead(StillInfo? Info, string? Problem)
{
    public ExifStamp? Stamp => ExifStamp.From(Info);
}

/// <summary>EXIF of Picture Offload and Lightroom files through IPhotoFileReader, read once per (path, size, mtime). A cloud-only file is
/// never opened (its attributes say so, spec 2026-10-04 §3); a failed or refused read becomes a Problem, never an exception — when the
/// guard refuses (UnsafeIoException) nothing was opened, and the file is simply treated as unreadable. A corrupt file can make the parser
/// throw anything (ArgumentException, IndexOutOfRangeException, OverflowException, …): that too is the one file's Problem, so one bad photo
/// never aborts the page or the Lightroom index; only a cancellation propagates.</summary>
public sealed class PhotoExifCache(IPhotoFileReader reader)
{
    public const string CloudOnlyProblem = PhotoCleanupRules.DateUnknownCloudOnly;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, PhotoExifRead> _reads = new(StringComparer.Ordinal);

    public int Opens { get; private set; }

    public PhotoExifRead Read(string fullPath, long size, DateTime mtimeUtc, uint attributes)
    {
        ArgumentNullException.ThrowIfNull(fullPath);
        if (PhotoCleanupRules.IsCloudOnly(attributes)) return new PhotoExifRead(null, CloudOnlyProblem);
        var key = string.Create(CultureInfo.InvariantCulture, $"{PathRules.Normalize(fullPath).ToUpperInvariant()}|{size}|{mtimeUtc.Ticks}");
        lock (_gate)
        {
            if (_reads.TryGetValue(key, out var hit)) return hit;
        }
        PhotoExifRead read;
        try
        {
            using var s = reader.OpenRead(fullPath);
            var info = StillProbe.Read(s);
            read = new PhotoExifRead(info, info.DtoNaive is null ? "it has no capture time (DateTimeOriginal)" : null);
        }
#pragma warning disable CA1031 // untrusted bytes: whatever the parser throws (not only ImageProcessingException) is this one file's Problem
        catch (Exception e) when (e is not OperationCanceledException)
#pragma warning restore CA1031
        {
            read = new PhotoExifRead(null, "its capture time couldn't be read: " + e.Message);
        }
        lock (_gate)
        {
            Opens++;
            _reads[key] = read;
        }
        return read;
    }
}
