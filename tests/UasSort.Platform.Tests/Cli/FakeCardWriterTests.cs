namespace UasSort.Platform.Tests.Cli;

public sealed class FakeCardWriterTests
{
    [Fact]
    public void RefusesARootOutsideTheTestTempFolders()
    {
        Assert.Throws<ArgumentException>(() => new FakeCardWriter(@"E:"));
        Assert.Throws<ArgumentException>(() => new FakeCardWriter(Path.Combine(Path.GetTempPath(), "not-a-test-dir")));
    }

    [Fact]
    public void WritesDjiNamesSizesAndMtimes()
    {
        using var temp = new TestTempDir();
        string root = temp.Combine("card");
        new FakeCardWriter(root)
            .AddDjiVideo("DJI_001", new DateTime(2026, 9, 27, 14, 1, 27), 123, new GeoPoint(57.5368, -153.7484))
            .AddFile("MISC/FC9113.db", "FC9113"u8.ToArray())
            .Write();
        var clip = new FileInfo(Path.Combine(root, "DCIM", "DJI_001", "DJI_20260927140127_0123_D.MP4"));
        Assert.True(clip.Length > 0);
        Assert.Equal(new DateTime(2026, 9, 27, 18, 2, 57, DateTimeKind.Utc), clip.LastWriteTimeUtc);   // mvhd 18:01:27Z + 90 s
        Assert.Equal(FakeCardWriter.DefaultMtimeUtc, File.GetLastWriteTimeUtc(Path.Combine(root, "MISC", "FC9113.db")));
    }
}
