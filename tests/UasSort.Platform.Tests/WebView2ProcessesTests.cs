using System.Diagnostics;

namespace UasSort.Platform.Tests;

/// <summary>F19: the selftest only ever waits for or stops the WebView2 processes of its own sandbox, found by --user-data-dir.</summary>
public sealed class WebView2ProcessesTests
{
    private const string Sandbox = @"C:\T\uas-sort-selftest-1";

    [Theory]
    [InlineData(@"msedgewebview2.exe --type=browser --user-data-dir=""C:\T\uas-sort-selftest-1\WebView2\EBWebView"" --x", true)]
    [InlineData(@"msedgewebview2.exe --user-data-dir=C:\T\uas-sort-selftest-1\WebView2\EBWebView --x", true)]
    [InlineData(@"msedgewebview2.exe --USER-DATA-DIR=""c:\t\UAS-SORT-SELFTEST-1""", true)]
    [InlineData(@"msedgewebview2.exe --user-data-dir=""C:\T\uas-sort-selftest-10\WebView2""", false)]
    [InlineData(@"msedgewebview2.exe --user-data-dir=""C:\Users\u\AppData\Local\uas-sort\WebView2\EBWebView""", false)]
    [InlineData(@"msedgewebview2.exe --type=renderer --lang=en-US", false)]
    public void UserDataDirIsUnder_MatchesOnlyThisFolder(string commandLine, bool expected)
        => Assert.Equal(expected, WebView2Processes.UserDataDirIsUnder(commandLine, Sandbox));

    [Fact]
    public void Stop_TerminatesOnlyTheProcessesOfThisFolder_AndLeavesNoneBehind()
    {
        using var temp = new TempDir();
        var mine = temp.Sub("uas-sort-selftest-a");
        var other = temp.Sub("uas-sort-selftest-b");
        using var p1 = Sleeper(Path.Join(mine, "WebView2", "EBWebView"));
        using var p2 = Sleeper(Path.Join(other, "WebView2", "EBWebView"));
        try
        {
            Assert.Contains("--user-data-dir=", WebView2Processes.CommandLine(p1.Id), StringComparison.Ordinal);
            var found = WebView2Processes.Under(mine, "powershell");
            Assert.Equal([p1.Id], found.Select(p => p.Id).ToArray());
            foreach (var p in found) p.Dispose();

            Assert.Equal(0, WebView2Processes.Stop(mine, TimeSpan.FromMilliseconds(200), "powershell"));

            Assert.True(p1.HasExited);
            Assert.False(p2.HasExited);                                        // another folder's process is never touched
            Assert.Empty(WebView2Processes.Under(mine, "powershell"));
        }
        finally
        {
            foreach (var p in new[] { p1, p2 })
                if (!p.HasExited) { p.Kill(); p.WaitForExit(); }
        }
    }

    /// <summary>A harmless stand-in that carries a WebView2-style --user-data-dir on its command line (a PowerShell comment).</summary>
    private static Process Sleeper(string userDataDir)
    {
        var info = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -Command \"Start-Sleep -Seconds 60 # --user-data-dir={userDataDir} x\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        return Process.Start(info)!;
    }
}