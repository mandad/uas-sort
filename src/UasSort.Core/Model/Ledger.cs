namespace UasSort.Core;

public enum VerifyKind { Unbuffered, Cached, NameSize }
public enum FolderSource { Created, Appended, CardLeftovers }

public sealed record LedgerFile(FileKey Key, string Src, DestRoot Root, string Dest, UInt128? Xxh128, VerifyKind Verify, DateTime AtUtc,
                                DateTime? CaptureUtc, GeoPoint? Point, string? TzId, DateOnly? LocalDate, SessionKey? Session,
                                string? Set, string Machine, string Run);
public sealed record LedgerSet(string SetName, DateTime FirstFrameCaptureUtc, ImmutableArray<(string Member, long Size)> Members);
public sealed record LedgerDecision(string Id, FileKey Key, DecisionKind Kind, DateTime AtUtc, string Machine, string? Set, string Why);
public sealed record LedgerFolder(string Path, string Description, FolderSource Source, GeoPoint? Centroid,
                                  DateOnly Start, DateOnly End, string TzId);
public sealed record LedgerRun(string Run, string Machine, DateTime StartUtc, DateTime EndUtc, string App, CardIdentity Card,
                               string? Model, string InventoryHash, string VideoRoot, string PhotoRoot, VerdictLevel Verdict,
                               ImmutableDictionary<AuditCategory, int> Counts);
public sealed record LedgerCardDelete(string Run, DateTime AtUtc, FileKey Key, string Src, string Evidence, string Machine);
public sealed record LedgerParseIssue(string File, int Line, string Reason);

public enum LedgerFolderState { Ok, Empty, Missing, NotPinned, Unwritable, CloudOnly, VideoRootMissing }

public sealed record LedgerFolderStatus(string Folder, LedgerFolderState State /* the most severe that applies: VideoRootMissing >
    CloudOnly > Unwritable > NotPinned > Missing > Empty > Ok */, bool Exists, bool InSyncRoot, bool Pinned, bool Writable,
    ImmutableArray<string> LedgerFiles, ImmutableArray<string> CloudOnlyFiles, ImmutableArray<string> OtherMachineFiles);

public sealed record LedgerSnapshot(
    ImmutableDictionary<FileKey, LedgerFile> Files,           // strongest verify per key (Unbuffered > Cached > NameSize), latest among equals
    ImmutableDictionary<string, ImmutableArray<LedgerSet>> SetsByName,
    ImmutableDictionary<FileKey, LedgerDecision> Decisions,   // after applying revoke records; a set has one per member (Ref §7.3)
    ImmutableDictionary<FileKey, DateTime> Seen,              // a set has one per member (Ref §7.3)
    ImmutableDictionary<string, LedgerFolder> Folders,        // by full path (case-insensitive)
    ImmutableArray<LedgerRun> Runs,
    ImmutableArray<LedgerCardDelete> CardDeletes,             // Card cleanup's audit trail; informational, read by no rule
    ImmutableArray<LedgerParseIssue> ParseIssues,
    ImmutableArray<string> SourceFiles, LedgerFolderStatus Status);

public interface ILedgerWriter : IDisposable       // from ILedgerStore.OpenOwn(); one instance per Commit or user action
{
    void Append(LedgerRecord r);                   // one line + "\n", FlushFileBuffers, then the same line to the local mirror;
}                                                  // throws on any failure (Commit stops, Ref §10.3)
