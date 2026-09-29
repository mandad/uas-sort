using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace UasSort.Platform.Win32;

[StructLayout(LayoutKind.Sequential)]
internal struct Win32FileAttributeData
{
    public uint FileAttributes;
    public uint CreationLow, CreationHigh;
    public uint LastAccessLow, LastAccessHigh;
    public uint LastWriteLow, LastWriteHigh;
    public uint SizeHigh, SizeLow;

    public readonly long Size => ((long)SizeHigh << 32) | SizeLow;
    public readonly DateTime CreationUtc => ToUtc(CreationHigh, CreationLow);
    public readonly DateTime LastAccessUtc => ToUtc(LastAccessHigh, LastAccessLow);
    public readonly DateTime LastWriteUtc => ToUtc(LastWriteHigh, LastWriteLow);
    private static DateTime ToUtc(uint high, uint low) => DateTime.FromFileTimeUtc(((long)high << 32) | low);
}

/// <summary>WIN32_FIND_DATAW; <see cref="Reserved0"/> is the reparse tag when FILE_ATTRIBUTE_REPARSE_POINT is set.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct Win32FindData
{
    public uint FileAttributes;
    public uint CreationLow, CreationHigh;
    public uint LastAccessLow, LastAccessHigh;
    public uint LastWriteLow, LastWriteHigh;
    public uint SizeHigh, SizeLow;
    public uint Reserved0, Reserved1;
    public fixed ushort FileName[260];
    public fixed ushort AlternateFileName[14];
}

internal static unsafe partial class Kernel32
{
    public const int ERROR_FILE_NOT_FOUND = 2, ERROR_PATH_NOT_FOUND = 3, ERROR_ACCESS_DENIED = 5, ERROR_NOT_READY = 21,
                     ERROR_WRITE_PROTECT = 19, ERROR_SHARING_VIOLATION = 32, ERROR_FILE_EXISTS = 80, ERROR_INVALID_NAME = 123,
                     ERROR_DIR_NOT_EMPTY = 145, ERROR_ALREADY_EXISTS = 183, ERROR_MORE_DATA = 234, ERROR_DEVICE_NOT_CONNECTED = 1167;
    public const int ERROR_DIRECTORY = 267;
    public const uint FILE_ATTRIBUTE_READONLY = 0x1, FILE_ATTRIBUTE_HIDDEN = 0x2, FILE_ATTRIBUTE_DIRECTORY = 0x10,
                      FILE_ATTRIBUTE_REPARSE_POINT = 0x400,
                      FILE_ATTRIBUTE_NOT_CONTENT_INDEXED = 0x2000, FILE_ATTRIBUTE_OFFLINE = 0x1000,
                      FILE_ATTRIBUTE_RECALL_ON_OPEN = 0x40000, FILE_ATTRIBUTE_PINNED = 0x80000, FILE_ATTRIBUTE_UNPINNED = 0x100000,
                      FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS = 0x400000;
    public const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
    public const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, FILE_SHARE_DELETE = 4;
    public const uint OPEN_EXISTING = 3;
    public const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000, FILE_FLAG_NO_BUFFERING = 0x20000000;
    public const uint MOVEFILE_WRITE_THROUGH = 0x8;
    private const int GetFileExInfoStandard = 0;
    private const int FindExInfoBasic = 1, FindExSearchNameMatch = 0;

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileAttributesExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetFileAttributesEx(string lpFileName, int fInfoLevelId, out Win32FileAttributeData lpFileInformation);

    [LibraryImport("kernel32.dll", EntryPoint = "FindFirstFileExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    public static partial nint FindFirstFileEx(string lpFileName, int fInfoLevelId, out Win32FindData lpFindFileData,
                                               int fSearchOp, nint lpSearchFilter, uint dwAdditionalFlags);

    [LibraryImport("kernel32.dll", EntryPoint = "FindClose", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FindClose(nint hFindFile);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    public static partial SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, nint lpSecurityAttributes,
                                                    uint dwCreationDisposition, uint dwFlagsAndAttributes, nint hTemplateFile);

    [LibraryImport("kernel32.dll", EntryPoint = "SetFileAttributesW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetFileAttributes(string lpFileName, uint dwFileAttributes);

    [LibraryImport("kernel32.dll", EntryPoint = "FlushFileBuffers", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FlushFileBuffers(SafeFileHandle hFile);

    [LibraryImport("kernel32.dll", EntryPoint = "MoveFileExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool MoveFileEx(string lpExistingFileName, string lpNewFileName, uint dwFlags);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
    public static partial uint GetFinalPathNameByHandle(SafeFileHandle hFile, char* lpszFilePath, uint cchFilePath, uint dwFlags);

    /// <summary>Attributes without opening anything; null when the call fails (error in <paramref name="error"/>).</summary>
    public static uint? TryGetAttributes(string fullPath, out int error)
    {
        if (GetFileAttributesEx(LongPath.Prefix(fullPath), GetFileExInfoStandard, out var data)) { error = 0; return data.FileAttributes; }
        error = Marshal.GetLastPInvokeError();
        return null;
    }

    /// <summary>
    /// The entry's own find data, read from its parent's directory listing (FindFirstFileExW with the exact name, no
    /// wildcard): the entry itself is never opened, so a cloud placeholder is not hydrated. False when the call fails.
    /// </summary>
    public static bool TryFindEntry(string fullPath, out Win32FindData data, out int error)
    {
        var handle = FindFirstFileEx(LongPath.Prefix(fullPath), FindExInfoBasic, out data, FindExSearchNameMatch, 0, 0);
        if (handle == -1) { error = Marshal.GetLastPInvokeError(); return false; }
        FindClose(handle);
        error = 0;
        return true;
    }

    public static bool TryGetAttributeData(string fullPath, out Win32FileAttributeData data, out int error)
    {
        if (GetFileAttributesEx(LongPath.Prefix(fullPath), GetFileExInfoStandard, out data)) { error = 0; return true; }
        error = Marshal.GetLastPInvokeError();
        return false;
    }

    public static bool IsNotFound(int error) => error is ERROR_FILE_NOT_FOUND or ERROR_PATH_NOT_FOUND;
}
