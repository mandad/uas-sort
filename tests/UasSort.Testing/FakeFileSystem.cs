namespace UasSort.Testing;

public sealed record FakeGuardCall(IoOp Op, string Path, string Decision);

internal sealed class FakeNode(string path, bool isDirectory, uint attributes, DateTime mtimeUtc)
{
    public string Path { get; set; } = path;
    public bool IsDirectory { get; } = isDirectory;
    public uint Attributes { get; set; } = attributes;
    public DateTime MtimeUtc { get; set; } = mtimeUtc;
    public DateTime CreationUtc { get; set; } = mtimeUtc;
    public DateTime LastAccessUtc { get; set; } = mtimeUtc;
    public byte[]? Bytes { get; set; }
    public long PatternLength { get; set; }
    public uint PatternSeed { get; set; }
    public bool DeletePending { get; set; }
    public long Size => IsDirectory ? 0 : Bytes?.LongLength ?? PatternLength;

    public Stream OpenContent() => Bytes is { } b ? new MemoryStream(b, writable: false) : new PatternStream(PatternLength, PatternSeed);

    public byte[] Materialize()
    {
        if (Bytes is null)
        {
            var b = new byte[PatternLength];
            for (long i = 0; i < b.LongLength; i++) b[i] = PatternStream.ByteAt(PatternSeed, i);
            Bytes = b;
            PatternLength = 0;
        }
        return Bytes;
    }
}

internal sealed class FakeVolume(CardIdentity identity, CardSpace space)
{
    public CardIdentity Identity { get; set; } = identity;
    public CardSpace Space { get; set; } = space;
}

/// <summary>In-memory file system that asks Core's IoGuardPolicy before every open, create, attribute change, delete or
/// rename, like Platform does (Ref §4.3 "Fake file system"). Thread-safe.</summary>
public sealed class FakeFileSystem : IDirectoryLister
{
    public const uint ArchiveAttribute = IoGuardPolicy.FileAttributeArchive;
    public const uint DirectoryAttribute = IoGuardPolicy.FileAttributeDirectory;
    public const uint CloudOnlyPlaceholder = 0x401620;   // Ref §13 fixture bits

    private readonly Lock _gate = new();
    private readonly Dictionary<string, FakeNode> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FakeVolume> _volumes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FakeGuardCall> _guardLog = [];
    private readonly List<CardDeleteViolation> _cardDeleteViolations = [];
    private readonly List<string> _hydrationViolations = [];
    private uint _nextSeed = 1;

    public FakeFileSystem(GuardContext context) => Context = context;

    public GuardContext Context { get; set; }
    public FakeFaults Faults { get; } = new();
    public long DestinationFreeBytes { get; set; } = 1_000_000_000_000;

    public IReadOnlyList<FakeGuardCall> GuardLog { get { lock (_gate) { return [.. _guardLog]; } } }
    public IReadOnlyList<CardDeleteViolation> CardDeleteViolations { get { lock (_gate) { return [.. _cardDeleteViolations]; } } }
    public IReadOnlyList<string> HydrationViolations { get { lock (_gate) { return [.. _hydrationViolations]; } } }

    public void AssertNoViolations()
    {
        lock (_gate)
        {
            if (_hydrationViolations.Count > 0 || _cardDeleteViolations.Count > 0)
                throw new InvalidOperationException(
                    $"Tripwire: {_hydrationViolations.Count} hydration violation(s) [{string.Join("; ", _hydrationViolations)}], "
                    + $"{_cardDeleteViolations.Count} card-delete violation(s) [{string.Join("; ", _cardDeleteViolations.Select(v => v.Path))}]");
        }
    }

    // ── setup (unguarded; tests only)
    public void AddDirectory(string path, uint attributes = DirectoryAttribute)
    {
        lock (_gate) { EnsureDirectoryChain(PathRules.Normalize(path), attributes | DirectoryAttribute); }
    }

    public void AddFile(string path, byte[] content, DateTime mtimeUtc, uint attributes = ArchiveAttribute)
    {
        ArgumentNullException.ThrowIfNull(content);
        lock (_gate) { PutFile(PathRules.Normalize(path), attributes, mtimeUtc).Bytes = [.. content]; }
    }

    public void AddFile(string path, long size, DateTime mtimeUtc, uint attributes = ArchiveAttribute)
    {
        lock (_gate)
        {
            var n = PutFile(PathRules.Normalize(path), attributes, mtimeUtc);
            n.PatternLength = size;
            n.PatternSeed = _nextSeed++;
        }
    }

    public void SetAttributes(string path, uint attributes)
    {
        lock (_gate) { Require(path).Attributes = attributes; }
    }

    public uint? GetAttributes(string path)
    {
        lock (_gate) { return Find(path)?.Attributes; }
    }

    public bool Exists(string path)
    {
        lock (_gate) { return Find(path) is not null; }
    }

