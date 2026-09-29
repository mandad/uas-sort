using UasSort.Core;

namespace UasSort.Core.Library;

public sealed record LibraryEntry(FsEntry Entry, RootListing Root);
public sealed record CollectedLibrary(ImmutableArray<LibraryEntry> Files, ImmutableArray<LibraryEntry> Directories,
                                      ImmutableArray<string> UnavailableRoots, ImmutableArray<(string Path, int Win32Error)> Errors);

/// <summary>Ref §7.1 Listing: all roots, overlaps once, the .uas-sort subtree never.</summary>
public static class LibraryEntries
{
    public static CollectedLibrary Collect(LibraryListings listings)
    {
        ArgumentNullException.ThrowIfNull(listings);
        string ledgerDir = LedgerPaths.For(listings.Video.Root);
        ImmutableArray<RootListing> roots = [listings.Video, listings.Photo, .. listings.PreviousPhoto];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenErrors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = ImmutableArray.CreateBuilder<LibraryEntry>();
        var dirs = ImmutableArray.CreateBuilder<LibraryEntry>();
        var unavailable = ImmutableArray.CreateBuilder<string>();
        var errors = ImmutableArray.CreateBuilder<(string Path, int Win32Error)>();

        foreach (RootListing root in roots)
        {
            if (!root.Available)
            {
                if (!unavailable.Any(u => PathRules.Equal(u, root.Root))) unavailable.Add(root.Root);
                continue;
            }
            foreach ((string path, int code) in root.Listing.Errors)
            {
                if (!PathRules.IsSameOrUnder(path, ledgerDir) && seenErrors.Add(PathRules.Normalize(path))) errors.Add((path, code));
            }
            foreach (FsEntry e in root.Listing.Entries)
            {
                if (PathRules.IsSameOrUnder(e.FullPath, ledgerDir)) continue;
                if (!seen.Add(PathRules.Normalize(e.FullPath))) continue;
                (e.IsDirectory ? dirs : files).Add(new LibraryEntry(e, root));
            }
        }
        return new CollectedLibrary(files.ToImmutable(), dirs.ToImmutable(), unavailable.ToImmutable(), errors.ToImmutable());
    }
}
