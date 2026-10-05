// src/UasSort.Core/Cleanup/Photos/LightroomIndex.cs
using System.Globalization;

namespace UasSort.Core.Cleanup;

/// <summary>One indexed library file. Spec §4: a photo or a set frame is verified by a DNG only (IsDng); a JPG entry can only confirm a
/// stitched panorama (an export or edit of a photo must never verify the Picture Offload original).</summary>
public sealed record LightroomPhoto(string FullPath, ExifStamp Stamp)
{
    public bool IsDng => PhotoCleanupRules.IsDng(FullPath);
}

/// <summary>The Lightroom library folder, read-only (spec 2026-10-04 §4): its DNG files (and JPGs, for stitched panoramas only) shot within
/// the range ± 1 day, by (DateTimeOriginal second, Model). Walks folder by folder so the catalog (LightroomRules) is never listed into or
/// opened, dated folders outside the range are not entered, files written before the range are not opened, and a cloud-only file is never
/// opened (PhotoExifCache; it is listed in Unreadable).</summary>
public sealed class LightroomIndex
{
    private static readonly IReadOnlySet<string> NoExcludes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static readonly string[] Extensions = [".dng", ".jpg", ".jpeg"];
    private readonly Dictionary<(DateTime Second, string Model), List<LightroomPhoto>> _bySecond;

    private LightroomIndex(string folder, Dictionary<(DateTime Second, string Model), List<LightroomPhoto>> bySecond, int opened,
                           ImmutableArray<(string Path, int Win32Error)> errors, ImmutableArray<string> unreadable)
    {
        Folder = folder;
        _bySecond = bySecond;
        Count = bySecond.Values.Sum(l => l.Count);
        Opened = opened;
        Errors = errors;
        Unreadable = unreadable;
    }

    public string Folder { get; }
    public int Count { get; }
    public int Opened { get; }
    public ImmutableArray<(string Path, int Win32Error)> Errors { get; }
    public ImmutableArray<string> Unreadable { get; }

    public static LightroomIndex Empty(string folder)
        => new(PathRules.Normalize(folder), new Dictionary<(DateTime Second, string Model), List<LightroomPhoto>>(), 0, [], []);

    internal static LightroomIndex From(string folder, IEnumerable<LightroomPhoto> photos)
    {
        var map = new Dictionary<(DateTime Second, string Model), List<LightroomPhoto>>();
        foreach (var p in photos) Add(map, p);
        return new LightroomIndex(PathRules.Normalize(folder), map, 0, [], []);
    }

    public IReadOnlyList<LightroomPhoto> SameSecond(ExifStamp stamp)
    {
        ArgumentNullException.ThrowIfNull(stamp);
        return _bySecond.TryGetValue((stamp.Second, Upper(stamp.Model)), out var list) ? list : [];
    }

    public static LightroomIndex Build(string folder, IDirectoryLister lister, PhotoExifCache exif, DateOnly from, DateOnly to,
                                       IProgress<PhotoScanProgress>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(lister);
        ArgumentNullException.ThrowIfNull(exif);
        var lo = from.AddDays(-1);
        var hi = to.AddDays(1);
        var errors = ImmutableArray.CreateBuilder<(string Path, int Win32Error)>();
        var candidates = new List<FsEntry>();
        var dirs = new Queue<string>();
        dirs.Enqueue(PathRules.Normalize(folder));
        while (dirs.TryDequeue(out var dir))
        {
            ct.ThrowIfCancellationRequested();
            var listing = lister.Enumerate(dir, recurse: false, NoExcludes);
            errors.AddRange(listing.Errors);
            foreach (var e in listing.Entries)
            {
                var name = PathRules.FileName(e.FullPath);
                if (LightroomRules.IsCatalogName(name) || LightroomRules.IsCatalogPath(e.FullPath)) continue;
                if (e.IsDirectory)
                {
                    if (!FolderOutside(name, lo, hi)) dirs.Enqueue(e.FullPath);
                    continue;
                }
                if (!Extensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase)) continue;
                if (DateOnly.FromDateTime(e.MtimeUtc) < lo.AddDays(-1)) continue;   // written before the range: it can't hold a shot from it
                candidates.Add(e);
            }
        }

        var map = new Dictionary<(DateTime Second, string Model), List<LightroomPhoto>>();
        var unreadable = ImmutableArray.CreateBuilder<string>();
        var opened = 0;
        for (var i = 0; i < candidates.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var e = candidates[i];
            progress?.Report(new PhotoScanProgress("Reading the Lightroom library", i + 1, candidates.Count));
            var read = exif.Read(e.FullPath, e.Size, e.MtimeUtc, e.RawAttributes);
            if (!PhotoCleanupRules.IsCloudOnly(e.RawAttributes)) opened++;
            if (read.Stamp is not { } stamp)
            {
                unreadable.Add(e.FullPath);
                continue;
            }
            var day = DateOnly.FromDateTime(stamp.Second);
            if (day >= lo && day <= hi) Add(map, new LightroomPhoto(e.FullPath, stamp));
        }
        return new LightroomIndex(PathRules.Normalize(folder), map, opened, errors.ToImmutable(), unreadable.ToImmutable());
    }

    /// <summary>A dated folder ("yyyy", "yyyy-MM", "yyyy-MM-dd…") wholly outside [lo, hi]; any other name is entered.</summary>
    internal static bool FolderOutside(string name, DateOnly lo, DateOnly hi)
    {
        var inv = CultureInfo.InvariantCulture;
        if (name.Length >= 10 && DateOnly.TryParseExact(name[..10], "yyyy-MM-dd", inv, DateTimeStyles.None, out var day))
            return day < lo || day > hi;
        if (name.Length == 7 && DateOnly.TryParseExact(name + "-01", "yyyy-MM-dd", inv, DateTimeStyles.None, out var month))
            return month.AddMonths(1).AddDays(-1) < lo || month > hi;
        if (name.Length == 4 && int.TryParse(name, NumberStyles.None, inv, out var year) && year is > 1900 and < 3000)
            return year < lo.Year || year > hi.Year;
        return false;
    }

    private static void Add(Dictionary<(DateTime Second, string Model), List<LightroomPhoto>> map, LightroomPhoto p)
    {
        var key = (p.Stamp.Second, Upper(p.Stamp.Model));
        if (!map.TryGetValue(key, out var list)) map[key] = list = [];
        list.Add(p);
    }

    private static string Upper(string model) => model.Trim().ToUpperInvariant();
}
