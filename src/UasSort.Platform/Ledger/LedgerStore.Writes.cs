using System.Globalization;
using System.Runtime.InteropServices;
using UasSort.Platform.Io;

namespace UasSort.Platform.Ledger;

public sealed partial class LedgerStore : ILedgerStore
{
    private const int SnapshotsKept = 20;
    // SetFileAttributesW accepts only these bits; DIRECTORY, REPARSE_POINT, SPARSE, COMPRESSED, ENCRYPTED are masked out
    private const uint SettableAttributes = 0x1 | 0x2 | 0x4 | 0x20 | 0x100 | 0x1000 | 0x2000 | 0x8000 | 0x20000 | 0x80000 | 0x100000;

    public void EnsureFolder()
    {
        if (!IsDirectory(Kernel32.TryGetAttributes(_videoRoot, out _)))
            throw new DirectoryNotFoundException($"The video root {_videoRoot} is missing");
        if (!IsDirectory(Kernel32.TryGetAttributes(Folder, out _)))
        {
            IoGate.Require(IoOp.CreateDir, Folder, _ctx);
#pragma warning disable RS0030 // IO layer: ledger exemption 3, creates videoRoot\.uas-sort itself (the parent exists)
            Directory.CreateDirectory(Folder);
#pragma warning restore RS0030
        }
        KeepOnDevice();
    }

    public void KeepOnDevice()
    {
        var attributes = IoGate.Require(IoOp.SetPinned, Folder, _ctx) ?? throw new DirectoryNotFoundException(Folder);
        var pinned = ((attributes | Kernel32.FILE_ATTRIBUTE_PINNED) & ~Kernel32.FILE_ATTRIBUTE_UNPINNED) & SettableAttributes;
        if ((attributes & Kernel32.FILE_ATTRIBUTE_PINNED) != 0 && (attributes & Kernel32.FILE_ATTRIBUTE_UNPINNED) == 0) return;
        if (!Kernel32.SetFileAttributes(LongPath.Prefix(Folder), pinned))
            throw new IOException($"Pinning {Folder} failed: {Marshal.GetLastPInvokeError()}");
    }

    public ILedgerWriter OpenOwn() => OpenOwnWriter();

    public void SnapshotToBackup(string runId)
    {
        var sources = LoadedFiles ?? LocalLedgerFiles(Check());
        var stamp = Clock.GetUtcNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var run8 = new string(runId.Where(char.IsAsciiLetterOrDigit).Take(8).ToArray());
        var snapshotsRoot = Path.Join(BackupDir, "snapshots");
        var dir = Path.Join(snapshotsRoot, $"{stamp}-{run8}");
        IoGate.Require(IoOp.CreateDir, dir, _ctx);
#pragma warning disable RS0030 // IO layer: local backup under %LOCALAPPDATA%\uas-sort (guard rule 5)
        Directory.CreateDirectory(dir);
        foreach (var listed in sources)
        {
            var source = _facts.Canonical(listed);
            IoGate.Require(IoOp.ReadData, source, _ctx);
            var destination = Path.Join(dir, Path.GetFileName(source));
            IoGate.Require(IoOp.CreateNew, destination, _ctx);
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
            output.Flush(flushToDisk: true);
        }
        var old = _lister.Enumerate(snapshotsRoot, recurse: false, NoExclusions).Entries
                         .Where(e => e.IsDirectory).Select(e => e.FullPath).Order(StringComparer.Ordinal).ToList();
        foreach (var stale in old.Take(Math.Max(0, old.Count - SnapshotsKept)))
        {
            IoGate.Require(IoOp.Delete, stale, _ctx);
            Directory.Delete(stale, recursive: true);
        }
#pragma warning restore RS0030
    }

    public LedgerSnapshot LoadFromBackup()
    {
        var files = new List<string>();
        var snapshotsRoot = Path.Join(BackupDir, "snapshots");
        var latest = _lister.Enumerate(snapshotsRoot, recurse: false, NoExclusions).Entries
                            .Where(e => e.IsDirectory).Select(e => e.FullPath).Order(StringComparer.Ordinal).LastOrDefault();
        if (latest is not null)
            files.AddRange(_lister.Enumerate(latest, recurse: false, NoExclusions).Entries.Where(e => !e.IsDirectory).Select(e => e.FullPath));
        var mirror = Path.Join(BackupDir, Path.GetFileName(OwnFile));
        if (Kernel32.TryGetAttributes(mirror, out _) is not null) files.Add(mirror);
        var texts = files.Select(f =>
        {
            IoGate.Require(IoOp.ReadData, f, _ctx);
            return new LedgerFileText(f, ReadAllText(f));
        }).ToList();
        return LedgerReader.Read(texts, LedgerSnapshots.Detached(BackupDir, files));
    }

