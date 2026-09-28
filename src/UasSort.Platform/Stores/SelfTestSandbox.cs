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

    /// <summary>Deletes the sandbox; retries while WebView2's child processes release their files (up to 3 s).</summary>
    public bool TryDelete()
    {
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

        return !Directory.Exists(Root);
    }

    public void Dispose() => TryDelete();
}
