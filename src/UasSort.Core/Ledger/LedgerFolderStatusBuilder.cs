// src/UasSort.Core/Ledger/LedgerFolderStatusBuilder.cs
using UasSort.Core;

namespace UasSort.Core.Ledger;

public sealed record LedgerFolderFacts(bool VideoRootExists, FsEntry? Folder, ListingResult? TopLevel, bool InSyncRoot, bool Writable);

/// <summary>Attributes-only folder status (Ref §11): computed before any ledger file is opened.</summary>
public static class LedgerFolderStatusBuilder
{
    public const uint PinnedBit = 0x80000;
    public const uint CloudBits = 0x400000 | 0x40000 | 0x1000;

    public static LedgerFolderStatus Build(string videoRoot, string machine, LedgerFolderFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        string folder = LedgerPaths.For(videoRoot);
        bool exists = facts.VideoRootExists && facts.Folder is { IsDirectory: true };
        bool pinned = exists && (facts.Folder!.RawAttributes & PinnedBit) != 0;

        ImmutableArray<FsEntry> ledgerEntries = exists && facts.TopLevel is { } top
            ? [.. top.Entries
                  .Where(e => !e.IsDirectory
                              && PathRules.Parent(e.FullPath) is { } parent && PathRules.Equal(parent, folder)
                              && LedgerPaths.IsLedgerFileName(PathRules.FileName(e.FullPath)))
                  .OrderBy(e => e.FullPath, StringComparer.OrdinalIgnoreCase)]
            : [];

        string own = LedgerPaths.OwnFile(videoRoot, machine);
        ImmutableArray<string> files = [.. ledgerEntries.Select(e => e.FullPath)];
        ImmutableArray<string> cloudOnly = [.. ledgerEntries.Where(e => (e.RawAttributes & CloudBits) != 0).Select(e => e.FullPath)];
        ImmutableArray<string> others = [.. files.Where(f => !PathRules.Equal(f, own))];

        LedgerFolderState state =
            !facts.VideoRootExists ? LedgerFolderState.VideoRootMissing
            : !cloudOnly.IsEmpty ? LedgerFolderState.CloudOnly
            : !facts.Writable ? LedgerFolderState.Unwritable
            : exists && facts.InSyncRoot && !pinned ? LedgerFolderState.NotPinned
            : !exists ? LedgerFolderState.Missing
            : files.IsEmpty ? LedgerFolderState.Empty
            : LedgerFolderState.Ok;

        return new LedgerFolderStatus(folder, state, exists, facts.InSyncRoot, pinned, facts.Writable, files, cloudOnly, others);
    }
}
