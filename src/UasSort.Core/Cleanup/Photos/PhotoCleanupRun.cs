// src/UasSort.Core/Cleanup/Photos/PhotoCleanupRun.cs
namespace UasSort.Core.Cleanup;

public enum PhotoCleanupStop { OffloadLockHeld, LedgerUnavailable, RecyclerRefused, Cancelled, LedgerWriteFailed, InternalSafetyStop }

/// <summary>Outcome per row set to Delete (spec 2026-10-04 §2 result page: moved / skipped-because-changed / failed).</summary>
public closed record class PhotoCleanupOutcome(string Item);
public sealed record class PhotoRecycled(string Item, int Files, long Bytes) : PhotoCleanupOutcome(Item)
{
    /// <summary>Members (relative paths) the shell removed without putting them in the Recycle Bin (deferred minor P.11); empty normally.</summary>
    public ImmutableArray<string> NotInRecycleBin { get; init; } = [];
}

public sealed record class PhotoSkippedChanged(string Item, string Why) : PhotoCleanupOutcome(Item);

public sealed record class PhotoPartlyRecycled(string Item, ImmutableArray<string> Recycled, ImmutableArray<string> Left, string Why) : PhotoCleanupOutcome(Item)
{
    /// <summary>Of <see cref="Recycled"/>: the members the shell removed without putting them in the Recycle Bin (deferred minor P.11).</summary>
    public ImmutableArray<string> NotInRecycleBin { get; init; } = [];
}

public sealed record class PhotoRecycleFailed(string Item, string Path, int Code, string Error, bool NotRecyclable) : PhotoCleanupOutcome(Item);
public sealed record class PhotoNotStarted(string Item) : PhotoCleanupOutcome(Item);

public sealed record PhotoCleanupProgress(int ItemsDone, int ItemsTotal, long BytesDone, long BytesTotal, string? Current);

public sealed record PhotoCleanupEnvironment(IPhotoRootRecyclerFactory Recyclers, IDirectoryLister Lister, ILedgerStore Ledger, IOffloadLock Lock,
                                             IPowerRequest Power, TimeProvider Clock, string Machine);

public sealed record PhotoCleanupResult(string RunId, ConfirmedPhotoCleanupPlan Plan, ImmutableArray<PhotoCleanupOutcome> Outcomes,
                                        PhotoCleanupStop? Stop /* null = ran to the end */,
                                        ImmutableArray<string> Unrecorded /* moved, but the photoDelete record couldn't be written */,
                                        DateTime StartUtc, DateTime EndUtc);
