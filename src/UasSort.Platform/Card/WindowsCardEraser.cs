using System.ComponentModel;
using System.Runtime.InteropServices;
using UasSort.Platform.Io;

namespace UasSort.Platform.Card;

/// <summary>The only code that deletes anything on a card (Ref §10.6). DeleteFileW and RemoveDirectoryW are declared here only.</summary>
public sealed partial class WindowsCardEraser : ICardEraser
{
    private readonly GuardContext _ctx;
    private readonly string _root;
    private bool _disposed;

    internal WindowsCardEraser(GuardContext ctx)
    {
        _ctx = ctx;
        _root = ctx.CardRoot ?? throw new ArgumentException("no card root", nameof(ctx));
    }

    public EraseResult DeleteFile(string cardRelPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var path = Resolve(cardRelPath);
        IoGate.Require(IoOp.CardDelete, path, _ctx);
#pragma warning disable RS0030 // IO layer: Card cleanup, confirmed plan only
        var ok = DeleteFileW(LongPath.Prefix(path));
#pragma warning restore RS0030
        return ok ? new EraseOk() : LastError();
    }

    public EraseResult RemoveEmptySetFolder(string cardRelDir)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var path = Resolve(cardRelDir);
        IoGate.Require(IoOp.CardDelete, path, _ctx);
#pragma warning disable RS0030 // IO layer: Card cleanup, confirmed plan only
        var ok = RemoveDirectoryW(LongPath.Prefix(path));
#pragma warning restore RS0030
        return ok ? new EraseOk() : LastError();
    }

    public void Dispose() => _disposed = true;

    private string Resolve(string rel) => Path.GetFullPath(Path.Join(_root, rel.Replace('/', '\\')));

    private static EraseError LastError()
    {
        var error = Marshal.GetLastPInvokeError();
        return new EraseError(error, new Win32Exception(error).Message);
    }

    [LibraryImport("kernel32.dll", EntryPoint = "DeleteFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteFileW(string lpFileName);

    [LibraryImport("kernel32.dll", EntryPoint = "RemoveDirectoryW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RemoveDirectoryW(string lpPathName);
}
