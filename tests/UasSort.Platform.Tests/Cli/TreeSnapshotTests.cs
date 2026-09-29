namespace UasSort.Platform.Tests.Cli;

public sealed class TreeSnapshotTests : IDisposable
{
    private readonly TestTempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    /// <summary>The appData snapshot skips exactly the App's WebView2 browser profile: a running App writes there, the CLI never does.</summary>
    [Fact]
    public void TreeSnapshot_SkippedTopFolder_IgnoresChangesThere_AndEverythingElseStaysStrict()
    {
        var root = _temp.FullPath;
        Directory.CreateDirectory(Path.Join(root, "WebView2", "EBWebView"));
        File.WriteAllText(Path.Join(root, "WebView2", "EBWebView", "Local State"), "a");
        File.WriteAllText(Path.Join(root, "settings.json"), "{}");
        var before = TreeSnapshot.Take(root, skipTopFolder: "WebView2");

        File.WriteAllText(Path.Join(root, "WebView2", "EBWebView", "Local State"), "a longer state");
        File.WriteAllText(Path.Join(root, "WebView2", "EBWebView", "lockfile"), "");
        Directory.CreateDirectory(Path.Join(root, "WebView2", "Crashpad"));
        Assert.Equal(before, TreeSnapshot.Take(root, skipTopFolder: "WebView2"));

        Directory.CreateDirectory(Path.Join(root, "logs"));
        File.WriteAllText(Path.Join(root, "logs", "webview2.log"), "x");   // a WebView2-like name elsewhere still counts
        Assert.NotEqual(before, TreeSnapshot.Take(root, skipTopFolder: "WebView2"));
        Assert.Contains(TreeSnapshot.Take(root), e => e.StartsWith(@"WebView2\EBWebView\lockfile|", StringComparison.Ordinal));
    }
}
