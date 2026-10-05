namespace UasSort.Core;

public interface IVolumeProvider { IReadOnlyList<VolumeInfo> GetVolumes(); }

public interface IDirectoryLister                                 // listing only; never opens a file
{
    // AttributesToSkip=0, IgnoreInaccessible=false, errors collected; a directory whose name is in excludeDirNames
    // (case-insensitive) is neither returned nor entered. Library roots pass {".uas-sort"}; the card, the ledger
    // store and stale-temp checks pass {}.
    ListingResult Enumerate(string root, bool recurse, IReadOnlySet<string> excludeDirNames);
}

public interface ICardSourceValidator                             // anchor + overlap + sync roots (Ref §4.3)
{
    // IsBrowsedFolder = detected is null, even when a Browse result is a volume root;
    // IsWriteProtected = detected?.IsReadOnlyVolume ?? false; Identity = detected?.Identity when detected is given
    CardSourceCheck Validate(string chosenPath, VolumeInfo? detected /* null = Browse to folder (and the CLI) */,
                             Settings s, IDirectoryLister lister, IPathFacts facts, string appDataDir);
}

public interface IPathFacts                                       // Platform: canonical paths and sync roots (Ref §4.3 steps 2–3)
{
    string Canonical(string path);                                // GetFullPath → BACKUP_SEMANTICS handle → GetFinalPathNameByHandleW
    bool InSyncRoot(string canonicalPath);                        // CfGetSyncRootInfoByPath succeeds
    IReadOnlyList<string> SyncRoots();                            // OneDrive UserFolder values + HKLM SyncRootManager roots
}

public interface ICardReader                                      // bound to one CardSource + CardIdentity; FileAccess.Read, FileShare.ReadWrite
{
    CardIdentity CurrentIdentity();                               // GetVolumeInformationW on the card root (cheap)
    Stream OpenRandom(string cardRelPath);                        // probes, thumbnails (4 KB block cache on top)
    Stream OpenSequential(string cardRelPath);                    // copy
    FsEntry Stat(string cardRelPath);
    ListingResult Relist();                                       // audit-time re-listing
    CardSpace Space();                                            // GetDiskFreeSpaceExW + GetDiskFreeSpaceW; opens nothing
}

public interface ICardReaderFactory { ICardReader Open(CardSource source, CardIdentity identity); }

public interface ICardEraserFactory                               // Platform; Card cleanup only (Ref §10.6); re-derives every volume fact
{                                                                 // from Win32 and builds the eraser's own GuardContext; throws UnsafeIoException
    ICardEraser Open(CardSource source, CardIdentity pinned, ConfirmedCleanupPlan plan);
}

public interface ICardEraser : IDisposable                        // IoGuardPolicy.Check(CardDelete, …) before every call
{
    EraseResult DeleteFile(string cardRelPath);                   // DeleteFileW(\\?\…); never clears attributes, never opens the file
    EraseResult RemoveEmptySetFolder(string cardRelDir);          // RemoveDirectoryW(\\?\…); fails on a non-empty folder; never recursive
}

public sealed record EraseOk;
public sealed record EraseError(int Win32Error, string Message);
public union EraseResult(EraseOk, EraseError);

public interface IFileOps                                         // destination writes only; guarded
{
    Stream CreateTemp(string finalPath, long size, out string tempPath);   // CreateNew + preallocate, Hidden|NotContentIndexed, "<final>.uas-sort.tmp"
    void FlushToDisk(Stream s);
    VerifyResult VerifyHash(string tempPath, long size, UInt128 expected, CancellationToken ct);
    void FinalizeAttributes(string tempPath, DateTime creationUtc, DateTime mtimeUtc);   // copy times, clear Hidden
    RenameResult RenameNoReplace(string tempPath, string finalPath);                       // MoveFileExW, never REPLACE_EXISTING
    bool ConfirmFinal(string finalPath, long size);                                        // metadata only
    void FlushDestination(string dir, IReadOnlyList<string> filesCreatedThisRun);          // non-NTFS / removable volumes
    void DeleteOwnTemp(string tempPath);
    void EnsureDirectory(string dir, bool allowCreate);           // allowCreate only for NewFolder paths and their YYYY/YYYY-MM parents
    bool TryGetSize(string path, out long size);
    long FreeBytes(string anyPathOnVolume);
}

