// src/UasSort.Core/Guard/GuardTypes.cs
namespace UasSort.Core;

public enum IoOp
{
    ReadData, AppendOwnLedger, CreateNew, CreateDir, SetPinned, SetAttributesOrTimes, Delete, Rename, OpenForFlush,
    CardDelete /* Card cleanup only (Ref §10.6): a card file, or an emptied set folder */,
    PhotoCleanupRead /* Picture Offload cleanup only: read EXIF of a photo-root file or a Lightroom library file (spec 2026-10-04 §3–§4) */,
    PhotoRootRecycle /* Picture Offload cleanup only: move a confirmed photo-root item to the Recycle Bin (spec 2026-10-04 §5) */,
}

public sealed record GuardContext(string VideoRoot, string PhotoRoot, ImmutableArray<string> PreviousPhotoRoots, string? CardRoot,
    string AppDataDir /* %LOCALAPPDATA%\uas-sort */, string Machine,
    IReadOnlySet<string> NewFolderDirs /* the run's set as given (OffloadCompiler.NewFolderDirs: incl. YYYY, YYYY-MM parents); no ancestor expansion */,
    IReadOnlySet<string> OwnTempsThisRun, IReadOnlySet<string> RenamedThisRun,   // all canonical, compared case-insensitively
    string SystemVolumeRoot /* e.g. C:\ */,
    bool CardIsVerifiedCardVolume /* true only in the GuardContext that the eraser factory builds after its volume check */,
    ConfirmedCleanupPlan? Cleanup /* set only in the eraser's own context while CleanupExecutor runs; null everywhere else */)
{
    /// <summary>Settings.LightroomFolder (canonical) in Picture Offload cleanup's contexts: readable with IoOp.PhotoCleanupRead only.</summary>
    public string? LightroomFolder { get; init; }

    /// <summary>Set only in the photo-root recycler's own context (spec 2026-10-04 §5); null everywhere else.</summary>
    public ConfirmedPhotoCleanupPlan? PhotoCleanup { get; init; }
}

public sealed record CardDeleteViolation(string Path, IoOp Op, string Reason);   // FakeFileSystem.CardDeleteViolations

public sealed record GuardAllow;
public sealed record GuardUnsafe(string Reason);
public sealed record GuardCloudOnly(string Path);
public sealed record GuardHydration(string Path, uint Attributes);
public union GuardDecision(GuardAllow, GuardUnsafe, GuardCloudOnly, GuardHydration);
