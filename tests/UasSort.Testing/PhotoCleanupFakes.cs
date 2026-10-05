// tests/UasSort.Testing/PhotoCleanupFakes.cs
namespace UasSort.Testing;

/// <summary>IPhotoFileReader over the fake FS: every open asks the guard (IoOp.PhotoCleanupRead); a placeholder trips the hydration tripwire.</summary>
public sealed class FakePhotoFileReader(FakeFileSystem fs, GuardContext ctx) : IPhotoFileReader
{
    private readonly Lock _gate = new();
    private readonly List<string> _opened = [];

    public IReadOnlyList<string> Opened { get { lock (_gate) { return [.. _opened]; } } }

    public Stream OpenRead(string fullPath)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var p = fs.Guard(IoOp.PhotoCleanupRead, fullPath, ctx);
        lock (_gate) _opened.Add(p);
        return new MemoryStream(fs.PeekContent(p), writable: false);
    }
}

/// <summary>IPhotoRootRecyclerFactory over the fake FS: Open adds the plan to the base context; Recycle asks the guard
/// (IoOp.PhotoRootRecycle) and removes the item. Faults: NotRecyclable (the shell would delete permanently), Fails (access denied),
/// BeforeRecycle (runs first, e.g. to change a file or cancel), OpenThrows (the factory's own refusal).</summary>
public sealed class FakePhotoRootRecyclerFactory(FakeFileSystem fs, GuardContext baseContext) : IPhotoRootRecyclerFactory
{
    internal FakeFileSystem Fs { get; } = fs;
    public List<string> Recycled { get; } = [];
    public HashSet<string> NotRecyclable { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Fails { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The shell deletes it but nothing arrives in the Recycle Bin (PostDeleteItem with psiNewlyCreated NULL).</summary>
    public HashSet<string> RemovedNotInBin { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The shell reports a delete without a Recycle Bin item, yet the item is still there.</summary>
    public HashSet<string> ClaimsRemovedButStays { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Action<string>? BeforeRecycle { get; set; }
    public bool OpenThrows { get; set; }
    public int Opened { get; private set; }
    public int Disposed { get; private set; }

    public IPhotoRootRecycler Open(ConfirmedPhotoCleanupPlan plan)
    {
        if (OpenThrows) throw new UnsafeIoException("Picture Offload cleanup refused: fake factory refusal");
        Opened++;
        return new Recycler(this, baseContext with { PhotoCleanup = plan });
    }

    private sealed class Recycler(FakePhotoRootRecyclerFactory owner, GuardContext ctx) : IPhotoRootRecycler
    {
        public PhotoItemStat? Stat(string fullPath)
            => owner.Fs.Metadata(fullPath) is { } e ? new PhotoItemStat(e.Size, e.MtimeUtc, e.RawAttributes, e.IsDirectory) : null;

        public RecycleResult Recycle(string fullPath)
        {
            owner.BeforeRecycle?.Invoke(fullPath);
            var p = owner.Fs.Guard(IoOp.PhotoRootRecycle, fullPath, ctx);
            if (owner.NotRecyclable.Contains(p))
                return new RecycleError(0, "Windows would delete it permanently instead of moving it to the Recycle Bin; kept", true);
            if (owner.Fails.Contains(p)) return new RecycleError(5, "Access is denied.", false);
            if (owner.ClaimsRemovedButStays.Contains(p)) return new RecycleNotInBin("Windows removed it without putting it in the Recycle Bin");
            owner.Fs.RemoveUnguarded(p);
            if (owner.RemovedNotInBin.Contains(p)) return new RecycleNotInBin("Windows removed it without putting it in the Recycle Bin");
            owner.Recycled.Add(p);
            return new RecycleOk();
        }

        public void Dispose() => owner.Disposed++;
    }
}
