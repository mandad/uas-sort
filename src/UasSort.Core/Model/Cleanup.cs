namespace UasSort.Core;

public enum CleanupMode { BeforeDate, FreeSpace }
public enum FreeSpaceKind { HaveFree /* default: target = Bytes */, FreeUp /* target = free + Bytes */ }

public sealed record FreeSpaceGoal(FreeSpaceKind Kind, long Bytes /* decimal GB × 10^9 */)
{
    public long TargetFreeBytes(CardSpace s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return Kind == FreeSpaceKind.FreeUp ? s.FreeBytes + Bytes : Math.Min(Bytes, s.TotalBytes);
    }
}

public sealed record CleanupRequest(CleanupMode Mode,
                                    DateOnly? Before /* BeforeDate: site-local day, kept */,
                                    FreeSpaceGoal? Goal /* FreeSpace */, bool IncludeNotInLibrary);
public enum CleanupEligibility { Evidence, NotInLibrary, Never }       // ascending strictness; a unit takes the worst of its files
public enum EvidenceSource { Listed, HistoryOnly }
public enum NotInLibraryReason { New, ProbablyImported, Conflict, Unfinished, Dismissed, RecordedAsImported, NoLongerInLibrary }
public sealed record CardSpace(long FreeBytes, long TotalBytes, int ClusterBytes);   // GetDiskFreeSpaceExW + GetDiskFreeSpaceW
public sealed record FileProof(string CardRelPath, FileKey Key, AuditCategory Category, string? ListedFolder,
                               bool LedgerVerified, string? DecisionId);
public sealed record CleanupCandidate(ItemId Unit, ImmutableArray<CardEntry> Files /* incl. twin and companions, in delete order */,
    long AllocatedBytes /* Σ size rounded up to ClusterBytes */, DateTime CaptureUtc, DateOnly LocalDate, string TzId,
    CleanupEligibility Eligibility, AuditCategory Evidence /* worst category of the primary files */, string Reason,
    TimeSpan? Duration, GeoPoint? Location, string? PlaceLabel /* "near Anvil Mountain · 0.2 mi" */,
    ItemKind Kind, DateTime LocalTime, NotInLibraryReason? NotInLibrary, string? SetFolder /* card rel dir, removed once empty */,
    SessionKey? Session, ImmutableArray<FileProof> Proofs /* one per primary file */,
    EvidenceSource? Source /* Evidence units */, bool TickedForOffload,
    ImmutableArray<CardEntry> NeverCopied /* companions and uncopied JPG twins, for the summary line */);
public sealed record CleanupKept(ItemId? Unit, ImmutableArray<string> CardRelPaths, long Bytes, DateTime? CaptureUtc, string Reason);
public sealed record CleanupCutoff(DateOnly? BeforeDate, DateTime? LastCaptureUtc, DateTime? LastLocalTime, string? TzId,
                                   int FilesDeletedOnCutoffDay, int FilesOnCutoffDay, DateTime? FlightContinuesLocal);
public sealed record FreeSpaceShortfall(long FreeableBytes, long HeldByNotInLibrary, long HeldByNever);
public sealed record CleanupInputs(CardInventory Inventory, Plan Plan, FormatVerdict Audit, OffloadResult? Offload, CardSpace Space,
                                   LibraryListings FreshListings, LedgerSnapshot FreshLedger,
                                   VolumeInfo Volume, IPlaceIndex? Places, Settings Settings);
public sealed record CleanupRows(ImmutableHashSet<ItemId> Keep, ImmutableHashSet<ItemId> Delete /* the rest in range are undecided */);

/// <summary>A class, not a record: no `with`. Only CleanupPlanner.Build (Part 08) calls the 18-argument constructor, passing
/// the inventory's InventoryHash and CameraModel; Confirm(CleanupAck, TimeProvider) is added by Part 08 in
/// Cleanup/CleanupPlan.Confirm.cs (partial, namespace UasSort.Core).</summary>
public sealed partial class CleanupPlan
{
    internal CleanupPlan(string planId, CardIdentity card, string cardRoot, string inventoryHash, string? cameraModel,
        CleanupRequest request, CardSpace spaceBefore,
        ImmutableArray<CleanupCandidate> delete, ImmutableArray<CleanupCandidate> notInLibraryInScope, CleanupRows rows,
        ImmutableHashSet<ItemId> undecided, ImmutableArray<CleanupKept> notDeletable, CleanupCutoff cutoff, int fileCount,
        long allocatedBytes, long expectedFreeAfter, FreeSpaceShortfall? shortfall, string fingerprint)
    {
        PlanId = planId; Card = card; CardRoot = cardRoot; InventoryHash = inventoryHash; CameraModel = cameraModel;
        Request = request; SpaceBefore = spaceBefore;
        Delete = delete; NotInLibraryInScope = notInLibraryInScope; Rows = rows; Undecided = undecided;
        NotDeletable = notDeletable; Cutoff = cutoff; FileCount = fileCount; AllocatedBytes = allocatedBytes;
        ExpectedFreeAfter = expectedFreeAfter; Shortfall = shortfall; Fingerprint = fingerprint;
    }

