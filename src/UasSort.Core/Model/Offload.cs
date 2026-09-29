namespace UasSort.Core;

public enum DestRoot { Video, Photo }

public sealed record CopyJob(ItemId Item, string CardRelPath, long Size, DateTime CardMtimeUtc, DateTime CardCreationUtc,
                             string DestPath, DestRoot Root, GroupId? Group, bool CreatesFolder);
public enum VerifyMode { Unbuffered, Cached }
public enum CopyPhase { CardCheck, Stat, CreateTemp, Copy, Flush, Verify, Finalize, Rename, Confirm, Ledger }
public sealed record HashMatch(VerifyMode Mode);
public sealed record HashMismatch(UInt128 Got, VerifyMode Mode);
public union VerifyResult(HashMatch, HashMismatch);
public sealed record Renamed;
public sealed record TargetExists;
public union RenameResult(Renamed, TargetExists);

public closed record class CopyOutcome(CopyJob Job);
public sealed record class Verified(CopyJob Job, UInt128 Hash, VerifyMode Mode) : CopyOutcome(Job);
public sealed record class AlreadyThere(CopyJob Job) : CopyOutcome(Job);
public sealed record class ConflictAtRename(CopyJob Job) : CopyOutcome(Job);
public sealed record class ChangedOnCard(CopyJob Job, long NowSize, DateTime NowMtimeUtc) : CopyOutcome(Job);
public sealed record class CardSwapped(CopyJob Job, CardIdentity Now) : CopyOutcome(Job);
public sealed record class Failed(CopyJob Job, CopyPhase Phase, string Error) : CopyOutcome(Job);
public sealed record class Cancelled(CopyJob Job) : CopyOutcome(Job);
public sealed record class NotStarted(CopyJob Job) : CopyOutcome(Job);

public sealed record OffloadBatch(string RunId, CardIdentity Card, ImmutableArray<CopyJob> Jobs,
                                  ImmutableArray<ItemId> SeenIfNotCopied, ImmutableArray<FolderPlan> Folders);
public sealed record FolderPlan(GroupId Group, string FullPath, bool Create, string Description, GeoPoint? Centroid,
                                DateOnly Start, DateOnly End, string TzId);
public enum StopReason { Cancelled, CardSwapped, CardRemoved, DestinationFull, DestinationLost, LedgerWriteFailed, InternalSafetyStop }
public sealed record OffloadResult(string RunId, ImmutableArray<CopyOutcome> Outcomes, StopReason? Stop /* null = ran to the end */,
                                   DateTime StartUtc, DateTime EndUtc, ImmutableArray<string> VolumesNeedingSafeRemoval)
{
    /// <summary>A ledger append after the loop failed (a folder record of a group the stop cut short); the stop reason is kept.</summary>
    public bool LedgerIncomplete { get; init; }
}

public sealed record VolumeNeed(string Volume, int Files, long Bytes, long FreeBytes, long RequiredFree);   // RequiredFree = Σ + max(1 GiB, 2 % of Σ)
public sealed record PreflightReport(ImmutableArray<Issue> Issues, ImmutableArray<string> FoldersToCreate,
                                     ImmutableArray<(string Path, Confidence Confidence)> FoldersAppended, ImmutableArray<VolumeNeed> Volumes,
                                     ImmutableArray<string> StaleTemps /* listed only; deleted at Start offload */,
                                     ImmutableArray<ItemId> AlreadyThere)
{
    public bool CanStart => !Issues.Any(i => i.Severity == IssueSeverity.Blocking);
}

public sealed record Ejected(string Volume);
public sealed record EjectRefused(string Volume, string Reason);
public union EjectResult(Ejected, EjectRefused);

public enum ScanPhase { ListingCard, ListingLibrary, ReadingLedger, ReadingMetadata, BuildingPlan }
public sealed record ScanProgress(ScanPhase Phase, int Done, int Total, string? Current);
public sealed record OffloadProgress(int FilesDone, int FilesTotal, long BytesDone, long BytesTotal, double MBps /* rolling 5 s */,
                                     TimeSpan? Eta, string? CurrentFile, CopyPhase Phase, GroupId? Group);
