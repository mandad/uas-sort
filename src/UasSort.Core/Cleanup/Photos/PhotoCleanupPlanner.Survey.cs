// src/UasSort.Core/Cleanup/Photos/PhotoCleanupPlanner.Survey.cs
namespace UasSort.Core.Cleanup;

/// <summary>Picture Offload cleanup planning (spec 2026-10-04 §3–§4). Pure apart from the EXIF reads through PhotoExifCache, which never
/// opens a cloud-only file.</summary>
public static partial class PhotoCleanupPlanner
{
    /// <summary>The photo root is listed like every library root: without the ledger folder (Ref §7.1).</summary>
    public static readonly IReadOnlySet<string> Excludes = new HashSet<string>([LedgerPaths.FolderName], StringComparer.OrdinalIgnoreCase);

    /// <summary>What is directly in the photo root, and when each file was shot: (1) the ledger file record for that destination with the
    /// file's size (resolved ambiguity 8a); (2) the EXIF of a local file, resolved by <paramref name="clock"/> as the scan does (8);
    /// (3) otherwise "date unknown (cloud-only)".</summary>
    public static PhotoSurvey Survey(string photoRoot, ListingResult listing, LedgerSnapshot ledger, PhotoExifCache exif, PhotoCaptureClock clock,
                                     IProgress<PhotoScanProgress>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(photoRoot);
        ArgumentNullException.ThrowIfNull(listing);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(exif);
        ArgumentNullException.ThrowIfNull(clock);
        var root = PathRules.Normalize(photoRoot);
        var byDest = new Dictionary<string, LedgerFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in ledger.Files.Values)
            if (f.Root == DestRoot.Photo) byDest.TryAdd(PathRules.Normalize(f.Dest), f);

