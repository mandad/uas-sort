// src/UasSort.Core/Cleanup/Photos/LightroomIndex.cs
using System.Globalization;

namespace UasSort.Core.Cleanup;

/// <summary>One indexed library file. Spec §4: a photo or a set frame is verified by a DNG only (IsDng); a JPG entry can only confirm a
/// stitched panorama (an export or edit of a photo must never verify the Picture Offload original).</summary>
public sealed record LightroomPhoto(string FullPath, ExifStamp Stamp)
{
    public bool IsDng => PhotoCleanupRules.IsDng(FullPath);
}

/// <summary>A library folder the walk couldn't list (Win32 error; <see cref="LightroomIndex.TooDeepError"/> past the depth limit,
/// <see cref="LightroomIndex.ProtectedFolderError"/> for a library folder inside a protected root).</summary>
public sealed record LightroomFolderError(string Path, int Win32Error);

/// <summary>What the Lightroom walk read and couldn't read (branch-2 ruling): carried by the plan, shown on the Review page and written to
/// the report, so a library that couldn't be read never looks like "not found in Lightroom".</summary>
public sealed record LightroomIndexSummary(string Folder, int Indexed, ImmutableArray<LightroomFolderError> FolderErrors,
                                           ImmutableArray<string> UnreadableFiles)
{
    /// <summary>Null when everything could be read; otherwise one sentence for the Review page and the report.</summary>
    public string? Problem
    {
        get
        {
            if (FolderErrors.IsDefaultOrEmpty && UnreadableFiles.IsDefaultOrEmpty) return null;
            var inv = CultureInfo.InvariantCulture;
            var parts = new List<string>();
            if (!FolderErrors.IsDefaultOrEmpty)
            {
                var first = FolderErrors[0];
                var more = FolderErrors.Length > 1 ? string.Create(inv, $", and {FolderErrors.Length - 1} more") : "";
                parts.Add(string.Create(inv, $"{Count(FolderErrors.Length, "library folder couldn't", "library folders couldn't")} be listed ")
                          + string.Create(inv, $"({first.Path}: Win32 error {first.Win32Error}{more})"));
            }
            if (!UnreadableFiles.IsDefaultOrEmpty)
                parts.Add($"{Count(UnreadableFiles.Length, "library file couldn't", "library files couldn't")} be read (cloud-only or corrupt)");
            return $"Part of the Lightroom library couldn't be read: {string.Join("; ", parts)}. "
                   + "Rows may show “not found” although the photo is in Lightroom.";
        }
    }

    private static string Count(int n, string one, string many) => n == 1 ? "1 " + one : string.Create(CultureInfo.InvariantCulture, $"{n} {many}");
}

/// <summary>The Lightroom library folder, read-only (spec 2026-10-04 §4): its DNG files (and JPGs, for stitched panoramas only) shot within
/// the range ± 1 day, by (DateTimeOriginal second, Model). Walks folder by folder so the catalog (LightroomRules) is never listed into or
/// opened, dated folders outside the range are not entered, files written before the range are not opened, and a cloud-only file is never
/// opened (PhotoExifCache; it is listed in Unreadable), as is a corrupt one.
/// The walk never follows a link (Task PCfix): a reparse-point folder or file that is not a cloud placeholder (a junction, mount point or
/// symbolic link — the listing carries no reparse tag, so any such reparse point counts as one, as WindowsDirectoryLister.ShouldDescend
/// would refuse an unknown tag) is never entered or read. So every listed path is the folder's canonical form plus real names, and no file
/// under a protected root (the photo root, the video root, app data) is ever listed into or indexed: a photo can never verify against
/// itself. The walk stops <see cref="MaxDepth"/> folders down with a listing error, whatever a listing reports.</summary>
public sealed class LightroomIndex
{
    /// <summary>The deepest folder level the walk enters below the library folder.</summary>
    public const int MaxDepth = 32;

    /// <summary>The listing error recorded for a folder below <see cref="MaxDepth"/>: ERROR_CANT_RESOLVE_FILENAME, what Windows reports
    /// for a link loop.</summary>
    public const int TooDeepError = 1921;

