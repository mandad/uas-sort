#pragma warning disable RS0030 // Golden checks only: read-only opens of COPIES under UASSORT_GOLDEN (Ref §13)
using UasSort.Core.Media;
using UasSort.Testing;
using static UasSort.Core.Tests.Media.ProbeAssert;

namespace UasSort.Core.Tests.Media;

public sealed class GoldenMediaTests
{
    [Theory]
    [InlineData(@"E:\")]
    [InlineData(@"E:")]
    [InlineData(@"\\nas\share\")]
    [InlineData(@"C:\Users\pilot\OneDrive\Pictures\UAS Videos\2026")]
    [InlineData(@"C:\Users\pilot\OneDrive - Contoso\golden")]
    [InlineData(@"D:\Picture Offload\001_0087")]
    [InlineData(@"C:\Users\pilot\Pictures\golden")]
    public void GoldenFolder_RefusesUserDataLocations(string path)
        => Assert.NotNull(GoldenFolder.Refusal(path, [@"C:\Users\pilot\Pictures"]));

    [Theory]
    [InlineData(@"C:\Temp\uas-golden")]
    [InlineData(@"C:\Users\pilot\Pictures2\golden")]
    public void GoldenFolder_AllowsAFolderOfCopies(string path)
        => Assert.Null(GoldenFolder.Refusal(path, [@"C:\Users\pilot\Pictures"]));

    [Fact]
    public void GoldenFolder_RefusesConfiguredLibraryRoots()
    {
        IReadOnlyList<string> roots = GoldenFolder.ConfiguredRoots(
            """{"videoRoot":"D:\\Vids","photoRoot":"D:\\Photos","previousPhotoRoots":["F:\\Old"]}""");

        Assert.Equal([@"D:\Vids", @"D:\Photos", @"F:\Old"], roots);
        Assert.NotNull(GoldenFolder.Refusal(@"D:\Vids\golden", roots));
        Assert.NotNull(GoldenFolder.Refusal(@"F:\Old\x", roots));
    }

    [Fact]
    public void Golden_Zachar0128_MatchesExiftool()
    {
        string? path = GoldenFolder.File("DJI_20260927140627_0128_D.MP4");
        if (path is null)
        {
            Assert.Skip("UASSORT_GOLDEN is not set or holds no DJI_20260927140627_0128_D.MP4");
            return;
        }
        using Stream s = File.OpenRead(path);

        Mp4Info info = Mp4Probe.Read(s);

        GpsFix fix = Fix(info.First);
        Assert.Equal(57.5504420579265, fix.Point.Lat, 1e-12);
        Assert.Equal(-153.738972972093, fix.Point.Lon, 1e-12);
        Assert.Equal(298.394, fix.AltM!.Value, 1e-6);
        Assert.Equal(0, fix.Sample);
        Assert.Equal(GpsSource.DjmdModelTable, fix.Source);
        Assert.Equal(new DateTime(2026, 9, 27, 18, 6, 27, DateTimeKind.Utc), info.MvhdUtc);
        Assert.Equal("dvtm_Air3s.proto", info.Protocol);
        Assert.True(info.Duration > TimeSpan.Zero);
        ByteRange thumb = info.Thumb!.Value;
        var soi = new byte[2];
        s.Position = thumb.Offset;
        s.ReadExactly(soi);
        Assert.Equal([0xFF, 0xD8], soi);
    }

    [Fact]
    public void Golden_Pano0001_DtoAndGps()
    {
        string? path = GoldenFolder.File("PANO_0001.DNG");
        if (path is null)
        {
            Assert.Skip("UASSORT_GOLDEN is not set or holds no PANO_0001.DNG");
            return;
        }
        using Stream s = File.OpenRead(path);

        StillInfo info = StillProbe.Read(s);

        Assert.Equal(new DateTime(2026, 5, 25, 9, 30, 28), info.DtoNaive);
        GpsFix fix = Fix(info.Gps);
        Assert.Equal(57.799648, fix.Point.Lat, 1e-6);
        Assert.Equal(-152.390180, fix.Point.Lon, 1e-6);
    }
}
