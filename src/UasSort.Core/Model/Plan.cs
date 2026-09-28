namespace UasSort.Core;

public sealed record Tuning(double RadiusMiles = 50, int GapDays = 1);
public readonly record struct GroupId(ItemId Anchor);           // earliest video; stable across re-derivations
public enum BoundaryCause { DayGap, Distance, LibraryFolder, UserSplit }
public sealed record Boundary(GroupId Left, GroupId Right, BoundaryCause Cause, Distance? Jump, TimeSpan Gap, int DayGap);
public sealed record DaySplit(ItemId FirstOfDay, DateOnly From, DateOnly To, Distance? Apart, TimeSpan Gap, bool Emphasised);
public enum Confidence { High, Medium }

public closed record class GroupTarget;
public sealed record class AlreadyImported(LibraryFolderRef Folder) : GroupTarget;
public sealed record class NothingToCopy(string Summary) : GroupTarget;
public sealed record class NewFolder(string RelPath) : GroupTarget;   // @"2026\2026-09\2026-09-27 Zachar Bay"
public sealed record class Append(LibraryFolderRef Folder, Confidence Confidence, string Why, CrossDayHint? Hint) : GroupTarget;
public sealed record class SkipGroup() : GroupTarget;

public sealed record CrossDayHint(string Text, ImmutableArray<PlanEdit> Fix);
public enum DescSource { ExistingFolder, Ledger, Feature, Place, Town, User, None }
public sealed record Suggestion(string Text, DescSource Source, Distance? Away, DateOnly? ForDay);
public sealed record PinState(bool MembershipChanged, int PinnedCount, int NowCount);
public sealed record VideoGroup(GroupId Id, ImmutableArray<ItemId> Videos, GeoPoint? Centroid, Distance Spread,
    DateOnly Start, DateOnly End, LibraryFolderRef? Wall, GroupTarget Target, PinState? TargetPin,
    string Description, DescSource DescSource, bool DescriptionEditable, PinState? NamePin,
    ImmutableArray<Suggestion> Suggestions, ImmutableArray<DaySplit> DaySplits, ImmutableArray<string> Hints,
    bool Foldable, int ColorIndex);
public enum SetResolution { Plain, Resume, DateSuffixed, Imported }
public sealed record SetPlacement(ItemId Set, string FolderName, SetResolution Resolution, ImmutableArray<string> MembersToCopy);
public sealed record PhotoDay(DateOnly Date, string TzId, ImmutableArray<ItemId> Items, string Reason);
public enum IssueSeverity { Blocking, Warning, Info }

public enum IssueCode                       // the closed catalogue of Ref §9.10; tests assert on codes, never on message text
{
    // Planner.Derive (Part 06 Task 06.1 appends NothingNew to this group)
    EmptyFolderName, TempPathTooLong, MediumAppend, EmphasisedDaySplit, PinMembershipChanged, ConflictingPins, SharedTarget,
    FolderExistsAppending, NewBeforeWallFolder, CheckDate, ClockNotSet, ClockMismatch, RootMissing, RootsUnconfirmed,
    LedgerParseIssue, LedgerCloudOnly, LedgerUnwritable, LedgerNotPinned, LedgerNoHistory,
    // PlanSession.Resume and the Settings page
    StaleEditsDropped, NoHistoryInNewRoot,
    // Preflight.Check
    CardIdentityChanged, CardUnreadable, OffloadLockHeld, AppendTargetGone, LowDiskSpace, DuplicateDestination, LedgerUnlistable,
    StaleTempFiles, DestinationAlreadyThere, AssumptionsTicked, ProbablyImportedLeftOut, NewItemsUnticked, UnfinishedRecordings,
    ConflictsLeftOut,
}

public sealed record Issue(IssueSeverity Severity, IssueCode Code, string Message, ItemId? Anchor,
                           ImmutableArray<QuickFix> QuickFixes, bool RequiresAckAtPreflight);
public sealed record QuickFix(string Label, ImmutableArray<PlanEdit> Edits);     // applied as ONE undo entry; Edits empty = a UI action
public sealed record SessionFlags(bool LedgerIssuesAccepted);                     // per session, never persisted in drafts

public sealed record ScanResult(CardInventory Inventory, ImmutableArray<RawItem> Raw, LibraryIndex Library,
                                LedgerSnapshot Ledger, ClockModel Clock, ImmutableArray<ScanWarning> Warnings, Settings Settings);
public sealed record PlanBase(ScanResult Scan, ImmutableArray<Item> Items, ImmutableArray<PhotoDay> PhotoDays,
                              ImmutableDictionary<ItemId, SetPlacement> Sets, ClockSummary Clock, DateTime? WatermarkUtc); // tuning-independent
public sealed record Plan(int Revision, PlanBase Base, Tuning Tuning, ImmutableArray<VideoGroup> Groups,
                          ImmutableArray<Boundary> Boundaries, ImmutableHashSet<ItemId> Included, ImmutableArray<Issue> Issues);
public sealed record GroupDraft(GroupId Id, ImmutableArray<Item> Videos, GeoPoint? Centroid, DateOnly Start, DateOnly End,
                                LibraryFolderRef? Wall, GroupId? UserSplitNeighbour);
public sealed record ClusterResult(ImmutableArray<GroupDraft> Groups, ImmutableArray<Boundary> Boundaries);
