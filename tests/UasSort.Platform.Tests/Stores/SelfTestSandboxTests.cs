using UasSort.Platform.Stores;
using UasSort.Testing;

namespace UasSort.Platform.Tests.Stores;

public sealed class SelfTestSandboxTests
{
    [Fact]
    public void Create_MakesAFreshFolderUnderTheGivenTempRoot()
    {
        using var temp = new TestTempDir();
        using var sandbox = SelfTestSandbox.Create(temp.FullPath);
        Assert.True(Directory.Exists(sandbox.Root));
        Assert.StartsWith(Path.Join(temp.FullPath, "uas-sort-selftest-"), sandbox.Root, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Path.Join(sandbox.Root, "WebView2"), sandbox.WebView2Folder);
    }

    [Fact]
    public void Create_TwiceGivesTwoDifferentFolders()
    {
        using var temp = new TestTempDir();
        using var a = SelfTestSandbox.Create(temp.FullPath);
        using var b = SelfTestSandbox.Create(temp.FullPath);
        Assert.NotEqual(a.Root, b.Root);
    }

    [Fact]
    public void Dispose_DeletesTheFolderAndEverythingInIt()
    {
        using var temp = new TestTempDir();
        var sandbox = SelfTestSandbox.Create(temp.FullPath);
        Directory.CreateDirectory(Path.Join(sandbox.WebView2Folder, "EBWebView"));
        File.WriteAllText(Path.Join(sandbox.WebView2Folder, "EBWebView", "Local State"), "{}");
        sandbox.Dispose();
        Assert.False(Directory.Exists(sandbox.Root));
    }

    [Fact]
    public void WriteResult_WritesExactBytes_ReplacingAnOlderResult()
    {
        using var temp = new TestTempDir();
        var path = temp.Combine("selftest-result.json");
        File.WriteAllText(path, "old");
        SelfTestSandbox.WriteResult(path, """{"ok":true,"firstFrameMs":312,"checks":[]}"""u8);
        Assert.Equal("""{"ok":true,"firstFrameMs":312,"checks":[]}""", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Theory]
    [InlineData("selftest-result.json")]
    [InlineData(@"C:\uas-sort-test\selftest-result.txt")]
    public void WriteResult_RefusesRelativeOrNonJsonPaths(string path)
    {
        Assert.Throws<ArgumentException>(() => SelfTestSandbox.WriteResult(path, "{}"u8));
    }
}
