using System.Diagnostics;
using System.Runtime.InteropServices;

namespace UasSort.Platform.Win32;

/// <summary>
/// Finds the WebView2 browser processes of one user-data folder by their command line (<c>--user-data-dir=</c>), so the selftest
/// never deletes its sandbox from under a live msedgewebview2.exe (which then shows Edge's "can't read and write to its data
/// directory" dialog). Only processes of the given name whose user-data-dir is at or under the given folder are ever matched,
/// waited for or terminated; every other Edge or WebView2 process is left alone.
/// </summary>
public static partial class WebView2Processes
{
    public const string BrowserProcessName = "msedgewebview2";
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int ProcessCommandLineInformation = 60;
    private const string UserDataDirSwitch = "--user-data-dir=";

    /// <summary>The processes named <paramref name="processName"/> whose --user-data-dir is at or under <paramref name="folder"/>.
    /// The caller disposes them.</summary>
    public static IReadOnlyList<Process> Under(string folder, string processName = BrowserProcessName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        var found = new List<Process>();
        foreach (var p in Process.GetProcessesByName(processName))
        {
            if (CommandLine(p.Id) is { } line && UserDataDirIsUnder(line, folder)) found.Add(p);
            else p.Dispose();
        }
        return found;
    }

    /// <summary>Waits up to <paramref name="wait"/> for this folder's processes to exit, then terminates only those still running.
    /// Returns how many are still alive afterwards (0 = the folder is free of them).</summary>
    public static int Stop(string folder, TimeSpan wait, string processName = BrowserProcessName)
    {
        var deadline = TimeProvider.System.GetTimestamp() + (long)(wait.TotalSeconds * TimeProvider.System.TimestampFrequency);
        var alive = Under(folder, processName);
        try
        {
            foreach (var p in alive)
            {
                var left = TimeSpan.FromSeconds(Math.Max(0, deadline - TimeProvider.System.GetTimestamp()) / (double)TimeProvider.System.TimestampFrequency);
                if (!p.WaitForExit(left))
                {
                    try { p.Kill(); }
                    catch (InvalidOperationException) { }                     // exited meanwhile
                    catch (System.ComponentModel.Win32Exception) { }          // access denied: counted below
                    p.WaitForExit(TimeSpan.FromSeconds(2));
                }
            }
            return alive.Count(p => !HasExited(p));
        }
        finally
        {
            foreach (var p in alive) p.Dispose();
        }
    }

    /// <summary>True when the command line carries --user-data-dir equal to <paramref name="folder"/> or a folder under it.</summary>
    public static bool UserDataDirIsUnder(string commandLine, string folder)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        ArgumentNullException.ThrowIfNull(folder);
        var root = folder.TrimEnd('\\');
        for (var at = commandLine.IndexOf(UserDataDirSwitch, StringComparison.OrdinalIgnoreCase); at >= 0;
             at = commandLine.IndexOf(UserDataDirSwitch, at + 1, StringComparison.OrdinalIgnoreCase))
        {
            var start = at + UserDataDirSwitch.Length;
            string value;
            if (start < commandLine.Length && commandLine[start] == '"')
            {
                var close = commandLine.IndexOf('"', start + 1);
                value = close < 0 ? commandLine[(start + 1)..] : commandLine[(start + 1)..close];
            }
            else
            {
                var space = commandLine.IndexOf(' ', start);
                value = space < 0 ? commandLine[start..] : commandLine[start..space];
            }
            value = value.TrimEnd('\\');
            if (value.Equals(root, StringComparison.OrdinalIgnoreCase)
                || value.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>The process's command line (NtQueryInformationProcess, ProcessCommandLineInformation), or null when it can't be read.</summary>
    public static string? CommandLine(int pid)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)pid);
        if (handle == 0) return null;
        var buffer = Marshal.AllocHGlobal(64 * 1024);
        try
        {
            if (NtQueryInformationProcess(handle, ProcessCommandLineInformation, buffer, 64 * 1024, out _) != 0) return null;
            var length = (ushort)Marshal.ReadInt16(buffer);                  // UNICODE_STRING.Length, in bytes
            var text = Marshal.ReadIntPtr(buffer, IntPtr.Size);              // UNICODE_STRING.Buffer (after Length, MaximumLength, padding)
            return text == 0 ? null : Marshal.PtrToStringUni(text, length / 2);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            _ = CloseProcessHandle(handle);
        }
    }

    private static bool HasExited(Process p)
    {
        try { return p.HasExited; }
        catch (InvalidOperationException) { return true; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "OpenProcess", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [LibraryImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseProcessHandle(nint handle);

    [LibraryImport("ntdll.dll", EntryPoint = "NtQueryInformationProcess")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int NtQueryInformationProcess(nint process, int infoClass, nint info, int infoLength, out int returnLength);
}
