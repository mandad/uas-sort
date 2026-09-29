using System.Runtime.InteropServices;

namespace UasSort.Platform.Win32;

[StructLayout(LayoutKind.Sequential)]
internal struct ReasonContext
{
    public uint Version;                 // POWER_REQUEST_CONTEXT_VERSION = 0
    public uint Flags;                   // POWER_REQUEST_CONTEXT_SIMPLE_STRING = 1
    public nint SimpleReasonString;
    public nint DetailedPadding1, DetailedPadding2;   // the union's Detailed arm is 24 bytes on 64-bit
}

[StructLayout(LayoutKind.Sequential)]
internal struct StorageDeviceNumber
{
    public uint DeviceType, DeviceNumber, PartitionNumber;
}

internal static partial class Kernel32
{
    public const int PowerRequestSystemRequired = 1;

    [LibraryImport("kernel32.dll", EntryPoint = "PowerCreateRequest", SetLastError = true)]
    public static partial nint PowerCreateRequest(in ReasonContext context);

    [LibraryImport("kernel32.dll", EntryPoint = "PowerSetRequest", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PowerSetRequest(nint powerRequest, int requestType);

    [LibraryImport("kernel32.dll", EntryPoint = "PowerClearRequest", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PowerClearRequest(nint powerRequest, int requestType);
}
