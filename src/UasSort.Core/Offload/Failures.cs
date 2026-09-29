namespace UasSort.Core.Offload;

/// <summary>How the offload tells IO failures apart (Ref §10.3 outcome table).</summary>
internal static class Failures
{
    /// <summary>An ordinary IO failure. UnsafeIoException (a guard refusal) is never one: it stops the run.</summary>
    public static bool IsIo(Exception e)
        => (e is IOException || e is UnauthorizedAccessException) && e is not UnsafeIoException;

    /// <summary>ERROR_DISK_FULL (0x70) or ERROR_HANDLE_DISK_FULL (0x27), as the low word of the HRESULT.</summary>
    public static bool IsDiskFull(Exception e) => e is IOException && (e.HResult & 0xFFFF) is 0x70 or 0x27;
}
