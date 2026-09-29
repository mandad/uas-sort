namespace UasSort.Platform.Stores;

/// <summary>
/// The --selftest sandbox (Ref §11 "Selftest" row): %TEMP%\uas-sort-selftest-&lt;guid&gt;\ holding the WebView2 user data
/// folder (and, from Part 11, the app-data folder, video/photo roots and card root). Deleted at exit. Also writes the
/// result file. Partial: Part 11 (Task 11.3) adds its members in a second file; nothing redeclares this class.
/// </summary>
public sealed partial class SelfTestSandbox : IDisposable
{
    public const string FolderPrefix = "uas-sort-selftest-";

    private SelfTestSandbox(string root) => Root = root;

    public string Root { get; }

    public string WebView2Folder => Path.Join(Root, "WebView2");

    public static SelfTestSandbox Create(string tempRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tempRoot);
        var root = Path.Join(Path.GetFullPath(tempRoot), FolderPrefix + Guid.NewGuid().ToString("N"));
#pragma warning disable RS0030 // IO layer: selftest sandbox, a fresh folder under %TEMP% (Ref §11)
        Directory.CreateDirectory(root);
#pragma warning restore RS0030
        return new SelfTestSandbox(root);
    }

    /// <summary>Writes selftest-result.json where the caller (deploy.ps1, tools/run-selftest.ps1) asked for it.</summary>
    public static void WriteResult(string path, ReadOnlySpan<byte> utf8Json)
    {
        if (!Path.IsPathFullyQualified(path) || !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The selftest result path must be a fully qualified .json path.", nameof(path));
        }

        var temp = path + ".tmp";
#pragma warning disable RS0030 // IO layer: the selftest result file named on the command line
        File.WriteAllBytes(temp, utf8Json);
        File.Move(temp, path, overwrite: true);
#pragma warning restore RS0030
    }

    /// <summary>How long this run's WebView2 processes get to exit before only they are terminated (F19).</summary>
    public static readonly TimeSpan BrowserExitWait = TimeSpan.FromSeconds(5);

    /// <summary>Why the last TryDelete left the sandbox in place (null after a clean delete).</summary>
    public string? CleanupProblem { get; private set; }

    /// <summary>Deletes the sandbox only once no msedgewebview2.exe of this run (its --user-data-dir under Root) is alive: they get
    /// <see cref="BrowserExitWait"/> to exit, then only those are terminated. A survivor leaves the sandbox in place (never pulled
    /// from under a live Edge process, which shows Edge's "can't read and write to its data directory" dialog); so does a folder
    /// that still can't be deleted after 3 s of retries. <see cref="CleanupProblem"/> then says why.</summary>
    public bool TryDelete()
    {
        CleanupProblem = null;
        var alive = WebView2Processes.Stop(Root, BrowserExitWait);
        if (alive > 0)
        {
            CleanupProblem = $"{alive} WebView2 process(es) using {WebView2Folder} are still running; the sandbox {Root} was left in place";
            return false;
        }
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (!Directory.Exists(Root))
            {
                return true;
            }

            try
            {
#pragma warning disable RS0030 // IO layer: deletes only this run's own selftest sandbox
                Directory.Delete(Root, recursive: true);
#pragma warning restore RS0030
                return true;
            }
            catch (IOException)
            {
                Thread.Sleep(300);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(300);
            }
        }

        if (!Directory.Exists(Root)) return true;
        CleanupProblem = $"the sandbox {Root} couldn't be deleted (a file in it is still in use); it was left in place";
        return false;
    }

    public void Dispose() => TryDelete();
}
