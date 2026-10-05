using System.Text.Json.Serialization;

namespace UasSort.Core;

// One JSON line each; fields mirror Ref §11 one for one, camelCase (LedgerJsonContext, Task 02.5).
[JsonPolymorphic(TypeDiscriminatorPropertyName = "t")]
[JsonDerivedType(typeof(FileRecord), "file")]
[JsonDerivedType(typeof(FolderRecord), "folder")]
[JsonDerivedType(typeof(SeenRecord), "seen")]
[JsonDerivedType(typeof(DecisionRecord), "decision")]
[JsonDerivedType(typeof(RevokeRecord), "revoke")]
[JsonDerivedType(typeof(RunRecord), "run")]
[JsonDerivedType(typeof(TornRecord), "torn")]
[JsonDerivedType(typeof(CardDeleteRecord), "cardDelete")]
[JsonDerivedType(typeof(PhotoDeleteRecord), "photoDelete")]
public closed record class LedgerRecord(int V, string Id, string Machine);

public sealed record class FileRecord(int V, string Id, string Machine, string Run, DateTime At, string Kind /* video|photo|twin|setMember */,
    string Name, long Size, string Src, string Root /* video|photo */, string Dest, string? Xxh128, string Verify /* unbuffered|cached|nameSize */,
    DateTime Mtime, DateTime? CaptureUtc, string? TimeSource, double? Lat, double? Lon, string? Tz, DateOnly? LocalDate,
    DateTime? SessionUtc, string? Serial, string? Set) : LedgerRecord(V, Id, Machine);

public sealed record class FolderRecord(int V, string Id, string Machine, string Run, string Path, string Desc,
    string Source /* created|appended|cardLeftovers */, double? Lat, double? Lon, DateOnly Start, DateOnly End, string Tz) : LedgerRecord(V, Id, Machine);

public sealed record class SeenRecord(int V, string Id, string Machine, string Run, DateTime At, string Name, long Size, string Src,
    DateTime? CaptureUtc, string Status /* New|Conflict */, string Why, string? Set) : LedgerRecord(V, Id, Machine);

public sealed record class DecisionRecord(int V, string Id, string Machine, string? Run, DateTime At, string Kind /* assumedImported|dismissed */,
    string Name, long Size, string Src, DateTime? CaptureUtc, string Why, string? Set) : LedgerRecord(V, Id, Machine);

public sealed record class RevokeRecord(int V, string Id, string Machine, DateTime At, string Decision /* the decision's id */) : LedgerRecord(V, Id, Machine);

public sealed record class RunRecord(int V, string Id, string Machine, string Run, DateTime Start, DateTime End, string App, RunCard Card,
    RunRoots Roots, string Verdict, ImmutableDictionary<string, int> Counts) : LedgerRecord(V, Id, Machine);

public sealed record RunCard(string Serial, string? Label, string Fs, string? Model, string InventoryHash);
public sealed record RunRoots(string Video, string Photo);

public sealed record class TornRecord(int V, string Id, string Machine, DateTime At, int Line) : LedgerRecord(V, Id, Machine);

public sealed record class CardDeleteRecord(int V, string Id, string Machine, string Run, DateTime At, string Name, long Size,
    string Src /* card rel path */, string Unit /* ItemId */, DateTime? CaptureUtc,
    string Evidence /* VerifiedThisRun|InLedger|NameSizeMatch, "historyOnly:" prefix, notInLibraryConfirmed, companionOf:<evidence> */,
    string Reason, string Mode /* beforeDate|freeSpace */, RunCard Card, string? Set) : LedgerRecord(V, Id, Machine);

/// <summary>Picture Offload cleanup (spec 2026-10-04 §6): one per file moved from the photo root to the Recycle Bin, written after the move.
/// Evidence: lightroom|hyperlapseResult|panoramaStitch|dateOnly|unverifiedConfirmed. Mode: beforeDate|verify.</summary>
public sealed record class PhotoDeleteRecord(int V, string Id, string Machine, string Run, DateTime At, string Name, long Size,
    string Dest /* the Picture Offload path that went to the Recycle Bin */, DateTime? CaptureUtc, string? Set /* the card set name */,
    string Evidence, string Mode, DateOnly Cutoff) : LedgerRecord(V, Id, Machine);
