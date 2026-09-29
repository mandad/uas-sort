using System.Text;

namespace UasSort.Platform.Tests.Io;

public sealed class ReadOnlyTextFileTests : IDisposable
{
    private readonly TestTempDir _temp = new();
    private string _dir => _temp.FullPath;

    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories))
            File.SetAttributes(f, FileAttributes.Normal);
        _temp.Dispose();
    }

    [Fact]
    public void Reads_Utf8WithBom_WhileAnotherHandleWrites()
    {
        string path = Path.Combine(_dir, "e.json");
        File.WriteAllText(path, "{ \"folders\": [] } · ok", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        using var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        Assert.Equal("{ \"folders\": [] } · ok", ReadOnlyTextFile.Read(path, 1000));
    }

    [Fact]
    public void Refuses_ACloudOnlyPlaceholder_WithoutOpeningIt()
    {
        string path = Path.Combine(_dir, "cloud.json");
        File.WriteAllText(path, "{}");
        File.SetAttributes(path, FileAttributes.Offline);
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);   // any open would fail differently
        var ex = Assert.Throws<IOException>(() => ReadOnlyTextFile.Read(path, 1000));
        Assert.Contains("cloud-only", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_AFileOverTheCap()
    {
        string path = Path.Combine(_dir, "big.json");
        File.WriteAllText(path, new string('x', 2000));
        var ex = Assert.Throws<IOException>(() => ReadOnlyTextFile.Read(path, 1000));
        Assert.Contains("larger than", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_AFolder_And_ReportsAMissingFile()
    {
        Assert.Throws<IOException>(() => ReadOnlyTextFile.Read(_dir, 1000));
        Assert.Throws<FileNotFoundException>(() => ReadOnlyTextFile.Read(Path.Combine(_dir, "missing.json"), 1000));
    }
}
