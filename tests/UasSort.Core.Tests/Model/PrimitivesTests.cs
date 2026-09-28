namespace UasSort.Core.Tests.Model;

public class PrimitivesTests
{
    [Theory]
    [InlineData(@"C:\V\", @"C:\V")]
    [InlineData("C:/V/2026", @"C:\V\2026")]
    [InlineData(@"\\?\C:\V", @"C:\V")]
    [InlineData("E:", @"E:\")]
    [InlineData(@"E:\", @"E:\")]
    public void Normalize_UsesBackslashesAndDropsTrailingSeparator(string input, string expected)
        => Assert.Equal(expected, PathRules.Normalize(input));

    [Theory]
    [InlineData(@"C:\V\.uas-sort\ledger-A.jsonl", @"C:\V\.uas-sort", true)]
    [InlineData(@"c:\v\.UAS-SORT", @"C:\V\.uas-sort", true)]
    [InlineData(@"C:\V\.uas-sort2\ledger-A.jsonl", @"C:\V\.uas-sort", false)]
    [InlineData(@"E:\DCIM", @"E:\", true)]
    [InlineData(@"D:\x", @"E:\", false)]
    public void IsSameOrUnder_RespectsSeparatorBoundaries(string path, string root, bool expected)
        => Assert.Equal(expected, PathRules.IsSameOrUnder(path, root));

    [Fact]
    public void Parent_WalksUpToTheDriveRoot()
    {
        Assert.Equal(@"E:\DCIM", PathRules.Parent(@"E:\DCIM\DJI_001"));
        Assert.Equal(@"E:\", PathRules.Parent(@"E:\DCIM"));
        Assert.Null(PathRules.Parent(@"E:\"));
    }

    [Fact]
    public void Join_And_RelativeCardPath_RoundTrip()
    {
        var full = PathRules.Join(@"E:\", "DCIM/DJI_001/DJI_20260927140627_0128_D.MP4");
        Assert.Equal(@"E:\DCIM\DJI_001\DJI_20260927140627_0128_D.MP4", full);
        Assert.Equal("DCIM/DJI_001/DJI_20260927140627_0128_D.MP4", PathRules.RelativeCardPath(full, @"E:\"));
        Assert.Equal("DCIM/PANORAMA/001_0087", PathRules.RelativeCardPath(@"C:\Temp\card\DCIM\PANORAMA\001_0087", @"C:\Temp\card"));
    }

    [Fact]
    public void Distance_ConvertsMiles()
    {
        Assert.Equal(80_467.2, Distance.FromMiles(50).Meters, 6);
        Assert.Equal(50, Distance.FromMiles(50).Miles, 9);
    }

    [Theory]
    [InlineData("DJI_20260927140627_0128_D.MP4", "dji_20260927140627_0128_d.mp4")]
    [InlineData("X (2).MP4", "x.mp4")]
    [InlineData("best shot (12).DNG", "best shot.dng")]
    [InlineData("(2).MP4", "(2).mp4")]
    [InlineData("noext (3)", "noext")]
    public void FileKey_NormalizeName_LowercasesAndDropsCopySuffix(string name, string expected)
        => Assert.Equal(expected, FileKey.NormalizeName(name));

    [Theory]
    [InlineData("DCIM/DJI_001/X (2).MP4", "x.mp4")]
    [InlineData(@"C:\a\x.mp4", "x.mp4")]
    [InlineData("x.mp4", "x.mp4")]
    public void FileKey_OfPath_KeysTheLastSegment(string pathOrName, string expectedName)
        => Assert.Equal(new FileKey(expectedName, 42), FileKey.OfPath(pathOrName, 42));

    [Fact]
    public void LedgerPaths_AreDerivedFromTheVideoRoot()
    {
        const string root = @"C:\Users\u\OneDrive\Pictures\UAS Videos";
        Assert.Equal(root + @"\.uas-sort", LedgerPaths.For(root));
        Assert.Equal(root + @"\.uas-sort\ledger-DESKTOP-A.jsonl", LedgerPaths.OwnFile(root, "DESKTOP-A"));
        var a = LedgerPaths.BackupDir(@"C:\L\uas-sort", root);
        Assert.Equal(a, LedgerPaths.BackupDir(@"C:\L\uas-sort", root.ToUpperInvariant()));
        Assert.NotEqual(a, LedgerPaths.BackupDir(@"C:\L\uas-sort", @"D:\Other Videos"));
        Assert.Matches(@"^C:\\L\\uas-sort\\ledger-backup\\[0-9a-f]{16}$", a);
    }

    [Theory]
    [InlineData("ledger-DESKTOP-A.jsonl", true)]
    [InlineData("LEDGER-B.JSONL", true)]
    [InlineData("ledger-B-DESKTOP-A.jsonl", true)]
    [InlineData("notes.txt", false)]
    [InlineData("xledger.jsonl", false)]
    public void IsLedgerFileName_MatchesLedgerStarJsonl(string name, bool expected)
        => Assert.Equal(expected, LedgerPaths.IsLedgerFileName(name));
}
