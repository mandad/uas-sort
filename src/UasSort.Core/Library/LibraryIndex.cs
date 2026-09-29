// src/UasSort.Core/Library/LibraryIndex.cs
using UasSort.Core.Library;

namespace UasSort.Core;

// The library as seen through listings and the ledger only (Ref §7.1). Never opens a library file.
public sealed partial class LibraryIndex
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mov", ".insv", ".avi" };

    private readonly ImmutableDictionary<FileKey, ImmutableArray<LibraryFile>> _byKey;
    private readonly ImmutableDictionary<string, ImmutableArray<LibraryFile>> _byName;
    private readonly ImmutableDictionary<string, ImmutableArray<LibraryFile>> _byFolder;
    private readonly ImmutableDictionary<string, ImmutableArray<SetFolderListing>> _setFolders;

    private LibraryIndex(ImmutableArray<LibraryFile> files, ImmutableArray<LibraryFolder> folders,
                         ImmutableDictionary<string, ImmutableArray<SetFolderListing>> setFolders, DateTime? watermarkUtc,
                         ImmutableArray<string> unavailableRoots, ImmutableHashSet<string> startsFromMtime,
                         ImmutableArray<(string Path, int Win32Error)> listingErrors)
    {
        Files = files;
        Folders = folders;
        WatermarkUtc = watermarkUtc;
        UnavailableRoots = unavailableRoots;
        StartsFromMtime = startsFromMtime;
        ListingErrors = listingErrors;
        _setFolders = setFolders;
        _byKey = files.GroupBy(f => f.Key).ToImmutableDictionary(g => g.Key, g => g.ToImmutableArray());
        _byName = files.GroupBy(f => f.Key.NormName, StringComparer.Ordinal)
                       .ToImmutableDictionary(g => g.Key, g => g.ToImmutableArray(), StringComparer.Ordinal);
        _byFolder = files.Where(f => f.EventFolder is not null)
                         .GroupBy(f => f.EventFolder!.FullPath, StringComparer.OrdinalIgnoreCase)
                         .ToImmutableDictionary(g => g.Key, g => g.ToImmutableArray(), StringComparer.OrdinalIgnoreCase);
    }

    public ImmutableArray<LibraryFolder> Folders { get; }
    public DateTime? WatermarkUtc { get; }
    public ImmutableArray<string> UnavailableRoots { get; }
    public ImmutableArray<LibraryFile> Files { get; }
    public ImmutableHashSet<string> StartsFromMtime { get; }
    public ImmutableArray<(string Path, int Win32Error)> ListingErrors { get; }

    public ImmutableArray<LibraryFile> Match(FileKey key) => _byKey.TryGetValue(key, out var v) ? v : [];

    public ImmutableArray<LibraryFile> SameNameOtherSize(string normName, long size)
        => _byName.TryGetValue(normName, out var v) ? [.. v.Where(f => f.Key.Size != size)] : [];

    public ImmutableArray<SetFolderListing> SetFolder(string name) => _setFolders.TryGetValue(name, out var v) ? v : [];

    public ImmutableArray<LibraryFile> FilesIn(LibraryFolderRef folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        return _byFolder.TryGetValue(PathRules.Normalize(folder.FullPath), out var v) ? v : [];
    }

    public static LibraryIndex Build(LibraryListings listings, LedgerSnapshot ledger, ClockModel clock)
    {
        ArgumentNullException.ThrowIfNull(listings);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(clock);
        CollectedLibrary collected = LibraryEntries.Collect(listings);
        string videoRoot = listings.Video.Root;
        ImmutableArray<string> photoRoots = [listings.Photo.Root, .. listings.PreviousPhoto.Select(p => p.Root)];

        var ledgerFolders = new Dictionary<string, LedgerFolder>(StringComparer.OrdinalIgnoreCase);
        foreach (LedgerFolder lf in ledger.Folders.Values) ledgerFolders[PathRules.Normalize(lf.Path)] = lf;

        // event folders from directory entries (incl. ones with no files yet)
        var refs = new Dictionary<string, LibraryFolderRef>(StringComparer.OrdinalIgnoreCase);
        foreach (LibraryEntry d in collected.Directories)
        {
            if (EventFolderLocator.For(d.Entry.FullPath, videoRoot, photoRoots, includeSelf: true) is { } r
                && PathRules.Equal(r.FullPath, d.Entry.FullPath))
            {
                refs.TryAdd(r.FullPath, r);
            }
        }

        var files = ImmutableArray.CreateBuilder<LibraryFile>(collected.Files.Length);
        var starts = new Dictionary<string, List<DateTime>>(StringComparer.OrdinalIgnoreCase);
        var fromMtime = ImmutableHashSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);
        DateTime? watermark = null;

        foreach (LibraryEntry entry in collected.Files)
        {
            FsEntry e = entry.Entry;
            string path = PathRules.Normalize(e.FullPath);
            string name = PathRules.FileName(path);
            LibraryFolderRef? folder = EventFolderLocator.For(path, videoRoot, photoRoots, includeSelf: false);
            if (folder is not null)
            {
                if (refs.TryGetValue(folder.FullPath, out LibraryFolderRef? known)) folder = known;
                else refs[folder.FullPath] = folder;
            }
            var file = new LibraryFile(path, FileKey.Of(name, e.Size), AsUtc(e.MtimeUtc), e.RawAttributes, folder);
            files.Add(file);
            if (!VideoExtensions.Contains(Path.GetExtension(name))) continue;

            string? tz = folder is not null && ledgerFolders.TryGetValue(folder.FullPath, out LedgerFolder? lf) ? lf.TzId : null;
            MemberStart start = MemberStartResolver.Resolve(name, e.MtimeUtc, tz, clock);
            if (start.FromMtime) fromMtime.Add(path);
            if (folder is not null)
            {
                if (!starts.TryGetValue(folder.FullPath, out List<DateTime>? list))
                {
                    list = new List<DateTime>();
                    starts[folder.FullPath] = list;
                }
                list.Add(start.Utc);

                // decision 4: only event-folder videos without a ledger `file` record move the watermark
                if (!ledger.Files.ContainsKey(file.Key) && (watermark is null || start.Utc > watermark)) watermark = start.Utc;
            }
        }

        ImmutableArray<LibraryFile> built = files.ToImmutable();

        var folders = ImmutableArray.CreateBuilder<LibraryFolder>(refs.Count);
        foreach (LibraryFolderRef r in refs.Values.OrderBy(x => x.FullPath, StringComparer.OrdinalIgnoreCase))
        {
            ImmutableArray<DateTime> memberStarts = starts.TryGetValue(r.FullPath, out List<DateTime>? list) ? [.. list.Order()] : [];
            ledgerFolders.TryGetValue(r.FullPath, out LedgerFolder? lf);
            GeoPoint? centroid = lf?.Centroid ?? Centroid(ledger.Files.Values
                .Where(f => f.Point is not null && PathRules.Parent(f.Dest) is { } parent && PathRules.Equal(parent, r.FullPath))
                .Select(f => f.Point.GetValueOrDefault()));
            folders.Add(new LibraryFolder(r, memberStarts, lf?.TzId, centroid, centroid is null ? LocationSource.Unknown : LocationSource.Ledger));
        }

        // set folders: directories directly inside an available listed root
        ImmutableArray<RootListing> roots = [listings.Video, listings.Photo, .. listings.PreviousPhoto];
        var rootPaths = new HashSet<string>(roots.Where(x => x.Available).Select(x => PathRules.Normalize(x.Root)), StringComparer.OrdinalIgnoreCase);
        var byParent = built.GroupBy(f => PathRules.Parent(f.FullPath) is { } p ? PathRules.Normalize(p) : "", StringComparer.OrdinalIgnoreCase)
                            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var setFolders = new Dictionary<string, List<SetFolderListing>>(StringComparer.OrdinalIgnoreCase);
        foreach (LibraryEntry d in collected.Directories)
        {
            string dir = PathRules.Normalize(d.Entry.FullPath);
            if (PathRules.Parent(dir) is not { } parent || !rootPaths.Contains(PathRules.Normalize(parent))) continue;
            ImmutableArray<(string Member, long Size, DateTime MtimeUtc)> members = byParent.TryGetValue(dir, out List<LibraryFile>? inDir)
                ? [.. inDir.OrderBy(f => PathRules.FileName(f.FullPath), StringComparer.OrdinalIgnoreCase)
                           .Select(f => (Member: PathRules.FileName(f.FullPath), Size: f.Key.Size, MtimeUtc: f.MtimeUtc))]
                : [];
            string name = PathRules.FileName(dir);
            if (!setFolders.TryGetValue(name, out List<SetFolderListing>? same))
            {
                same = new List<SetFolderListing>();
                setFolders[name] = same;
            }
            same.Add(new SetFolderListing(dir, name, members));
        }

        return new LibraryIndex(built, folders.ToImmutable(),
                                setFolders.ToImmutableDictionary(kv => kv.Key, kv => kv.Value.ToImmutableArray(), StringComparer.OrdinalIgnoreCase),
                                watermark, collected.UnavailableRoots, fromMtime.ToImmutable(), collected.Errors);
    }

    private static DateTime AsUtc(DateTime d) => d.Kind == DateTimeKind.Utc ? d : DateTime.SpecifyKind(d, DateTimeKind.Utc);

    /// <summary>Mean of unit vectors (the same centroid definition as clustering, Ref §8.2).</summary>
    private static GeoPoint? Centroid(IEnumerable<GeoPoint> points)
    {
        double x = 0, y = 0, z = 0;
        int n = 0;
        foreach (GeoPoint p in points)
        {
            double lat = p.Lat * Math.PI / 180, lon = p.Lon * Math.PI / 180;
            x += Math.Cos(lat) * Math.Cos(lon);
            y += Math.Cos(lat) * Math.Sin(lon);
            z += Math.Sin(lat);
            n++;
        }
        if (n == 0) return null;
        x /= n;
        y /= n;
        z /= n;
        return new GeoPoint(Math.Atan2(z, Math.Sqrt((x * x) + (y * y))) * 180 / Math.PI, Math.Atan2(y, x) * 180 / Math.PI);
    }
}
