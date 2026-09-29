namespace UasSort.Testing;

public sealed class FakeCardReaderFactory(FakeFileSystem fs) : ICardReaderFactory
{
    public int OpenCount { get; private set; }

    public ICardReader Open(CardSource source, CardIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(source);
        OpenCount++;
        return new FakeCardReader(fs, source.Root, identity);
    }
}

public sealed class FakeCardReader : ICardReader
{
    private readonly FakeFileSystem _fs;
    private readonly string _root;
    private int _identityCalls;

    public FakeCardReader(FakeFileSystem fs, string root, CardIdentity pinned)
    {
        _fs = fs;
        _root = PathRules.Normalize(root);
        Pinned = pinned;
    }

    public CardIdentity Pinned { get; }
    public int IdentityCalls => Volatile.Read(ref _identityCalls);

    internal static IOException NotReady() => new("The device is not ready.", unchecked((int)0x80070015));

    public CardIdentity CurrentIdentity()
    {
        if (_fs.Faults.CardRemoved) throw NotReady();
        var n = Interlocked.Increment(ref _identityCalls);
        return _fs.Faults.IdentityOnCall?.Invoke(n) ?? _fs.CardIdentityOf(_root);
    }

    public Stream OpenRandom(string cardRelPath) => Open(cardRelPath);

    public Stream OpenSequential(string cardRelPath) => Open(cardRelPath);

    public FsEntry Stat(string cardRelPath)
    {
        if (_fs.Faults.CardRemoved) throw NotReady();
        var full = PathRules.Join(_root, cardRelPath);
        return _fs.Metadata(full) is { IsDirectory: false } e
            ? e with { RelPath = cardRelPath.Replace('/', '\\') }
            : throw new FileNotFoundException("Not found", full);
    }

    public ListingResult Relist()
        => _fs.Faults.CardRemoved
            ? new ListingResult([], [(_root, 21)])
            : _fs.Enumerate(_root, recurse: true, ImmutableHashSet<string>.Empty);

    public CardSpace Space()
    {
        if (_fs.Faults.CardRemoved) throw NotReady();
        return _fs.CardSpaceOf(_root);
    }

    private FaultyCardStream Open(string cardRelPath)
    {
        if (_fs.Faults.CardRemoved) throw NotReady();
        var key = cardRelPath.Replace('\\', '/');
        var inner = _fs.OpenRead(PathRules.Join(_root, key), _fs.Context with { CardRoot = _root });
        return new FaultyCardStream(inner, _fs.Faults, key);
    }
}

public sealed class FakeCardEraserFactory(FakeFileSystem fs) : ICardEraserFactory
{
    private readonly List<FakeCardEraser> _erasers = [];

    /// <summary>False = one of the Win32 cleanup volume facts fails (not a volume root, wrong bus, no MISC index, …).</summary>
    public bool VolumeVerified { get; set; } = true;
    public int OpenCount { get; private set; }

    /// <summary>Every path deleted by every eraser this factory opened, in order ('/' card paths; removed folders end in '/').</summary>
    public IReadOnlyList<string> AllDeleted => [.. _erasers.SelectMany(e => e.Deleted)];

    public ICardEraser Open(CardSource source, CardIdentity pinned, ConfirmedCleanupPlan plan)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(plan);
        if (source.IsBrowsedFolder) throw new UnsafeIoException("Cleanup works only on a detected card volume");
        if (source.IsWriteProtected) throw new UnsafeIoException("The card is write-protected");
        if (!VolumeVerified) throw new UnsafeIoException("The volume failed the cleanup volume check");
        if (fs.Faults.CardRemoved) throw new UnsafeIoException("The card volume is gone");
        if (!PathRules.Equal(plan.Plan.CardRoot, source.Root) || plan.Plan.Card != pinned || fs.CardIdentityOf(source.Root) != pinned)
            throw new UnsafeIoException("The confirmed plan is for another card");
        OpenCount++;
        var root = PathRules.Normalize(source.Root);
        return Track(new FakeCardEraser(fs, root, fs.Context with { CardRoot = root, CardIsVerifiedCardVolume = true, Cleanup = plan }));
    }

    /// <summary>Tripwire tests only (Part 08): an eraser bound to any context, so a refused context can be exercised.
    /// Not counted in OpenCount.</summary>
    public ICardEraser OpenUnchecked(GuardContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        return Track(new FakeCardEraser(fs, PathRules.Normalize(ctx.CardRoot ?? FakeLayout.CardRoot), ctx));
    }

    private FakeCardEraser Track(FakeCardEraser eraser)
    {
        _erasers.Add(eraser);
        return eraser;
    }
}

public sealed class FakeCardEraser : ICardEraser
{
    private readonly FakeFileSystem _fs;
    private readonly string _root;
    private readonly GuardContext _ctx;
    private readonly List<string> _deleted = [];

    internal FakeCardEraser(FakeFileSystem fs, string root, GuardContext ctx)
    {
        _fs = fs;
        _root = root;
        _ctx = ctx;
    }

    public IReadOnlyList<string> Deleted => [.. _deleted];
    public bool IsDisposed { get; private set; }

    public EraseResult DeleteFile(string cardRelPath)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        var key = cardRelPath.Replace('\\', '/');
        var full = PathRules.Join(_root, key);
        if (_fs.Faults.CardRemoved) return new EraseError(21, "The device is not ready.");
        if (_fs.Metadata(full) is not { IsDirectory: false }) return new EraseError(2, "The system cannot find the file specified.");
        _fs.Guard(IoOp.CardDelete, full, _ctx);   // throws UnsafeIoException + records a CardDeleteViolation on refusal
        _fs.Faults.OnCardDelete?.Invoke(key);    // Part 08: outside the lock, before DeleteErrors and the delete apply
        lock (_fs.Gate)
        {
            if (_fs.Faults.DeleteErrors.TryGetValue(key, out var code)) return new EraseError(code, $"Win32 error {code}");
            var node = _fs.Require(full);
            if (node.DeletePending) return new EraseError(2, "The system cannot find the file specified.");
            if ((node.Attributes & IoGuardPolicy.FileAttributeReadOnly) != 0) return new EraseError(5, "Access is denied.");
            var volume = _fs.Volume(_root);
            var cluster = volume.Space.ClusterBytes;
            var allocated = node.Size == 0 ? 0 : (node.Size + cluster - 1) / cluster * cluster;
            if (_fs.Faults.DeletePending.Contains(key)) node.DeletePending = true;
            else _fs.RemoveTree(full);
            volume.Space = volume.Space with { FreeBytes = volume.Space.FreeBytes + allocated };
            _deleted.Add(key);
            return new EraseOk();
        }
    }

    public EraseResult RemoveEmptySetFolder(string cardRelDir)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        var key = cardRelDir.Replace('\\', '/').TrimEnd('/');
        var full = PathRules.Join(_root, key);
        if (_fs.Faults.CardRemoved) return new EraseError(21, "The device is not ready.");
        if (_fs.Metadata(full) is not { IsDirectory: true }) return new EraseError(3, "The system cannot find the path specified.");
        _fs.Guard(IoOp.CardDelete, full, _ctx);
        lock (_fs.Gate)
        {
            if (_fs.ChildrenOf(full).Count > 0) return new EraseError(145, "The directory is not empty.");
            _fs.RemoveTree(full);
            _deleted.Add(key + "/");
            return new EraseOk();
        }
    }

    public void Dispose()
    {
        IsDisposed = true;
        GC.SuppressFinalize(this);
    }
}
