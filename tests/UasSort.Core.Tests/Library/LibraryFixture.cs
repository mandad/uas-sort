using UasSort.Core;

namespace UasSort.Core.Tests.Library;

/// <summary>Builds <see cref="LibraryListings"/> as the lister returns them (directories included, recursive, relative paths).
/// It deliberately does NOT skip .uas-sort, so the tests prove the index drops that subtree itself.</summary>
internal sealed class LibraryFixture
{
    public const string VideoRoot = @"C:\Lib\UAS Videos";
    public const string PhotoRoot = VideoRoot + @"\Picture Offload";
    public static readonly DateTime T = Utc(2026, 9, 27, 20, 0, 0);

    private readonly List<FsEntry> _entries = [];
    private readonly HashSet<string> _dirs = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _previous = [];
    private readonly HashSet<string> _unavailable = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string Path, int Win32Error)> _errors = [];
    private string _photoRoot = PhotoRoot;

    public static DateTime Utc(int y, int mo, int d, int h = 0, int mi = 0, int s = 0) => new(y, mo, d, h, mi, s, DateTimeKind.Utc);

    public LibraryFixture WithFile(string fullPath, long size, DateTime mtimeUtc, uint attributes = 0x20)
    {
        AddParents(fullPath);
        _entries.Add(new FsEntry(fullPath, "", false, size, mtimeUtc, mtimeUtc, mtimeUtc, attributes));
        return this;
    }

    public LibraryFixture WithDir(string fullPath)
    {
        AddParents(fullPath);
        AddDir(fullPath);
        return this;
    }

    public LibraryFixture WithPhotoRoot(string root) { _photoRoot = root; return this; }
    public LibraryFixture WithPrevious(string root) { _previous.Add(root); return this; }
    public LibraryFixture WithUnavailable(string root) { _unavailable.Add(root); return this; }
    public LibraryFixture WithError(string path, int win32Error) { _errors.Add((path, win32Error)); return this; }

    public LibraryListings Build()
        => new(Listing(VideoRoot, DestRoot.Video, false), Listing(_photoRoot, DestRoot.Photo, false),
               [.. _previous.Select(p => Listing(p, DestRoot.Photo, true))]);

    private void AddParents(string path)
    {
        for (string? p = PathRules.Parent(path); p is not null; p = PathRules.Parent(p)) AddDir(p);
    }

    private void AddDir(string path)
    {
        string n = PathRules.Normalize(path);
        if (_dirs.Add(n)) _entries.Add(new FsEntry(n, "", true, 0, T, T, T, 0x10));
    }

    private RootListing Listing(string root, DestRoot kind, bool previous)
    {
        string r = PathRules.Normalize(root);
        bool available = !_unavailable.Contains(root);
        ImmutableArray<FsEntry> entries = available
            ? [.. _entries.Where(e => PathRules.IsSameOrUnder(e.FullPath, r) && !PathRules.Equal(e.FullPath, r))
                          .Select(e => e with { RelPath = PathRules.RelativeCardPath(e.FullPath, r).Replace('/', '\\') })]
            : [];
        ImmutableArray<(string Path, int Win32Error)> errors = available ? [.. _errors.Where(x => PathRules.IsSameOrUnder(x.Path, r))] : [];
        return new RootListing(root, kind, previous, available, new ListingResult(entries, errors));
    }
}
