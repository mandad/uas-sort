using System.Runtime.InteropServices;

namespace UasSort.Platform.Win32;

[StructLayout(LayoutKind.Sequential)]
internal struct GenericMapping
{
    public uint GenericRead, GenericWrite, GenericExecute, GenericAll;
}

internal static unsafe partial class Advapi32
{
    public const uint OWNER_GROUP_DACL = 0x1 | 0x2 | 0x4;
    public const uint TOKEN_DUPLICATE = 0x2, TOKEN_QUERY = 0x8;
    public const int SecurityImpersonation = 2;

    [LibraryImport("advapi32.dll", EntryPoint = "GetFileSecurityW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetFileSecurity(string lpFileName, uint requestedInformation, byte* pSecurityDescriptor, uint nLength,
                                               out uint lpnLengthNeeded);

    [LibraryImport("advapi32.dll", EntryPoint = "OpenProcessToken", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenProcessToken(nint processHandle, uint desiredAccess, out nint tokenHandle);

    [LibraryImport("advapi32.dll", EntryPoint = "DuplicateToken", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DuplicateToken(nint existingTokenHandle, int impersonationLevel, out nint duplicateTokenHandle);

    [LibraryImport("advapi32.dll", EntryPoint = "AccessCheck", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AccessCheck(byte* pSecurityDescriptor, nint clientToken, uint desiredAccess, ref GenericMapping genericMapping,
                                           byte* privilegeSet, ref uint privilegeSetLength, out uint grantedAccess,
                                           [MarshalAs(UnmanagedType.Bool)] out bool accessStatus);
}

internal static partial class Kernel32
{
    [LibraryImport("kernel32.dll", EntryPoint = "GetCurrentProcess")]
    public static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);
}
