using System.Diagnostics;

namespace UasSort.Platform.Tests;

internal static class Cmd
{
    public static int Run(string arguments)
    {
        // A raw command line, not ArgumentList: ArgumentList escapes embedded quotes as \" and cmd.exe does not
        // understand that ("The filename, directory name, or volume label syntax is incorrect.").
        var psi = new ProcessStartInfo("cmd.exe", "/c " + arguments) { UseShellExecute = false, CreateNoWindow = true,
                                                                       RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = Process.Start(psi)!;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode;
    }

    /// <summary>A drive letter that is not in use, from Z: down to M:.</summary>
    public static char FreeDriveLetter()
    {
        var used = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        for (var c = 'Z'; c >= 'M'; c--) if (!used.Contains(c)) return c;
        throw new InvalidOperationException("no free drive letter for the subst test");
    }
}