    /// <summary>The listing error recorded when the library folder itself is (under) a protected root, so it is never read: ERROR_ACCESS_DENIED
    /// (branch-2 ruling: an Errors entry, never a silent empty index).</summary>
    public const int ProtectedFolderError = 5;

    private const uint ReparsePoint = 0x400;   // FILE_ATTRIBUTE_REPARSE_POINT
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

    internal static LightroomIndex From(string folder, IEnumerable<LightroomPhoto> photos, IEnumerable<(string Path, int Win32Error)>? errors = null,
                                        IEnumerable<string>? unreadable = null)
    {
        var map = new Dictionary<(DateTime Second, string Model), List<LightroomPhoto>>();
        foreach (var p in photos) Add(map, p);
        return new LightroomIndex(PathRules.Normalize(folder), map, 0, [.. errors ?? []], [.. unreadable ?? []]);
    }

    /// <summary>Some of the library couldn't be read: a listing failed (or was refused or too deep), or a file couldn't be read. A shot that
    /// is "not found" may then be in the part that wasn't read.</summary>
    public bool Incomplete => !Errors.IsEmpty || !Unreadable.IsEmpty;

    public LightroomIndexSummary Summary()
        => new(Folder, Count, [.. Errors.Select(e => new LightroomFolderError(e.Path, e.Win32Error))], Unreadable);

    public IReadOnlyList<LightroomPhoto> SameSecond(ExifStamp stamp)
    {
        ArgumentNullException.ThrowIfNull(stamp);
        return _bySecond.TryGetValue((stamp.Second, Upper(stamp.Model)), out var list) ? list : [];
    }

    /// <param name="protectedRoots">Canonical folders whose files are never Lightroom files (the photo root, the video root, previous photo
    /// roots, app data): never listed into, never indexed.</param>
    public static LightroomIndex Build(string folder, IDirectoryLister lister, PhotoExifCache exif, DateOnly from, DateOnly to,
                                       IReadOnlyCollection<string> protectedRoots, IProgress<PhotoScanProgress>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(lister);
        ArgumentNullException.ThrowIfNull(exif);
        ArgumentNullException.ThrowIfNull(protectedRoots);
        var lo = from.AddDays(-1);
        var hi = to.AddDays(1);
        bool Protected(string path) => protectedRoots.Any(r => PathRules.IsSameOrUnder(path, r));
        var errors = ImmutableArray.CreateBuilder<(string Path, int Win32Error)>();
        var candidates = new List<FsEntry>();
        var dirs = new Queue<(string Path, int Depth)>();
        var root = PathRules.Normalize(folder);
        if (Protected(root)) errors.Add((root, ProtectedFolderError));
        else dirs.Enqueue((root, 0));
        while (dirs.TryDequeue(out var dir))
        {
            ct.ThrowIfCancellationRequested();
            var listing = lister.Enumerate(dir.Path, recurse: false, NoExcludes);
            errors.AddRange(listing.Errors);
            foreach (var e in listing.Entries)
            {
                var name = PathRules.FileName(e.FullPath);
                if (LightroomRules.IsCatalogName(name) || LightroomRules.IsCatalogPath(e.FullPath)) continue;
                if (IsLink(e.RawAttributes) || Protected(e.FullPath)) continue;   // never followed, never indexed
                if (e.IsDirectory)
                {
                    if (FolderOutside(name, lo, hi)) continue;
                    if (dir.Depth >= MaxDepth) errors.Add((e.FullPath, TooDeepError));
                    else dirs.Enqueue((e.FullPath, dir.Depth + 1));
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

    /// <summary>A reparse point that is not a cloud placeholder: a junction, mount point or symbolic link (no reparse tag in the listing).</summary>
    private static bool IsLink(uint attributes) => (attributes & ReparsePoint) != 0 && !PhotoCleanupRules.IsCloudOnly(attributes);

    private static void Add(Dictionary<(DateTime Second, string Model), List<LightroomPhoto>> map, LightroomPhoto p)
    {
        var key = (p.Stamp.Second, Upper(p.Stamp.Model));
        if (!map.TryGetValue(key, out var list)) map[key] = list = [];
        list.Add(p);
    }

    private static string Upper(string model) => model.Trim().ToUpperInvariant();
}
