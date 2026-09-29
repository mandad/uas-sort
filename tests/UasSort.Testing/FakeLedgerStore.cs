// tests/UasSort.Testing/FakeLedgerStore.cs
using System.Text;
using UasSort.Core;

namespace UasSort.Testing;

/// <summary>The one fake ILedgerStore (Part 06 creates it, Part 07 completes it). With fs == null it is purely in memory (the
/// snapshot, or an empty one); with a FakeFileSystem every folder create, pin, read and append goes through the guard.</summary>
public sealed class FakeLedgerStore(FakeFileSystem? fs, string videoRoot, string machine, LedgerSnapshot? snapshot = null) : ILedgerStore
{
    // ── Part 06 (Task 06.21): status and load — verbatim from Part 06 (Load records only "Load", never a second "Check")
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

    // ── Part 07 (Task 07.4): the write half
    public FakeLedgerWriter Writer { get; } = new();
    public bool EnsureFolderThrows { get; set; }
    /// <summary>Thrown by EnsureFolder instead (e.g. an UnsafeIoException: the setup's internal safety stop).</summary>
    public Exception? EnsureFolderFault { get; set; }

    public void EnsureFolder()
    {
        Calls.Add("EnsureFolder");
        if (EnsureFolderFault is { } fault) throw fault;
        if (EnsureFolderThrows) throw new IOException($"Couldn't create {Folder}.");
        if (fs is not { } files) return;
        if (files.Metadata(Folder) is null) files.CreateDirectory(Folder);
        files.SetPinned(Folder);
    }

    public ILedgerWriter OpenOwn()
    {
        Calls.Add("OpenOwn");
        Writer.Attach(fs?.OpenAppend(LedgerPaths.OwnFile(videoRoot, machine)));
        return Writer;
    }

    public void SnapshotToBackup(string runId)
    {
        Calls.Add("SnapshotToBackup " + runId);
        if (fs is not { } files || files.Metadata(Folder) is not { IsDirectory: true }) return;
        var dir = PathRules.Join(LedgerPaths.BackupDir(files.Context.AppDataDir, videoRoot), $"snapshots/{runId}");
        foreach (var e in files.Enumerate(Folder, recurse: false, ImmutableHashSet<string>.Empty).Entries)
        {
            if (e.IsDirectory || !LedgerPaths.IsLedgerFileName(PathRules.FileName(e.FullPath))) continue;
            byte[] content;
            using (var s = files.OpenRead(e.FullPath))
            using (var copy = new MemoryStream())
            {
                s.CopyTo(copy);
                content = copy.ToArray();
            }
            var dest = PathRules.Join(dir, PathRules.FileName(e.FullPath));
            files.Guard(IoOp.CreateNew, dest);
            files.AddFile(dest, content, DateTime.UnixEpoch);
        }
    }

    public void KeepOnDevice()
    {
        Calls.Add("KeepOnDevice");
        fs?.SetPinned(Folder);
    }

    /// <summary>Like Part 09's LedgerStore.CopyInto: ensures the new root's ledger folder and appends every line of
    /// current.SourceFiles whose id is not yet in the new own file; torn and unparseable lines are never copied.</summary>
    public void CopyInto(string newVideoRoot, LedgerSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(current);
        Calls.Add("CopyInto " + newVideoRoot);
        if (fs is not { } files) return;
        var target = files.Context with { VideoRoot = newVideoRoot };
        var targetFolder = LedgerPaths.For(newVideoRoot);
        if (files.Metadata(targetFolder) is null) files.CreateDirectory(targetFolder, target);
        files.SetPinned(targetFolder, target);
        var own = LedgerPaths.OwnFile(newVideoRoot, machine);
        var present = new HashSet<string>(StringComparer.Ordinal);
        if (files.Exists(own))
            foreach (var line in ReadLines(files.OpenRead(own, target)))
                if (LedgerCodec.TryParse(line, out _) is { } r) present.Add(r.Id);
        using var append = files.OpenAppend(own, target);
        foreach (var source in current.SourceFiles)
            foreach (var line in ReadLines(files.OpenRead(source)))
                if (LedgerCodec.TryParse(line, out _) is { } r && r is not TornRecord && present.Add(r.Id))
                    append.Write(Encoding.UTF8.GetBytes(line + "\n"));
    }

    private static List<string> ReadLines(Stream stream)
    {
        using (stream)
        using (var reader = new StreamReader(stream, Encoding.UTF8))
            return [.. reader.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0)];
    }
}

/// <summary>ILedgerWriter that keeps every record as an object and, when opened on the fake FS, appends its LedgerCodec line
/// to the own file.</summary>
public sealed class FakeLedgerWriter : ILedgerWriter
{
    private Stream? _stream;

    public List<LedgerRecord> Records { get; } = [];
    public Func<LedgerRecord, bool>? ThrowWhen { get; set; }
    public int Opened { get; private set; }
    public bool Disposed { get; private set; }

    internal void Attach(Stream? stream)
    {
        _stream = stream;
        Opened++;
        Disposed = false;
    }

    public void Append(LedgerRecord r)
    {
        ArgumentNullException.ThrowIfNull(r);
        if (ThrowWhen?.Invoke(r) == true) throw new IOException("The ledger file couldn't be written.");
        if (_stream is not null)
        {
            _stream.Write(Encoding.UTF8.GetBytes(LedgerCodec.Serialize(r) + "\n"));
            _stream.Flush();
        }
        Records.Add(r);
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;
        Disposed = true;
        GC.SuppressFinalize(this);
    }
}
