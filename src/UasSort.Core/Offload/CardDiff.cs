using System.Collections.Immutable;

namespace UasSort.Core.Offload;

public sealed record CardDiffEntry(string RelPath, long Size);

public sealed record CardDiffResult(ImmutableArray<CardDiffEntry> Added, ImmutableArray<CardDiffEntry> Removed,
                                    ImmutableArray<CardDiffEntry> Changed, int LastAccessOnly)
{
    public const string ChangedDetail = "changed since scan";
    public const string AddedDetail = "changed since scan (added)";
    public const string RemovedDetail = "changed since scan (removed)";

    private readonly HashSet<string> _removed = new(Removed.Select(e => OffloadPaths.NormRel(e.RelPath)), StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _changed = new(Changed.Select(e => OffloadPaths.NormRel(e.RelPath)), StringComparer.OrdinalIgnoreCase);

    public bool IsEmpty => Added.IsEmpty && Removed.IsEmpty && Changed.IsEmpty;

    public string? Touched(string relPath)
    {
        var key = OffloadPaths.NormRel(relPath);
        return _removed.Contains(key) ? RemovedDetail : _changed.Contains(key) ? ChangedDetail : null;
    }
}

/// <summary>Ref §10.5: re-list the card and diff it against the scanned inventory (listing only).</summary>
public static class CardDiff
{
    private const string Svi = "System Volume Information";

    private static bool Excluded(string relPath)
    {
        var r = OffloadPaths.NormRel(relPath);
        return r.Equals(Svi, StringComparison.OrdinalIgnoreCase) || r.StartsWith(Svi + "/", StringComparison.OrdinalIgnoreCase);
    }

    public static CardDiffResult Compare(IEnumerable<CardEntry> scanned, ListingResult relisted)
    {
        ArgumentNullException.ThrowIfNull(scanned);
        ArgumentNullException.ThrowIfNull(relisted);
        var now = new Dictionary<string, FsEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in relisted.Entries)
            if (!e.IsDirectory && !Excluded(e.RelPath)) now.TryAdd(OffloadPaths.NormRel(e.RelPath), e);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var removed = ImmutableArray.CreateBuilder<CardDiffEntry>();
        var changed = ImmutableArray.CreateBuilder<CardDiffEntry>();
        int lastAccessOnly = 0;
        foreach (var e in scanned)
        {
            if (Excluded(e.RelPath)) continue;
            var key = OffloadPaths.NormRel(e.RelPath);
            seen.Add(key);
            if (!now.TryGetValue(key, out var n))
            {
                removed.Add(new CardDiffEntry(key, e.Size));
                continue;
            }
            if (n.Size != e.Size || n.MtimeUtc != e.MtimeUtc || n.CreationUtc != e.CreationUtc || n.RawAttributes != e.RawAttributes)
                changed.Add(new CardDiffEntry(key, n.Size));
            else if (n.LastAccessUtc != e.LastAccessUtc)
                lastAccessOnly++;
        }
        var added = now.Where(kv => !seen.Contains(kv.Key))
                       .Select(kv => new CardDiffEntry(kv.Key, kv.Value.Size))
                       .OrderBy(e => e.RelPath, StringComparer.Ordinal);
        return new CardDiffResult([.. added], removed.ToImmutable(), changed.ToImmutable(), lastAccessOnly);
    }
}
