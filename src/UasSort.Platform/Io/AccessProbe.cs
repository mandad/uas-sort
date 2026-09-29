namespace UasSort.Platform.Io;

/// <summary>Effective access from the security descriptor only (Ref §11 Ledger folder status); no data handle is opened.</summary>
internal static unsafe class AccessProbe
{
    public const uint FILE_ADD_FILE = 0x2, FILE_APPEND_DATA = 0x4;

    public static bool Has(string path, uint desiredAccess)
    {
        var prefixed = LongPath.Prefix(Path.GetFullPath(path));
        Advapi32.GetFileSecurity(prefixed, Advapi32.OWNER_GROUP_DACL, null, 0, out var needed);
        if (needed == 0) return false;
        var descriptor = new byte[needed];
        nint process = 0, token = 0;
        try
        {
            fixed (byte* sd = descriptor)
            {
                if (!Advapi32.GetFileSecurity(prefixed, Advapi32.OWNER_GROUP_DACL, sd, needed, out _)) return false;
                if (!Advapi32.OpenProcessToken(Kernel32.GetCurrentProcess(), Advapi32.TOKEN_DUPLICATE | Advapi32.TOKEN_QUERY, out process)) return false;
                if (!Advapi32.DuplicateToken(process, Advapi32.SecurityImpersonation, out token)) return false;
                var mapping = new GenericMapping { GenericRead = 0x120089, GenericWrite = 0x120116, GenericExecute = 0x1200A0, GenericAll = 0x1F01FF };
                var privileges = stackalloc byte[256];
                var privilegesLength = 256u;
                return Advapi32.AccessCheck(sd, token, desiredAccess, ref mapping, privileges, ref privilegesLength, out _, out var granted)
                       && granted;
            }
        }
        finally
        {
            if (token != 0) Kernel32.CloseHandle(token);
            if (process != 0) Kernel32.CloseHandle(process);
        }
    }
}
