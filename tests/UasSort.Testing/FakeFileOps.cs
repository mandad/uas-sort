using System.IO.Hashing;

namespace UasSort.Testing;

public sealed class FakeFileOps(FakeFileSystem fs) : IFileOps
{
    private const uint TempAttributes = IoGuardPolicy.FileAttributeHidden | IoGuardPolicy.FileAttributeNotContentIndexed
                                        | IoGuardPolicy.FileAttributeArchive;
    private readonly HashSet<string> _ownTemps = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _renamed = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _flushed = [];
    private readonly List<string> _createdDirectories = [];
    private long _bytesWritten;

    public IReadOnlySet<string> OwnTemps { get { lock (fs.Gate) { return _ownTemps.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase); } } }
    public IReadOnlySet<string> Renamed { get { lock (fs.Gate) { return _renamed.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase); } } }
    public IReadOnlyList<string> Flushed { get { lock (fs.Gate) { return [.. _flushed]; } } }
    public IReadOnlyList<string> CreatedDirectories { get { lock (fs.Gate) { return [.. _createdDirectories]; } } }
    public int FlushToDiskCount { get; private set; }

    private GuardContext Ctx => fs.Context with { OwnTempsThisRun = OwnTemps, RenamedThisRun = Renamed };

    public Stream CreateTemp(string finalPath, long size, out string tempPath)
    {
        var final = PathRules.Normalize(finalPath);
        ThrowIfLost(final);
        var temp = final + IoGuardPolicy.TempSuffix;
        tempPath = temp;
        fs.Guard(IoOp.CreateNew, temp, Ctx);
        lock (fs.Gate)
        {
            if (fs.Find(temp) is not null) throw new IOException($"The file '{temp}' already exists.", unchecked((int)0x80070050));
            fs.RequireParentDirectory(temp);
            if (size > fs.DestinationFreeBytes) throw DiskFull();
            var node = fs.PutFile(temp, TempAttributes, DateTime.UnixEpoch);
            node.Bytes = [];
            _ownTemps.Add(temp);
            return new FakeWriteStream(this, node);
        }
    }

    public void FlushToDisk(Stream s)
    {
        ArgumentNullException.ThrowIfNull(s);
        s.Flush();
        FlushToDiskCount++;
    }

    public VerifyResult VerifyHash(string tempPath, long size, UInt128 expected, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var temp = PathRules.Normalize(tempPath);
        ThrowIfLost(temp);
        fs.Guard(IoOp.ReadData, temp, Ctx);
        byte[] data;
        VerifyMode mode;
        lock (fs.Gate)
        {
            data = [.. fs.Require(temp).Materialize()];
            var final = temp[..^IoGuardPolicy.TempSuffix.Length];
            mode = fs.Faults.UnbufferedUnsupported.Contains(final) ? VerifyMode.Cached : VerifyMode.Unbuffered;
            if (fs.Faults.CorruptVerify.TryGetValue(final, out var left) && left > 0 && data.Length > 0)
            {
                fs.Faults.CorruptVerify[final] = left - 1;
                data[0] ^= 1;
            }
        }
        var got = XxHash128.HashToUInt128(data);
        return got == expected && data.LongLength == size ? new HashMatch(mode) : new HashMismatch(got, mode);
    }

    public void FinalizeAttributes(string tempPath, DateTime creationUtc, DateTime mtimeUtc)
    {
        var temp = PathRules.Normalize(tempPath);
        ThrowIfLost(temp);
        fs.Guard(IoOp.SetAttributesOrTimes, temp, Ctx);
        lock (fs.Gate)
        {
            var n = fs.Require(temp);
            n.CreationUtc = creationUtc;
            n.MtimeUtc = mtimeUtc;
            n.Attributes &= ~IoGuardPolicy.FileAttributeHidden;
        }
    }

    public RenameResult RenameNoReplace(string tempPath, string finalPath)
    {
        var temp = PathRules.Normalize(tempPath);
        var final = PathRules.Normalize(finalPath);
        ThrowIfLost(final);
        fs.Guard(IoOp.Rename, temp, Ctx);
        lock (fs.Gate)
        {
            if (!PathRules.Equal(PathRules.Parent(temp) ?? "", PathRules.Parent(final) ?? ""))
                throw new UnsafeIoException($"Rename {temp} → {final} leaves its directory");
            if (fs.Faults.TargetAppearsBeforeRename.Contains(final) && fs.Find(final) is null)
                fs.AddFile(final, [0x42], DateTime.UnixEpoch);
            if (fs.Find(final) is not null) return new TargetExists();
            fs.Move(fs.Require(temp), final);
            _ownTemps.Remove(temp);
            _renamed.Add(final);
            return new Renamed();
        }
    }

    public bool ConfirmFinal(string finalPath, long size)
    {
        var final = PathRules.Normalize(finalPath);
        ThrowIfLost(final);
        lock (fs.Gate)
        {
            if (fs.Metadata(final) is not { IsDirectory: false } e) return false;
            var seen = fs.Faults.SizeAfterRename.TryGetValue(final, out var faked) ? faked : e.Size;
            return seen == size;
        }
    }

    public void FlushDestination(string dir, IReadOnlyList<string> filesCreatedThisRun)
    {
        ArgumentNullException.ThrowIfNull(filesCreatedThisRun);
        var d = PathRules.Normalize(dir);
        ThrowIfLost(d);
        foreach (var f in filesCreatedThisRun)
        {
            var p = fs.Guard(IoOp.OpenForFlush, f, Ctx);
            lock (fs.Gate) { _flushed.Add(p); }
        }
        var dp = fs.Guard(IoOp.OpenForFlush, d, Ctx);
        lock (fs.Gate) { _flushed.Add(dp); }
    }

    public void DeleteOwnTemp(string tempPath)
    {
        var temp = PathRules.Normalize(tempPath);
        if (!temp.EndsWith(IoGuardPolicy.TempSuffix, StringComparison.OrdinalIgnoreCase))
            throw new UnsafeIoException($"DeleteOwnTemp refuses {temp}: not a *{IoGuardPolicy.TempSuffix} file");
        ThrowIfLost(temp);
        if (!fs.Exists(temp)) return;
        fs.Guard(IoOp.Delete, temp, Ctx);
        lock (fs.Gate)
        {
            fs.RemoveTree(temp);
            _ownTemps.Remove(temp);
        }
    }

    public void EnsureDirectory(string dir, bool allowCreate)
    {
        var d = PathRules.Normalize(dir);
        ThrowIfLost(d);
        if (fs.Metadata(d) is { IsDirectory: true }) return;
        if (!allowCreate) throw new DirectoryNotFoundException($"{d} doesn't exist");
        var missing = new Stack<string>();
        for (var cur = d; cur is not null && !fs.Exists(cur); cur = PathRules.Parent(cur)) missing.Push(cur);
        while (missing.Count > 0)
        {
            var m = missing.Pop();
            fs.Guard(IoOp.CreateDir, m, Ctx);
            lock (fs.Gate)
            {
                fs.AddDirectory(m);
                _createdDirectories.Add(m);
            }
        }
    }

    public bool TryGetSize(string path, out long size)
    {
        var e = fs.Metadata(path);
        size = e is { IsDirectory: false } ? e.Size : 0;
        return e is { IsDirectory: false };
    }

    public long FreeBytes(string anyPathOnVolume)
    {
        ThrowIfLost(PathRules.Normalize(anyPathOnVolume));
        return fs.DestinationFreeBytes;
    }

    internal void WriteTemp(FakeNode node, ReadOnlySpan<byte> bytes)
    {
        ThrowIfLost(node.Path);
        var allowed = bytes.Length;
        if (fs.Faults.DiskFullAfterBytes is long limit)
            allowed = (int)Math.Max(0, Math.Min(bytes.Length, limit - Interlocked.Read(ref _bytesWritten)));
        fs.AppendBytes(node, bytes[..allowed]);
        Interlocked.Add(ref _bytesWritten, allowed);
        if (allowed < bytes.Length) throw DiskFull();
    }

    private void ThrowIfLost(string path)
    {
        if (fs.Faults.IsLost(path)) throw new DirectoryNotFoundException($"The destination root of {path} is gone");
    }

    private static IOException DiskFull() => new("There is not enough space on the disk.", unchecked((int)0x80070070));
}

internal sealed class FakeWriteStream(FakeFileOps ops, FakeNode node) : Stream
{
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => node.Size;
    public override long Position { get => node.Size; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override void Write(byte[] buffer, int offset, int count) => ops.WriteTemp(node, buffer.AsSpan(offset, count));
    public override void Write(ReadOnlySpan<byte> buffer) => ops.WriteTemp(node, buffer);
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