    /// <summary>Video-root [Copy] (Ref §9.14). The app calls it on the NEW root's store (the new root is saved first) with the old
    /// root's snapshot, so each source is cleared with the guard context of the root it belongs to: a <c>&lt;root&gt;\.uas-sort\ledger*.jsonl</c>
    /// under that root's context (the ledger exemption), a backup under app data with this store's. Every source is read before the
    /// target's folder and own file are touched, so a refusal or a read error leaves no empty own file behind (the "No history
    /// found" prompt comes back).</summary>
    public void CopyInto(string newVideoRoot, LedgerSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(current);
        var texts = new List<string>(current.SourceFiles.Length);
        foreach (var listed in current.SourceFiles)
        {
            var source = _facts.Canonical(listed);
            IoGate.Require(IoOp.ReadData, source, ContextOfSource(source));
            texts.Add(ReadAllText(source));
        }

        var target = new LedgerStore(CurrentSettings with { VideoRoot = newVideoRoot }, AppDataDir, Machine, _facts, _lister, Clock);
        target.EnsureFolder();
        var present = new HashSet<string>(StringComparer.Ordinal);
        if (Kernel32.TryGetAttributes(target.OwnFile, out _) is not null)
        {
            IoGate.Require(IoOp.ReadData, target.OwnFile, target._ctx);
            foreach (var line in ReadAllText(target.OwnFile).Split('\n'))
                if (TryRecord(line) is { } r) present.Add(r.Id);
        }
        using var writer = target.OpenOwnWriter();
        foreach (var text in texts)
        {
            foreach (var line in text.Split('\n'))
            {
                if (TryRecord(line) is not { } r || r is TornRecord) continue;   // torn line numbers belong to their own file
                if (present.Add(r.Id)) writer.AppendRawLine(line.TrimEnd('\r'));
            }
        }
    }

    /// <summary>The guard context a [Copy] source is read under: its own root's for a file directly in a <c>.uas-sort</c> folder,
    /// else this store's (a local backup snapshot or mirror under app data).</summary>
    private GuardContext ContextOfSource(string source)
    {
        if (PathRules.Parent(source) is { } folder
            && string.Equals(PathRules.FileName(folder), LedgerPaths.FolderName, StringComparison.OrdinalIgnoreCase)
            && PathRules.Parent(folder) is { } root
            && !PathRules.Equal(root, _videoRoot))
            return GuardContexts.For(CurrentSettings with { VideoRoot = root }, AppDataDir, Machine, _facts);
        return _ctx;
    }

    private static ImmutableArray<string> LocalLedgerFiles(LedgerFolderStatus status)
        => [.. status.LedgerFiles.Where(f => !status.CloudOnlyFiles.Contains(f, StringComparer.OrdinalIgnoreCase))];

    private LedgerWriter OpenOwnWriter()
    {
        IoGate.Require(IoOp.AppendOwnLedger, OwnFile, _ctx);
        var mirrorPath = Path.Join(BackupDir, Path.GetFileName(OwnFile));
        IoGate.Require(IoOp.CreateDir, BackupDir, _ctx);
        IoGate.Require(IoOp.AppendOwnLedger, mirrorPath, _ctx);
#pragma warning disable RS0030 // IO layer: ledger exemption 2 (own file, FileShare.Read single writer) and its local mirror
        var own = new FileStream(OwnFile, new FileStreamOptions
        {
            Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.Read, BufferSize = 0,
        });
        try
        {
            Directory.CreateDirectory(BackupDir);
            var mirror = new FileStream(mirrorPath, new FileStreamOptions
            {
                Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.Read, BufferSize = 0,
            });
#pragma warning restore RS0030
            try { return new LedgerWriter(own, mirror, Machine, Clock); }
            catch { mirror.Dispose(); throw; }
        }
        catch { own.Dispose(); throw; }
    }

    /// <summary>One ledger line through Core's codec; null for a blank, torn-tail or otherwise unparseable line (never copied).</summary>
    private static LedgerRecord? TryRecord(string line)
        => line.TrimEnd('\r') is { Length: > 0 } text ? LedgerCodec.TryParse(text, out _) : null;
}