    public FsEntry? Metadata(string path)
    {
        lock (_gate)
        {
            var n = Find(path);
            return n is null ? null : ToEntry(n, PathRules.Parent(n.Path) ?? n.Path);
        }
    }

    public byte[] PeekContent(string path)
    {
        lock (_gate)
        {
            var n = Require(path);
            return n.Bytes is { } b ? [.. b] : [.. n.Materialize()];
        }
    }

    public void RemoveUnguarded(string path)
    {
        lock (_gate) { RemoveTree(PathRules.Normalize(path)); }
    }

    // ── card volumes
    public void AddCardVolume(string root, CardIdentity identity, CardSpace space)
    {
        lock (_gate)
        {
            var r = PathRules.Normalize(root);
            EnsureDirectoryChain(r, DirectoryAttribute);
            _volumes[r] = new FakeVolume(identity, space);
        }
    }

    public void SetCardIdentity(string root, CardIdentity identity)
    {
        lock (_gate) { Volume(root).Identity = identity; }
    }

    public CardIdentity CardIdentityOf(string root)
    {
        lock (_gate) { return Volume(root).Identity; }
    }

    public CardSpace CardSpaceOf(string root)
    {
        lock (_gate) { return Volume(root).Space; }
    }

    // ── the tripwire
    public string Guard(IoOp op, string path, GuardContext? context = null)
    {
        var ctx = context ?? Context;
        lock (_gate)
        {
            var p = PathRules.Normalize(path);
            var node = Find(p);
            var decision = IoGuardPolicy.Check(op, p, node?.Attributes, ctx);
            var kind = decision switch
            {
                GuardAllow => "Allow",
                GuardUnsafe => "Unsafe",
                GuardCloudOnly => "CloudOnly",
                GuardHydration => "Hydration",
            };
            _guardLog.Add(new FakeGuardCall(op, p, kind));
            if (decision is GuardHydration h)
            {
                _hydrationViolations.Add(p);
                throw new HydrationViolation(h.Path, h.Attributes);
            }
            if (decision is GuardCloudOnly)
            {
                _hydrationViolations.Add(p);
                throw new HydrationViolation(p, node?.Attributes ?? 0);
            }
            if (decision is GuardUnsafe u)
            {
                if (op == IoOp.CardDelete || (op == IoOp.Delete && ctx.CardRoot is { } card && PathRules.IsSameOrUnder(p, card)))
                {
                    _cardDeleteViolations.Add(new CardDeleteViolation(p, op, u.Reason));
                    throw new UnsafeIoException(u.Reason);
                }
                if (node is { IsDirectory: false } && IsDataOpen(op) && IsUnderLibrary(p, ctx))
                {
                    _hydrationViolations.Add(p);
                    throw new HydrationViolation(p, node.Attributes);
                }
                throw new UnsafeIoException(u.Reason);
            }
            return p;
        }
    }

    public Stream OpenRead(string path, GuardContext? context = null)
    {
        var p = Guard(IoOp.ReadData, path, context);
        lock (_gate)
        {
            var n = Find(p) ?? throw new FileNotFoundException("Not found", p);
            if (n.IsDirectory) throw new UnauthorizedAccessException(p);
            return n.OpenContent();
        }
    }

    public Stream OpenAppend(string path, GuardContext? context = null)
    {
        var p = Guard(IoOp.AppendOwnLedger, path, context);
        lock (_gate)
        {
            var n = Find(p);
            if (n is null)
            {
                RequireParentDirectory(p);
                n = PutFile(p, ArchiveAttribute, DateTime.UnixEpoch);
                n.Bytes = [];
            }
            n.Materialize();
            return new FakeAppendStream(this, n, Faults.AppendFails.Contains(p));
        }
    }

    public void CreateDirectory(string path, GuardContext? context = null)
    {
        var p = Guard(IoOp.CreateDir, path, context);
        lock (_gate)
        {
            if (Find(p) is { IsDirectory: true }) return;
            RequireParentDirectory(p);
            _nodes[p] = new FakeNode(p, true, DirectoryAttribute, DateTime.UnixEpoch);
        }
    }

    public void SetPinned(string path, GuardContext? context = null)
    {
        var p = Guard(IoOp.SetPinned, path, context);
        lock (_gate)
        {
            var n = Require(p);
            n.Attributes = (n.Attributes | IoGuardPolicy.FileAttributePinned) & ~IoGuardPolicy.FileAttributeUnpinned;
        }
    }

