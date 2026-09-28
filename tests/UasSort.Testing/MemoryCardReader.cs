namespace UasSort.Testing;

/// <summary>
/// In-memory <see cref="ICardReader"/> for probe, harvester and thumbnail tests: read-only streams over byte arrays,
/// with open counting, live-handle tracking and per-path open failures. Paths compare case-insensitively.
/// </summary>
public sealed class MemoryCardReader(CardIdentity identity) : ICardReader
{
    public static readonly DateTime DefaultMtimeUtc = new(2026, 9, 27, 18, 8, 0, DateTimeKind.Utc);

    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _failing = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<TrackedStream> _streams = [];
    private readonly Lock _lock = new();

    public CardIdentity Identity { get; set; } = identity;

    /// <summary>Every OpenRandom/OpenSequential call, in order.</summary>
    public IReadOnlyList<string> OpenLog { get { lock (_lock) return [.. _streams.Select(s => s.Path)]; } }

    /// <summary>Streams opened and not yet disposed.</summary>
    public int OpenHandles { get { lock (_lock) return _streams.Count(s => !s.IsDisposed); } }

    public MemoryCardReader Add(string relPath, byte[] content)
    {
        lock (_lock) _files[relPath] = content;
        return this;
    }

    /// <summary>Makes every later open of <paramref name="relPath"/> throw <see cref="IOException"/>.</summary>
    public MemoryCardReader FailOpen(string relPath)
    {
        lock (_lock) _failing.Add(relPath);
        return this;
    }

    public CardIdentity CurrentIdentity() => Identity;
    public Stream OpenRandom(string cardRelPath) => Open(cardRelPath);
    public Stream OpenSequential(string cardRelPath) => Open(cardRelPath);

    public FsEntry Stat(string cardRelPath)
    {
        lock (_lock)
        {
            if (!_files.TryGetValue(cardRelPath, out byte[]? content)) throw new FileNotFoundException("Not on the fake card.", cardRelPath);
            return Entry(cardRelPath, content.Length);
        }
    }

    public ListingResult Relist()
    {
        lock (_lock) return new ListingResult([.. _files.Select(kv => Entry(kv.Key, kv.Value.Length))], []);
    }

    public CardSpace Space() => new(FreeBytes: 32_000_000_000, TotalBytes: 128_000_000_000, ClusterBytes: 131_072);

    private static FsEntry Entry(string relPath, long size)
    {
        string native = relPath.Replace('/', '\\');      // FsEntry.RelPath is native (Part 02)
        return new FsEntry(@"E:\" + native, native, false, size, DefaultMtimeUtc, DefaultMtimeUtc, DefaultMtimeUtc, 0x20);
    }

    private TrackedStream Open(string relPath)
    {
        lock (_lock)
        {
            if (_failing.Contains(relPath)) throw new IOException($"Simulated read error: {relPath}");
            if (!_files.TryGetValue(relPath, out byte[]? content)) throw new FileNotFoundException("Not on the fake card.", relPath);
            var stream = new TrackedStream(relPath, content);
            _streams.Add(stream);
            return stream;
        }
    }

    private sealed class TrackedStream(string path, byte[] content) : MemoryStream(content, writable: false)
    {
        public string Path { get; } = path;
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
