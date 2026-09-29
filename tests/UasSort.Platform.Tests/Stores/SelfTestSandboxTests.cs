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

    [Fact]
    public void Create_WithoutArgument_MakesTheFourRootsUnderTemp()
    {
        using var sandbox = SelfTestSandbox.Create();
        Assert.StartsWith(Path.Join(Path.GetTempPath(), SelfTestSandbox.FolderPrefix), sandbox.Root, StringComparison.OrdinalIgnoreCase);
        foreach (var dir in new[] { sandbox.AppDataDir, sandbox.VideoRoot, sandbox.PhotoRoot, sandbox.CardRoot })
        {
            Assert.True(Directory.Exists(dir), dir);
            Assert.Equal(sandbox.Root, Path.GetDirectoryName(dir));
        }
    }

    [Fact]
    public void WriteFile_WritesUnderTheRoot_AndRefusesAPathThatLeavesIt()
    {
        using var temp = new TestTempDir();
        using var sandbox = SelfTestSandbox.Create(temp.FullPath);
        sandbox.WriteFile(@"card\DCIM\DJI_001\a.bin", "abc"u8);
        Assert.Equal("abc", File.ReadAllText(Path.Join(sandbox.Root, @"card\DCIM\DJI_001\a.bin")));
        Assert.Throws<InvalidOperationException>(() => sandbox.WriteFile(@"..\escape.bin", "x"u8));
        Assert.False(File.Exists(Path.Join(temp.FullPath, "escape.bin")));
    }
}
