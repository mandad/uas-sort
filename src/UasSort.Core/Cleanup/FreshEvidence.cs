// src/UasSort.Core/Cleanup/FreshEvidence.cs
namespace UasSort.Core.Cleanup;

/// <summary>(NormName, size) → folder, from FRESH library listings only (Ref §10.6 Preparation 3; defined here).</summary>
public sealed class FreshEvidence
{
    private readonly Dictionary<FileKey, string> _folderByKey;

    private FreshEvidence(Dictionary<FileKey, string> folderByKey) => _folderByKey = folderByKey;

    public static FreshEvidence From(LibraryListings listings)
    {
        ArgumentNullException.ThrowIfNull(listings);
        var map = new Dictionary<FileKey, string>();
        foreach (var root in new[] { listings.Video, listings.Photo }.Concat(listings.PreviousPhoto))
        {
            if (!root.Available) continue;                               // an unavailable root contributes nothing
            foreach (var e in root.Listing.Entries)
            {
                if (e.IsDirectory || InLedgerFolder(e.RelPath)) continue;
                var full = e.FullPath.Replace('/', '\\');
                var i = full.LastIndexOf('\\');
                var name = i < 0 ? full : full[(i + 1)..];
                map.TryAdd(new FileKey(CleanupKeys.NormName(name), e.Size), i < 0 ? "" : full[..i]);
            }
        }
        return new FreshEvidence(map);
    }

    public string? ListedFolder(FileKey key) => _folderByKey.TryGetValue(key, out var f) ? f : null;

    private static bool InLedgerFolder(string rel)
    {
        foreach (var seg in rel.Replace('\\', '/').Split('/'))
            if (seg.Equals(LedgerPaths.FolderName, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
