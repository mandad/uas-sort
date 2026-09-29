using System.Runtime.InteropServices;

namespace UasSort.Platform.Io;

/// <summary>IFileOps for one Commit: every create, read-back, attribute change, rename, flush and delete asks IoGuardPolicy first.</summary>
public sealed class GuardedFileOps : IFileOps
{
    private const string TempSuffix = IoGuardPolicy.TempSuffix;
    private readonly IPathFacts _facts;
    private readonly WindowsFileOps _inner;
    private readonly HashSet<string> _ownTemps = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _renamed = new(StringComparer.OrdinalIgnoreCase);
    private readonly GuardContext _ctx;

    /// <param name="newFolderDirs">The run's GuardContext.NewFolderDirs (OffloadCompiler.NewFolderDirs), used verbatim; empty for Preflight.</param>
    public GuardedFileOps(Settings settings, string appDataDir, string machine, IPathFacts facts, IReadOnlySet<string> newFolderDirs)
        : this(settings, appDataDir, machine, facts, newFolderDirs, new WindowsFileOps()) { }

    internal GuardedFileOps(Settings settings, string appDataDir, string machine, IPathFacts facts,
                            IReadOnlySet<string> newFolderDirs, WindowsFileOps inner)
    {
        _facts = facts;
        _inner = inner;
        // Decision 27: the set is taken as given. GuardContexts.For canonicalises each entry into an OrdinalIgnoreCase set;
        // no ancestor is added here (the caller's set already lists every YYYY and YYYY-MM folder the run creates).
        _ctx = GuardContexts.For(settings, appDataDir, machine, facts, cardRoot: null, newFolderDirs, _ownTemps, _renamed);
    }

    public Stream CreateTemp(string finalPath, long size, out string tempPath)
    {
        tempPath = Canon(finalPath) + TempSuffix;
        IoGate.Require(IoOp.CreateNew, tempPath, _ctx);
        var stream = _inner.CreateNew(tempPath, size);
        _ownTemps.Add(tempPath);
        try
        {
            IoGate.Require(IoOp.SetAttributesOrTimes, tempPath, _ctx);
            _inner.MarkTemp(stream);
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }

    public void FlushToDisk(Stream s)
    {
        if (s is not FileStream fs) throw new ArgumentException("not a stream from CreateTemp", nameof(s));
        fs.Flush();
        if (!Kernel32.FlushFileBuffers(fs.SafeFileHandle))
            throw new IOException($"FlushFileBuffers failed: {Marshal.GetLastPInvokeError()}");
    }

    public VerifyResult VerifyHash(string tempPath, long size, UInt128 expected, CancellationToken ct)
    {
        var path = Canon(tempPath);
        IoGate.Require(IoOp.ReadData, path, _ctx);
        var (got, mode) = _inner.Hash(path, ct);
        return got == expected ? new HashMatch(mode) : new HashMismatch(got, mode);
    }

    public void FinalizeAttributes(string tempPath, DateTime creationUtc, DateTime mtimeUtc)
    {
        var path = Canon(tempPath);
        var attributes = IoGate.Require(IoOp.SetAttributesOrTimes, path, _ctx)
                         ?? throw new FileNotFoundException("temp file missing", path);
        _inner.SetTimesAndClearHidden(path, attributes, creationUtc, mtimeUtc);
    }

    public RenameResult RenameNoReplace(string tempPath, string finalPath)
    {
        var from = Canon(tempPath);
        var to = Canon(finalPath);
        if (!string.Equals(from, to + TempSuffix, StringComparison.OrdinalIgnoreCase))
            throw new UnsafeIoException($"Refused rename {from} → {to}: a temp may only take its own final name in its own folder");
        IoGate.Require(IoOp.Rename, from, _ctx);
        var error = _inner.Move(from, to);
        if (error is Kernel32.ERROR_FILE_EXISTS or Kernel32.ERROR_ALREADY_EXISTS) return new TargetExists();
        if (error != 0) throw new IOException($"MoveFileExW({from}) failed: {error}", error);
        _ownTemps.Remove(from);
        _renamed.Add(to);
        return new Renamed();
    }

    public bool ConfirmFinal(string finalPath, long size)
    {
        var path = Path.GetFullPath(finalPath);
        return Kernel32.TryGetAttributeData(path, out var d, out _)
               && (d.FileAttributes & Kernel32.FILE_ATTRIBUTE_DIRECTORY) == 0
               && d.Size == size
               && _inner.ExactNameExists(path);
    }

    public void FlushDestination(string dir, IReadOnlyList<string> filesCreatedThisRun)
    {
        foreach (var file in filesCreatedThisRun)
        {
            var path = Canon(file);
            IoGate.Require(IoOp.OpenForFlush, path, _ctx);
            _inner.FlushFile(path);
        }
        var d = Canon(dir);
        IoGate.Require(IoOp.OpenForFlush, d, _ctx);
        _inner.FlushDirectory(d);
    }

    public void DeleteOwnTemp(string tempPath)
    {
        var path = Canon(tempPath);
        if (!path.EndsWith(TempSuffix, StringComparison.OrdinalIgnoreCase))
            throw new UnsafeIoException($"Refused delete of {path}: only *{TempSuffix} files are ever deleted");
        IoGate.Require(IoOp.Delete, path, _ctx);
        _inner.Delete(path);
        _ownTemps.Remove(path);
    }

    public void EnsureDirectory(string dir, bool allowCreate)
    {
        var target = Canon(dir);
        var missing = new Stack<string>();
        for (var d = target; ; d = Path.GetDirectoryName(d)!)
        {
            var attributes = Kernel32.TryGetAttributes(d, out var error);
            if (attributes is uint a)
            {
                if ((a & Kernel32.FILE_ATTRIBUTE_DIRECTORY) == 0) throw new IOException($"{d} exists and is not a folder");
                break;
            }
            if (!Kernel32.IsNotFound(error)) throw new IOException($"{d} can't be read (Win32 error {error})", error);
            if (!allowCreate) throw new DirectoryNotFoundException($"{target} no longer exists (renamed or moved since the scan)");
            missing.Push(d);
            if (Path.GetDirectoryName(d) is null) throw new DirectoryNotFoundException($"{d}: the volume is missing");
        }
        foreach (var d in missing)               // shallowest first
        {
            IoGate.Require(IoOp.CreateDir, d, _ctx);
            _inner.CreateDirectory(d);
        }
    }

    public bool TryGetSize(string path, out long size)
    {
        size = 0;
        if (!Kernel32.TryGetAttributeData(Path.GetFullPath(path), out var d, out _) || (d.FileAttributes & Kernel32.FILE_ATTRIBUTE_DIRECTORY) != 0)
            return false;
        size = d.Size;
        return true;
    }

    public long FreeBytes(string anyPathOnVolume)
    {
        for (var d = Path.GetFullPath(anyPathOnVolume); d is not null; d = Path.GetDirectoryName(d))
            if (Kernel32.TryGetAttributes(d, out _) is uint a && (a & Kernel32.FILE_ATTRIBUTE_DIRECTORY) != 0)
                return VolumeQuery.Space(d)?.Free ?? throw new IOException($"GetDiskFreeSpaceExW({d}) failed");
        throw new DirectoryNotFoundException($"No existing folder on the volume of {anyPathOnVolume}");
    }

    private string Canon(string path) => _facts.Canonical(path);
}