    public string PlanId { get; }
    public CardIdentity Card { get; }
    public string CardRoot { get; }
    public string InventoryHash { get; }                                    // the scanned inventory the plan was built from
    public string? CameraModel { get; }                                     // CardInventory.CameraModel (cardDelete records' RunCard)
    public CleanupRequest Request { get; }
    public CardSpace SpaceBefore { get; }
    public ImmutableArray<CleanupCandidate> Delete { get; }                 // oldest first
    public ImmutableArray<CleanupCandidate> NotInLibraryInScope { get; }    // the review list
    public CleanupRows Rows { get; }
    public ImmutableHashSet<ItemId> Undecided { get; }                      // rows that joined later, not yet set
    public ImmutableArray<CleanupKept> NotDeletable { get; }                // the "Kept" list
    public CleanupCutoff Cutoff { get; }
    public int FileCount { get; }
    public long AllocatedBytes { get; }
    public long ExpectedFreeAfter { get; }
    public FreeSpaceShortfall? Shortfall { get; }
    public string Fingerprint { get; }                                      // for binding the checkboxes; Confirm recomputes it
}

public sealed record CleanupAck(string PlanFingerprint, bool CantBeRecovered, bool IncludesNotInLibrary,
                                ImmutableHashSet<ItemId> NotInLibraryDelete /* review rows left on Delete */);

/// <summary>A class, not a record, so `with` can't copy it; the constructor is internal to Core (Confirm is the only production
/// factory). This derivation (PathRules.Join, OrdinalIgnoreCase) is the canonical one; Part 08 never re-derives the sets.</summary>
public sealed class ConfirmedCleanupPlan
{
    internal ConfirmedCleanupPlan(CleanupPlan plan, Guid token, DateTime confirmedUtc)
    {
        ArgumentNullException.ThrowIfNull(plan);
        Plan = plan;
        Token = token;
        ConfirmedUtc = confirmedUtc;
        var files = new List<string>();
        var sets = new List<string>();
        var notInLibrary = new List<ItemId>();
        foreach (var c in plan.Delete)
        {
            foreach (var f in c.Files) files.Add(PathRules.Join(plan.CardRoot, f.RelPath));
            if (c.SetFolder is { } folder) sets.Add(PathRules.Join(plan.CardRoot, folder));
            if (c.Eligibility == CleanupEligibility.NotInLibrary) notInLibrary.Add(c.Unit);
        }
        FilePaths = files.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        SetFolders = sets.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        NotInLibraryConfirmed = notInLibrary.ToImmutableHashSet();
    }

    public CleanupPlan Plan { get; }
    public Guid Token { get; }
    public DateTime ConfirmedUtc { get; }
    public IReadOnlySet<string> FilePaths { get; }              // canonical card paths the guard allows for CardDelete
    public IReadOnlySet<string> SetFolders { get; }             // canonical set folders: RemoveDirectory only, once empty
    public IReadOnlySet<ItemId> NotInLibraryConfirmed { get; }  // the per-unit confirmation tokens of NotInLibrary units
}

// outcome per unit; the cases that CopyOutcome also has are prefixed "Cleanup" to keep the names apart
public closed record class CleanupOutcome(ItemId Unit);
public sealed record class Deleted(ItemId Unit, int Files, long Bytes, bool SetFolderRemoved) : CleanupOutcome(Unit);
public sealed record class SkippedChanged(ItemId Unit, string CardRelPath, long? NowSize /* null = gone */, DateTime? NowMtimeUtc) : CleanupOutcome(Unit);
public sealed record class SkippedEvidenceGone(ItemId Unit, string CardRelPath, string Why) : CleanupOutcome(Unit);
public sealed record class PartiallyDeleted(ItemId Unit, ImmutableArray<string> DeletedPaths, ImmutableArray<string> StillOnCard, string Why) : CleanupOutcome(Unit);
public sealed record class CleanupFailed(ItemId Unit, string CardRelPath, int Win32Error, string Error) : CleanupOutcome(Unit);
public sealed record class CleanupNotStarted(ItemId Unit) : CleanupOutcome(Unit);
public sealed record class CleanupCardSwapped(ItemId Unit, CardIdentity Now) : CleanupOutcome(Unit);

public enum CleanupStop
{
    OffloadLockHeld, LedgerUnavailable, Cancelled, CardSwapped, CardRemoved, WriteProtected, LedgerWriteFailed, InternalSafetyStop,
}

public sealed record CleanupEnvironment(CardSource Source, CardIdentity Pinned, ICardReader Reader, IThumbnailSource Thumbnails,
    ICardEraserFactory Erasers, IDirectoryLister Lister, ILedgerStore Ledger, IOffloadLock Lock, IPowerRequest Power,
    TimeProvider Clock, Settings Settings);

public sealed record CleanupResult(string RunId, ConfirmedCleanupPlan Plan, ImmutableArray<CleanupOutcome> Outcomes,
    CleanupStop? Stop /* null = ran to the end */, CardSpace SpaceAfter /* re-read */,
    ImmutableArray<string> StillListed /* deleted, yet present at the re-list */,
    DateTime StartUtc, DateTime EndUtc);

public sealed record CleanupProgress(int FilesDone, int FilesTotal, long BytesDone, long BytesTotal, string? CurrentFile, ItemId? Unit);
