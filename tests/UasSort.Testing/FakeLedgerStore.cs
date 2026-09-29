namespace UasSort.Testing;

/// <summary>The one fake <see cref="ILedgerStore"/> (registry decision 6). Reads go through Part 05's builder and loader over the
/// fake file system (every open is guarded and logged), or return an in-memory snapshot when no file system is given.
/// Part 07 Task 07.4 implements the write members in this file.</summary>
public sealed class FakeLedgerStore(FakeFileSystem? fs, string videoRoot, string machine, LedgerSnapshot? snapshot = null) : ILedgerStore
{
    private static readonly IReadOnlySet<string> NoExcludes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public string Folder { get; } = LedgerPaths.For(videoRoot);
    public List<string> Calls { get; } = [];
    public LedgerFolderStatus? StatusOverride { get; set; }
    public bool CheckThrows { get; set; }

    public LedgerFolderStatus Check()
    {
        Calls.Add(nameof(Check));
        return Status();
    }

    public LedgerSnapshot Load()
    {
        Calls.Add(nameof(Load));
        var status = Status();
        if (fs is not { } files) return snapshot ?? LedgerSnapshots.Empty(status);
        return LedgerLoader.Load(status, p => files.OpenRead(p));      // guarded ReadData; logged in files.GuardLog
    }

    public void EnsureFolder() => throw NotYet(nameof(EnsureFolder));
    public ILedgerWriter OpenOwn() => throw NotYet(nameof(OpenOwn));
    public void SnapshotToBackup(string runId) => throw NotYet(nameof(SnapshotToBackup));
    public void KeepOnDevice() => throw NotYet(nameof(KeepOnDevice));
    public void CopyInto(string newVideoRoot, LedgerSnapshot current) => throw NotYet(nameof(CopyInto));

    private LedgerFolderStatus Status()
    {
        if (CheckThrows) throw new IOException("Fake ledger folder check failed.");
        if (StatusOverride is { } overridden) return overridden;
        if (fs is null)
            return snapshot?.Status ?? LedgerFolderStatusBuilder.Build(videoRoot, machine, new LedgerFolderFacts(true, null, null, false, true));
        var root = fs.Metadata(videoRoot);
        var folder = fs.Metadata(Folder);
        var top = folder is { IsDirectory: true } ? fs.Enumerate(Folder, false, NoExcludes) : null;
        return LedgerFolderStatusBuilder.Build(videoRoot, machine,
            new LedgerFolderFacts(root is { IsDirectory: true }, folder, top, InSyncRoot: false, Writable: true));
    }

    private NotSupportedException NotYet(string member)
    {
        Calls.Add(member);
        return new NotSupportedException($"FakeLedgerStore.{member} is completed by Part 07 Task 07.4.");
    }
}