public interface ILedgerStore                                     // folder = LedgerPaths.For(videoRoot) (derived, never configured)
{
    LedgerFolderStatus Check();                                   // attributes and security descriptors only, BEFORE any open
    LedgerSnapshot Load();                                        // union of every top-level ledger*.jsonl, deduped by id; honours torn
    void EnsureFolder();                                          // creates <videoRoot>\.uas-sort (folder only), then KeepOnDevice()
    ILedgerWriter OpenOwn();                                      // ledger-<MACHINE>.jsonl, append, FileShare.Read; torn-tail repair; mirrored
    void SnapshotToBackup(string runId);                          // BackupDir\snapshots\<yyyyMMdd-HHmmss>-<run8>\ (keeps 20)
    void KeepOnDevice();                                          // FILE_ATTRIBUTE_PINNED on the .uas-sort folder only
    void CopyInto(string newVideoRoot, LedgerSnapshot current);   // video-root change [Copy]
}

#pragma warning disable CA1716 // registry-mandated parameter name (Ref 3): readOnly
public interface ISettingsStore { SettingsLoad Load(bool readOnly = false); void Save(Settings s); }
#pragma warning restore CA1716
public interface IDraftStore { Draft? Load(string cardKey); void Save(string cardKey, Draft d); void Delete(string cardKey); }
public interface IReportStore { string Save(OffloadReport r); string Save(CleanupReport r); string Save(PhotoCleanupReport r); }
public interface IAppAssets { Stream OpenPlaces(); Stream OpenSelfTest(string name); }   // Part 11 adds OpenMapAsset only at its fallback 2
public interface IPowerRequest { IDisposable KeepSystemAwake(string reason); }
public interface IOffloadLock { IDisposable? TryAcquire(); }                    // named mutex Local\uas-sort-offload
public interface IDeviceEject { EjectResult Eject(string volumeRoot); }
public interface IShellLauncher { void OpenFolder(string path); void OpenFile(string path); void OpenHttps(Uri uri); }
public interface ITimeZoneResolver { TzLookup Resolve(GeoPoint p); }
public interface IPlaceIndex { IReadOnlyList<PlaceHit> Near(GeoPoint p, Distance r, PlaceClass cls, int max); }

public interface IThumbnailSource                                 // bytes, not images
{
    ValueTask<ReadOnlyMemory<byte>> GetAsync(ItemId id, CancellationToken ct);
    IDisposable Pause();                                          // Commit and Card cleanup: closes the cached card handles
}

// ── Picture Offload cleanup (spec 2026-10-04 §5)
public interface IPhotoFileReader                                 // Platform: IoGuardPolicy.Check(PhotoCleanupRead, …) before every open;
{                                                                 // read-only, FileShare.ReadWrite | Delete; a placeholder is refused, never hydrated
    Stream OpenRead(string fullPath);
}

public sealed record PhotoItemStat(long Size, DateTime MtimeUtc, uint Attributes, bool IsDirectory);

public sealed record RecycleOk;
public sealed record RecycleError(int Code, string Message, bool NotRecyclable /* the shell would have deleted it permanently */);
public union RecycleResult(RecycleOk, RecycleError);

public interface IPhotoRootRecycler : IDisposable                 // IoGuardPolicy.Check(PhotoRootRecycle, …) before every move
{
    PhotoItemStat? Stat(string fullPath);                         // attributes only, never opens; null = gone
    RecycleResult Recycle(string fullPath);                       // one file or one set folder → the Recycle Bin; never a permanent delete
}

public interface IPhotoRootRecyclerFactory                        // Platform: re-derives the photo root from the saved settings and builds
{                                                                 // the recycler's own GuardContext with the plan; throws UnsafeIoException
    IPhotoRootRecycler Open(ConfirmedPhotoCleanupPlan plan);
}