    // ── IDirectoryLister: listing only, never checks the guard, never opens
    public ListingResult Enumerate(string root, bool recurse, IReadOnlySet<string> excludeDirNames)
    {
        ArgumentNullException.ThrowIfNull(excludeDirNames);
        lock (_gate)
        {
            var r = PathRules.Normalize(root);
            var entries = ImmutableArray.CreateBuilder<FsEntry>();
            var errors = ImmutableArray.CreateBuilder<(string Path, int Win32Error)>();
            if (Find(r) is not { IsDirectory: true })
            {
                errors.Add((r, 3));   // ERROR_PATH_NOT_FOUND
                return new ListingResult(entries.ToImmutable(), errors.ToImmutable());
            }
            if (Faults.EnumerationErrors.TryGetValue(r, out var rootError))
            {
                errors.Add((r, rootError));
                return new ListingResult(entries.ToImmutable(), errors.ToImmutable());
            }
            var queue = new Queue<string>();
            queue.Enqueue(r);
            while (queue.Count > 0)
            {
                var dir = queue.Dequeue();
                foreach (var child in ChildrenOf(dir))
                {
                    var name = PathRules.FileName(child.Path);
                    if (child.IsDirectory && excludeDirNames.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase))) continue;
                    entries.Add(ToEntry(child, r));
                    if (!child.IsDirectory || !recurse) continue;
                    if (Faults.EnumerationErrors.TryGetValue(child.Path, out var error)) errors.Add((child.Path, error));
                    else queue.Enqueue(child.Path);
                }
            }
            return new ListingResult(entries.ToImmutable(), errors.ToImmutable());
        }
    }

    // ── internals shared with the other fakes of this assembly
    internal Lock Gate => _gate;

    internal FakeNode? Find(string path) => _nodes.GetValueOrDefault(PathRules.Normalize(path));

    internal FakeNode Require(string path) => Find(path) ?? throw new FileNotFoundException("Not found", PathRules.Normalize(path));

    internal FakeVolume Volume(string root)
        => _volumes.GetValueOrDefault(PathRules.Normalize(root)) ?? throw new InvalidOperationException($"No card volume at {root}");

    internal FakeNode PutFile(string normalizedPath, uint attributes, DateTime mtimeUtc)
    {
        if (PathRules.Parent(normalizedPath) is { } parent) EnsureDirectoryChain(parent, DirectoryAttribute);
        var n = new FakeNode(normalizedPath, false, attributes & ~DirectoryAttribute, mtimeUtc);
        _nodes[normalizedPath] = n;
        return n;
    }

    internal void RequireParentDirectory(string normalizedPath)
    {
        if (PathRules.Parent(normalizedPath) is not { } parent || Find(parent) is not { IsDirectory: true })
            throw new DirectoryNotFoundException($"Parent of {normalizedPath} doesn't exist");
    }

    internal void AppendBytes(FakeNode node, ReadOnlySpan<byte> bytes)
    {
        lock (_gate)
        {
            var old = node.Materialize();
            var grown = new byte[old.Length + bytes.Length];
            old.CopyTo(grown, 0);
            bytes.CopyTo(grown.AsSpan(old.Length));
            node.Bytes = grown;
        }
    }

    internal void Move(FakeNode node, string newNormalizedPath)
    {
        _nodes.Remove(node.Path);
        node.Path = newNormalizedPath;
        _nodes[newNormalizedPath] = node;
    }

    internal void RemoveTree(string normalizedPath)
    {
        foreach (var key in _nodes.Keys.Where(k => PathRules.IsSameOrUnder(k, normalizedPath)).ToList()) _nodes.Remove(key);
    }

    internal IReadOnlyList<FakeNode> ChildrenOf(string dir)
        => [.. _nodes.Values.Where(n => PathRules.Parent(n.Path) is { } p && PathRules.Equal(p, dir))
                            .OrderBy(n => n.Path, StringComparer.Ordinal)];

    internal static FsEntry ToEntry(FakeNode n, string root)
        => new(n.Path, PathRules.IsStrictlyUnder(n.Path, root) ? PathRules.RelativeCardPath(n.Path, root).Replace('/', '\\') : PathRules.FileName(n.Path),
               n.IsDirectory, n.Size, n.MtimeUtc, n.CreationUtc, n.LastAccessUtc, n.Attributes);

    private void EnsureDirectoryChain(string normalizedDir, uint attributes)
    {
        if (Find(normalizedDir) is { IsDirectory: true }) return;
        if (PathRules.Parent(normalizedDir) is { } parent) EnsureDirectoryChain(parent, DirectoryAttribute);
        _nodes[normalizedDir] = new FakeNode(normalizedDir, true, attributes, DateTime.UnixEpoch);
    }

    private static bool IsDataOpen(IoOp op)
        => op is IoOp.ReadData or IoOp.AppendOwnLedger or IoOp.OpenForFlush or IoOp.SetAttributesOrTimes;

    private static bool IsUnderLibrary(string p, GuardContext ctx)
        => PathRules.IsSameOrUnder(p, ctx.VideoRoot) || PathRules.IsSameOrUnder(p, ctx.PhotoRoot)
           || ctx.PreviousPhotoRoots.Any(r => PathRules.IsSameOrUnder(p, r));
}
