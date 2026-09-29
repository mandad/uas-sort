// src/UasSort.Core/Ledger/LedgerReader.cs
using UasSort.Core;

namespace UasSort.Core.Ledger;

public static class LedgerReader
{
    /// <summary>Parse the union of <paramref name="sources"/> and build the snapshot (Ref §4.1 ILedgerStore.Load, pure half).</summary>
    public static LedgerSnapshot Read(IReadOnlyList<LedgerFileText> sources, LedgerFolderStatus status)
        => LedgerSnapshotBuilder.Build(LedgerParser.Parse(sources), status);
}

public static class LedgerSnapshots
{
    public static LedgerSnapshot Empty(LedgerFolderStatus status)
        => new(ImmutableDictionary<FileKey, LedgerFile>.Empty,
               ImmutableDictionary<string, ImmutableArray<LedgerSet>>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase),
               ImmutableDictionary<FileKey, LedgerDecision>.Empty,
               ImmutableDictionary<FileKey, DateTime>.Empty,
               ImmutableDictionary<string, LedgerFolder>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase),
               [], [], [], [], status);

    /// <summary>A status for ledger text that doesn't come from a live .uas-sort folder (backup mirrors, tests).</summary>
    public static LedgerFolderStatus Detached(string folder, IEnumerable<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        return new(folder, LedgerFolderState.Ok, Exists: true, InSyncRoot: false, Pinned: false, Writable: false, [.. files], [], []);
    }
}
