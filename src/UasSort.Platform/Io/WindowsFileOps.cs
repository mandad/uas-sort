using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO.Hashing;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace UasSort.Platform.Io;

/// <summary>Raw destination writes (Ref §10.3). Callers clear every path with IoGate first; nothing here decides safety.</summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
                 Justification = "Injected instance (tests force the buffered fallback); GuardedFileOps calls every operation through it")]
internal sealed class WindowsFileOps(bool allowUnbuffered = true)
{
    private const int Chunk = 1 << 20;
    private const FileOptions NoBuffering = (FileOptions)0x20000000;

#pragma warning disable RS0030 // IO layer: GuardedFileOps is the only caller; each path was cleared by IoGuardPolicy
    public FileStream CreateNew(string path, long size) => new(path, new FileStreamOptions
    {
        Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, PreallocationSize = size, BufferSize = 0,
    });

    public void MarkTemp(FileStream s)
        => File.SetAttributes(s.SafeFileHandle, FileAttributes.Hidden | FileAttributes.NotContentIndexed);

    public (UInt128 Hash, VerifyMode Mode) Hash(string path, CancellationToken ct)
    {
        if (allowUnbuffered)
        {
            try { return (HashUnbuffered(path, ct), VerifyMode.Unbuffered); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        return (HashBuffered(path, ct), VerifyMode.Cached);
    }

    private static unsafe UInt128 HashUnbuffered(string path, CancellationToken ct)
    {
        SafeFileHandle handle;
        try { handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, NoBuffering); }
        catch (ArgumentOutOfRangeException)
        {
            handle = Kernel32.CreateFile(LongPath.Prefix(path), Kernel32.GENERIC_READ, Kernel32.FILE_SHARE_READ, 0,
                                         Kernel32.OPEN_EXISTING, Kernel32.FILE_FLAG_NO_BUFFERING, 0);
            if (handle.IsInvalid) throw new IOException($"CreateFileW(NO_BUFFERING) failed: {Marshal.GetLastPInvokeError()}");
        }
        using (handle)
        {
            var buffer = NativeMemory.AlignedAlloc(Chunk, 4096);
            try
            {
                var hasher = new XxHash128();
                var span = new Span<byte>(buffer, Chunk);
                for (long offset = 0; ; offset += Chunk)
                {
                    ct.ThrowIfCancellationRequested();
                    var n = RandomAccess.Read(handle, span, offset);
                    hasher.Append(span[..n]);
                    if (n < Chunk) break;           // the next unbuffered read would start at an unaligned offset
                }
                return hasher.GetCurrentHashAsUInt128();
            }
            finally { NativeMemory.AlignedFree(buffer); }
        }
    }

    private static UInt128 HashBuffered(string path, CancellationToken ct)
    {
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.SequentialScan);
        var buffer = ArrayPool<byte>.Shared.Rent(Chunk);
        try
        {
            var hasher = new XxHash128();
            long offset = 0;
            int n;
            while ((n = RandomAccess.Read(handle, buffer.AsSpan(0, Chunk), offset)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                hasher.Append(buffer.AsSpan(0, n));
                offset += n;
            }
            return hasher.GetCurrentHashAsUInt128();
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    public void SetTimesAndClearHidden(string path, uint attributes, DateTime creationUtc, DateTime mtimeUtc)
    {
        File.SetCreationTimeUtc(path, creationUtc);
        File.SetLastWriteTimeUtc(path, mtimeUtc);
        var cleared = attributes & ~Kernel32.FILE_ATTRIBUTE_HIDDEN;
        if (!Kernel32.SetFileAttributes(LongPath.Prefix(path), cleared == 0 ? 0x80u /* NORMAL */ : cleared))
            throw new IOException($"SetFileAttributesW({path}) failed: {Marshal.GetLastPInvokeError()}");
    }

    public int Move(string from, string to)
        => Kernel32.MoveFileEx(LongPath.Prefix(from), LongPath.Prefix(to), Kernel32.MOVEFILE_WRITE_THROUGH) ? 0 : Marshal.GetLastPInvokeError();

    public void FlushFile(string path)
    {
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
        if (!Kernel32.FlushFileBuffers(handle)) throw new IOException($"FlushFileBuffers({path}) failed: {Marshal.GetLastPInvokeError()}");
    }

    public void FlushDirectory(string dir)
    {
        using var handle = Kernel32.CreateFile(LongPath.Prefix(dir), Kernel32.GENERIC_WRITE,
            Kernel32.FILE_SHARE_READ | Kernel32.FILE_SHARE_WRITE | Kernel32.FILE_SHARE_DELETE, 0, Kernel32.OPEN_EXISTING,
            Kernel32.FILE_FLAG_BACKUP_SEMANTICS, 0);
        if (handle.IsInvalid) throw new IOException($"Opening {dir} for flush failed: {Marshal.GetLastPInvokeError()}");
        if (!Kernel32.FlushFileBuffers(handle)) throw new IOException($"FlushFileBuffers({dir}) failed: {Marshal.GetLastPInvokeError()}");
    }

    public void Delete(string path) => File.Delete(path);

    public void CreateDirectory(string dir) => Directory.CreateDirectory(dir);

    public bool ExactNameExists(string path)
    {
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileName(path);
        return Directory.EnumerateFiles(dir, name, new EnumerationOptions { AttributesToSkip = 0, MatchCasing = MatchCasing.CaseInsensitive })
                        .Any(p => string.Equals(Path.GetFileName(p), name, StringComparison.Ordinal));
    }
#pragma warning restore RS0030
}
