namespace UasSort.Platform.Io;

/// <summary>The only code that moves anything out of the photo root (spec 2026-10-04 §5): IoGuardPolicy.Check(PhotoRootRecycle) — in
/// the confirmed plan, directly in the photo root — then IFileOperation to the Recycle Bin on its own STA thread. Stat reads attributes only.</summary>
public sealed class WindowsPhotoRootRecycler : IPhotoRootRecycler
{
    private readonly GuardContext _ctx;
    private readonly StaWorker _sta = new("uas-sort Recycle Bin");
    private bool _disposed;

    internal WindowsPhotoRootRecycler(GuardContext ctx) => _ctx = ctx;

    public PhotoItemStat? Stat(string fullPath)
    {
        var path = Path.GetFullPath(fullPath);
        if (Kernel32.TryGetAttributeData(path, out var d, out var error))
            return new PhotoItemStat(d.Size, d.LastWriteUtc, d.FileAttributes, (d.FileAttributes & Kernel32.FILE_ATTRIBUTE_DIRECTORY) != 0);
        return Kernel32.IsNotFound(error) ? null : throw new IOException($"Stat {path} failed (Win32 error {error})", error);
    }

    public RecycleResult Recycle(string fullPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var path = Path.GetFullPath(fullPath);
        IoGate.Require(IoOp.PhotoRootRecycle, path, _ctx);
        if (path.Length >= FileOperationCom.MaxShellPath)
            return new RecycleError(206, "the path is too long for the Recycle Bin; kept", false);
        return _sta.Invoke(() => FileOperationCom.Recycle(path));
    }

    /// <summary>Branch-2 ruling: the Recycle Bin of the photo root's volume (registry + SHQueryRecycleBinW; nothing is opened).</summary>
    public RecycleBinCapacity Capacity(string fullPath) => RecycleBinQuery.Read(fullPath);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sta.Dispose();
    }
}

/// <summary>Re-derives the photo root from the saved settings (never trusts the plan's), refuses a plan for another photo folder, and
/// builds the recycler's own GuardContext with the plan and the Lightroom folder.</summary>
public sealed class WindowsPhotoRootRecyclerFactory(Func<Settings> settings, string appDataDir, string machine, IPathFacts facts)
    : IPhotoRootRecyclerFactory
{
    public IPhotoRootRecycler Open(ConfirmedPhotoCleanupPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var s = settings();
        var root = facts.Canonical(s.PhotoRoot);
        if (!PathRules.Equal(facts.Canonical(plan.PhotoRoot), root))
            throw new UnsafeIoException($"Picture Offload cleanup refused: the confirmed plan is for {plan.PhotoRoot}, the photo folder is {root}");
        if (!FolderFacts.Exists(root)) throw new UnsafeIoException($"Picture Offload cleanup refused: {root} is not available");
        if (PathRules.IsSameOrUnder(root, LedgerPaths.For(facts.Canonical(s.VideoRoot))))
            throw new UnsafeIoException("Picture Offload cleanup refused: the photo folder is inside the history folder");
        return new WindowsPhotoRootRecycler(GuardContexts.ForPhotoCleanup(s, appDataDir, machine, facts, plan));
    }
}
