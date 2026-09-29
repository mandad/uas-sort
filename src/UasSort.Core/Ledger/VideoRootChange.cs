using UasSort.Core;

namespace UasSort.Core.Ledger;

/// <summary>Checks behind the video-root change prompt and the [Start empty] confirmation (main spec §8, Ref §11, §13).</summary>
public static class VideoRootChange
{
    public static bool HasRecords(LedgerSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return !snapshot.Files.IsEmpty || !snapshot.Decisions.IsEmpty || !snapshot.Seen.IsEmpty || !snapshot.Folders.IsEmpty
            || !snapshot.Runs.IsEmpty || !snapshot.CardDeletes.IsEmpty;
    }

    public static bool NeedsHistoryPrompt(LedgerFolderStatus newRootStatus, LedgerSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(newRootStatus);
        return newRootStatus.State is LedgerFolderState.Missing or LedgerFolderState.Empty && HasRecords(current);
    }

    public static int AppCopiedVideos(ListingResult newRootListing, string newVideoRoot, LedgerSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(newRootListing);
        ArgumentNullException.ThrowIfNull(current);
        string ledgerDir = LedgerPaths.For(newVideoRoot);
        return newRootListing.Entries.Count(e =>
            !e.IsDirectory
            && !PathRules.IsSameOrUnder(e.FullPath, ledgerDir)
            && current.Files.TryGetValue(FileKey.Of(PathRules.FileName(e.FullPath), e.Size), out LedgerFile? f)
            && f.Root == DestRoot.Video);
    }
}
