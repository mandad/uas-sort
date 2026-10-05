// src/UasSort.Core/Cleanup/Photos/PhotoRootThumbnails.cs
namespace UasSort.Core.Cleanup;

/// <summary>Thumbnails of Picture Offload rows for the existing ThumbnailCache (spec 2026-10-04 §2), under "photo-root:&lt;primary rel
/// path&gt;" keys: a DNG's embedded IFD0 JPEG, or a JPEG of at most 16 MB itself. A cloud-only file is never opened: empty bytes, so the
/// placeholder shows.</summary>
public sealed class PhotoRootThumbnails(IPhotoFileReader reader) : IThumbnailSource
{
    public const string KeyPrefix = "photo-root:";
    public const long MaxWholeJpegBytes = 16_000_000;

    private readonly Lock _gate = new();
    private ImmutableDictionary<string, (string FullPath, uint Attributes, long Size)> _files =
        ImmutableDictionary<string, (string FullPath, uint Attributes, long Size)>.Empty;
    private int _pauses;

    public static ItemId KeyOf(PhotoItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new ItemId(KeyPrefix + item.Primary.RelPath);
    }

    public static bool IsKey(ItemId id) => id.CardRelPath.StartsWith(KeyPrefix, StringComparison.Ordinal);

    public void Use(string photoRoot, IEnumerable<PhotoItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var b = ImmutableDictionary.CreateBuilder<string, (string FullPath, uint Attributes, long Size)>(StringComparer.Ordinal);
        foreach (var i in items) b[KeyOf(i).CardRelPath] = (PhotoCleanupPaths.Full(photoRoot, i.Primary.RelPath), i.Primary.Attributes, i.Primary.Size);
        lock (_gate) _files = b.ToImmutable();
    }

    public ValueTask<ReadOnlyMemory<byte>> GetAsync(ItemId id, CancellationToken ct)
    {
        (string FullPath, uint Attributes, long Size) file;
        lock (_gate)
        {
            if (_pauses > 0 || !_files.TryGetValue(id.CardRelPath, out file)) return ValueTask.FromResult(ReadOnlyMemory<byte>.Empty);
        }
        return ValueTask.FromResult(PhotoCleanupRules.IsCloudOnly(file.Attributes) ? ReadOnlyMemory<byte>.Empty : Read(file.FullPath, file.Size));
    }

    public IDisposable Pause()
    {
        lock (_gate) _pauses++;
        return new Resume(this);
    }

    private ReadOnlyMemory<byte> Read(string fullPath, long size)
    {
        try
        {
            using var s = reader.OpenRead(fullPath);
            if (PhotoCleanupRules.IsJpg(fullPath))
            {
                if (size > MaxWholeJpegBytes) return ReadOnlyMemory<byte>.Empty;
                var whole = new byte[s.Length];
                s.ReadExactly(whole);
                return whole;
            }
            if (StillProbe.Read(s).Thumb is not { } range || range.Offset + range.Length > s.Length) return ReadOnlyMemory<byte>.Empty;
            var buffer = new byte[range.Length];
            s.Position = range.Offset;
            s.ReadExactly(buffer);
            return buffer;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or UnsafeIoException or MetadataExtractor.ImageProcessingException)
        {
            return ReadOnlyMemory<byte>.Empty;
        }
    }

    private sealed class Resume(PhotoRootThumbnails owner) : IDisposable
    {
        private int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) != 0) return;
            lock (owner._gate) owner._pauses--;
        }
    }
}