        var topFiles = new List<FsEntry>();
        var topDirs = new List<FsEntry>();
        var inDir = new Dictionary<string, List<FsEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in listing.Entries)
        {
            var rel = e.RelPath.Replace('/', '\\').Trim('\\');
            var cut = rel.IndexOf('\\');
            if (cut < 0)
            {
                (e.IsDirectory ? topDirs : topFiles).Add(e);
                continue;
            }
            var top = rel[..cut];
            if (!inDir.TryGetValue(top, out var list)) inDir[top] = list = [];
            list.Add(e with { RelPath = rel });
        }

        var notTouched = new List<string>();
        var pending = new List<(string RelPath, PhotoItemKind Kind, PhotoSetKind SetKind, string? SetName, List<FsEntry> Files)>();

        // Loose photos: a DNG and a JPG/JPEG with the same stem are one unit (the DNG decides); every other photo is its own unit.
        foreach (var stem in topFiles.Where(f => PhotoCleanupRules.IsPhotoName(Name(f)))
                                     .GroupBy(f => Path.GetFileNameWithoutExtension(Name(f)), StringComparer.OrdinalIgnoreCase))
        {
            var files = stem.OrderBy(Name, StringComparer.OrdinalIgnoreCase).ToList();
            var dng = files.FirstOrDefault(f => PhotoCleanupRules.IsDng(Name(f)));
            var twin = dng is null ? null : files.FirstOrDefault(f => PhotoCleanupRules.IsJpg(Name(f)));
            if (dng is not null && twin is not null)
            {
                pending.Add((Name(dng), PhotoItemKind.Photo, PhotoSetKind.Unknown, null, [dng, twin]));
                files.Remove(dng);
                files.Remove(twin);
            }
            foreach (var f in files) pending.Add((Name(f), PhotoItemKind.Photo, PhotoSetKind.Unknown, null, [f]));
        }
        foreach (var f in topFiles.Where(f => !PhotoCleanupRules.IsPhotoName(Name(f)))) notTouched.Add(Name(f));

        // Set folders: named like a set, or holding this app's set-member records; only photo files directly inside, nothing else.
        foreach (var d in topDirs)
        {
            var folder = Name(d);
            var content = inDir.GetValueOrDefault(folder) ?? [];
            var records = content.Select(c => byDest.GetValueOrDefault(PathRules.Join(root, c.RelPath))).OfType<LedgerFile>().ToList();
            var isSet = PhotoCleanupRules.LooksLikeSetFolder(folder) || records.Exists(r => r.Set is not null);
            var plain = content.Count > 0 && content.TrueForAll(c => !c.IsDirectory && c.RelPath.Count(ch => ch == '\\') == 1
                                                                      && PhotoCleanupRules.IsPhotoName(Name(c)));
            if (!isSet || !plain)
            {
                notTouched.Add(folder);
                continue;
            }
            var setName = records.Select(r => r.Set).FirstOrDefault(s => s is not null) ?? PhotoCleanupRules.SetNameOf(folder);
            pending.Add((folder, PhotoItemKind.Set, KindOf(records), setName, [.. content.OrderBy(Name, StringComparer.OrdinalIgnoreCase)]));
        }

        var toRead = pending.Sum(p => p.Files.Count(f => Record(Full(p.Kind, f), f.Size) is not { LocalDate: not null }
                                                         && !PhotoCleanupRules.IsCloudOnly(f.RawAttributes)));
        var done = 0;
        var items = new List<PhotoItem>(pending.Count);
        foreach (var p in pending)
        {
            ct.ThrowIfCancellationRequested();
            var members = ImmutableArray.CreateBuilder<PhotoMember>(p.Files.Count);
            foreach (var f in p.Files)
            {
                var rel = p.Kind == PhotoItemKind.Set ? f.RelPath.Replace('/', '\\').Trim('\\') : Name(f);
                var full = PathRules.Join(root, rel);
                var record = Record(full, f.Size);
                if (record is { LocalDate: { } date })
                {
                    members.Add(new PhotoMember(rel, f.Size, f.MtimeUtc, f.RawAttributes, record.CaptureUtc, date, CaptureSource.Ledger, null, null, record));
                    continue;
                }
                var read = exif.Read(full, f.Size, f.MtimeUtc, f.RawAttributes);
                if (!PhotoCleanupRules.IsCloudOnly(f.RawAttributes)) progress?.Report(new PhotoScanProgress("Reading photo dates", ++done, toRead));
                if (read.Info is { DtoNaive: not null } info)
                {
                    var (utc, localDate) = clock.Resolve(info);
                    members.Add(new PhotoMember(rel, f.Size, f.MtimeUtc, f.RawAttributes, utc, localDate, CaptureSource.Exif, read.Stamp, null, record)
                    {
                        Pixels = PhotoCleanupRules.PixelsOf(info),
                    });
                }
                else
                {
                    members.Add(new PhotoMember(rel, f.Size, f.MtimeUtc, f.RawAttributes, null, null, CaptureSource.None, null,
                                                read.Problem ?? "date unknown", record));
                }
            }
            items.Add(new PhotoItem(p.RelPath, p.Kind, p.SetKind, p.SetName, members.MoveToImmutable()));
        }

        return new PhotoSurvey(root,
            [.. items.OrderBy(i => i.FirstDate ?? DateOnly.MaxValue).ThenBy(i => i.RelPath, StringComparer.OrdinalIgnoreCase)],
            [.. notTouched.Order(StringComparer.OrdinalIgnoreCase)], ledger);

        string Full(PhotoItemKind kind, FsEntry f)
            => PathRules.Join(root, kind == PhotoItemKind.Set ? f.RelPath.Replace('/', '\\').Trim('\\') : Name(f));

        // Spec §3(1): the record for this destination only, and only while the file there still has the recorded size (8a).
        LedgerFile? Record(string full, long size)
            => byDest.GetValueOrDefault(PathRules.Normalize(full)) is { } r && r.Key.Size == size ? r : null;
    }

    private static string Name(FsEntry e) => PathRules.FileName(e.FullPath);

    private static PhotoSetKind KindOf(IEnumerable<LedgerFile> records)
    {
        foreach (var r in records)
        {
            var src = r.Src.Replace('\\', '/');
            if (src.Contains("/PANORAMA/", StringComparison.OrdinalIgnoreCase)) return PhotoSetKind.Panorama;
            if (src.Contains("/HYPERLAPSE/", StringComparison.OrdinalIgnoreCase)) return PhotoSetKind.Hyperlapse;
        }
        return PhotoSetKind.Unknown;
    }
}
