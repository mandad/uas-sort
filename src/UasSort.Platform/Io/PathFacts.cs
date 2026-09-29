using Microsoft.Win32;

namespace UasSort.Platform.Io;

/// <summary>Canonical paths and cloud sync roots (Ref §4.3 card source validation steps 2–3; the ledger exemption's path matching).</summary>
public sealed class PathFacts : IPathFacts
{
    public string Canonical(string path)
    {
        var full = Path.GetFullPath(LongPath.Strip(path));
        var current = IsRoot(full) ? full : Path.TrimEndingDirectorySeparator(full);
        var tail = new Stack<string>();
        // Walk up to the nearest existing DIRECTORY. A file is never opened here: a placeholder with
        // RecallOnOpen would be hydrated by any open (Ref §4.3 Placeholders).
        while (Kernel32.TryGetAttributes(current, out _) is not uint a || (a & Kernel32.FILE_ATTRIBUTE_DIRECTORY) == 0)
        {
            var parent = Path.GetDirectoryName(current);
            if (parent is null) return full;                       // no existing ancestor (missing drive): lexical form
            tail.Push(Path.GetFileName(current));
            current = parent;
        }
        var resolved = FinalDirectoryPath(current) ?? current;
        foreach (var name in tail) resolved = Path.Join(resolved, name);   // Stack enumerates shallowest first
        return resolved;
    }

    public bool InSyncRoot(string canonicalPath)
    {
        unsafe
        {
            var buffer = stackalloc byte[64];
            uint returned = 0;
            var hr = CldApi.CfGetSyncRootInfoByPath(LongPath.Strip(canonicalPath), CldApi.CF_SYNC_ROOT_INFO_BASIC, buffer, 64, &returned);
            return hr >= 0 || hr == CldApi.HRESULT_ERROR_MORE_DATA;
        }
    }

    public IReadOnlyList<string> SyncRoots()
    {
        var roots = new List<string>();
        using (var accounts = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\OneDrive\Accounts"))
        {
            foreach (var name in accounts?.GetSubKeyNames() ?? [])
            {
                using var account = accounts!.OpenSubKey(name);
                if (account?.GetValue("UserFolder") is string folder && Path.IsPathFullyQualified(folder)) roots.Add(folder);
            }
        }
        using (var manager = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\SyncRootManager"))
        {
            foreach (var name in manager?.GetSubKeyNames() ?? [])
            {
                using var user = manager!.OpenSubKey(name + @"\UserSyncRoots");
                foreach (var valueName in user?.GetValueNames() ?? [])
                    if (user!.GetValue(valueName) is string folder && Path.IsPathFullyQualified(folder)) roots.Add(folder);
            }
        }
        return roots.Select(r => Path.TrimEndingDirectorySeparator(r)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsRoot(string full) => string.Equals(Path.GetPathRoot(full), full, StringComparison.OrdinalIgnoreCase);

    private static unsafe string? FinalDirectoryPath(string directory)
    {
        using var handle = Kernel32.CreateFile(LongPath.Prefix(directory), 0 /* no data access */,
            Kernel32.FILE_SHARE_READ | Kernel32.FILE_SHARE_WRITE | Kernel32.FILE_SHARE_DELETE, 0, Kernel32.OPEN_EXISTING,
            Kernel32.FILE_FLAG_BACKUP_SEMANTICS, 0);
        if (handle.IsInvalid) return null;
        var size = 512u;
        while (true)
        {
            var buffer = new char[size];
            fixed (char* p = buffer)
            {
                var n = Kernel32.GetFinalPathNameByHandle(handle, p, size, 0 /* FILE_NAME_NORMALIZED | VOLUME_NAME_DOS */);
                if (n == 0) return null;
                if (n < size)
                {
                    var s = LongPath.Strip(new string(p, 0, (int)n));
                    return IsRoot(s) ? s : Path.TrimEndingDirectorySeparator(s);
                }
                size = n + 1;
            }
        }
    }
}
