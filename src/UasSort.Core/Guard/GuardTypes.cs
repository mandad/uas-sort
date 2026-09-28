// src/UasSort.Core/Guard/GuardTypes.cs
namespace UasSort.Core;

public enum IoOp
{
    ReadData, AppendOwnLedger, CreateNew, CreateDir, SetPinned, SetAttributesOrTimes, Delete, Rename, OpenForFlush,
    CardDelete /* Card cleanup only (Ref §10.6): a card file, or an emptied set folder */,
}

public sealed record GuardContext(string VideoRoot, string PhotoRoot, ImmutableArray<string> PreviousPhotoRoots, string? CardRoot,
    string AppDataDir /* %LOCALAPPDATA%\uas-sort */, string Machine,
    IReadOnlySet<string> NewFolderDirs /* the run's set as given (OffloadCompiler.NewFolderDirs: incl. YYYY, YYYY-MM parents); no ancestor expansion */,
    IReadOnlySet<string> OwnTempsThisRun, IReadOnlySet<string> RenamedThisRun,   // all canonical, compared case-insensitively
    string SystemVolumeRoot /* e.g. C:\ */,
    bool CardIsVerifiedCardVolume /* true only in the GuardContext that the eraser factory builds after its volume check */,
    ConfirmedCleanupPlan? Cleanup /* set only in the eraser's own context while CleanupExecutor runs; null everywhere else */);

public sealed record CardDeleteViolation(string Path, IoOp Op, string Reason);   // FakeFileSystem.CardDeleteViolations

public sealed record GuardAllow;
public sealed record GuardUnsafe(string Reason);
public sealed record GuardCloudOnly(string Path);
public sealed record GuardHydration(string Path, uint Attributes);
public union GuardDecision(GuardAllow, GuardUnsafe, GuardCloudOnly, GuardHydration);
