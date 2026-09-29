using UasSort.Core;
using UasSort.Core.Offload;

namespace UasSort.Core.Tests.Offload;

public class OffloadPathsTests
{
    [Fact]
    public void Join_UsesBackslashes()
        => Assert.Equal(@"C:\V\2026\2026-09\2026-09-27 Zachar Bay",
                        OffloadPaths.Join(@"C:\V\", "2026/2026-09/2026-09-27 Zachar Bay"));

    [Theory]
    [InlineData(@"C:\Lib\x.mp4", @"C:\")]
    [InlineData(@"d:\Photos\a.dng", @"D:\")]
    [InlineData(@"\\?\D:\x\y.dng", @"D:\")]
    [InlineData(@"\\nas\share\v\x.mp4", @"\\nas\share\")]
    public void VolumeRoot_FindsTheVolume(string path, string expected)
        => Assert.Equal(expected, OffloadPaths.VolumeRoot(path));

    [Fact]
    public void DriveLabel_DropsTheSeparator() => Assert.Equal("E:", OffloadPaths.DriveLabel(@"E:\"));

    [Theory]
    [InlineData("DCIM/DJI_001/DJI_1 (2).MP4", "dji_1.mp4")]
    [InlineData("PANO_0001.DNG", "pano_0001.dng")]
    [InlineData(@"C:\V\A (12).dng", "a.dng")]
    public void NormName_MatchesTheLibraryKeyRule(string path, string expected)
        => Assert.Equal(expected, OffloadPaths.NormName(path));

    [Fact]
    public void Key_UsesNormNameAndSize()
        => Assert.Equal(new FileKey("x.mp4", 7), OffloadPaths.Key("DCIM/DJI_001/X.MP4", 7));

    [Theory]
    [InlineData("DJI_1.MP4", 2, "DJI_1 (2).MP4")]
    [InlineData("noext", 3, "noext (3)")]
    public void WithCopyNumber_InsertsBeforeTheExtension(string name, int n, string expected)
        => Assert.Equal(expected, OffloadPaths.WithCopyNumber(name, n));

    [Fact]
    public void NewFolderDirs_ListsYearMonthAndFolder()
        => Assert.Equal(
            [@"C:\V\2026", @"C:\V\2026\2026-09", @"C:\V\2026\2026-09\2026-09-27 Z"],
            OffloadPaths.NewFolderDirs(@"C:\V", @"C:\V\2026\2026-09\2026-09-27 Z").ToArray());

    [Fact]
    public void NewFolderDirs_OutsideTheVideoRoot_IsJustTheFolder()
        => Assert.Equal([@"D:\Photos\001_0087"], OffloadPaths.NewFolderDirs(@"C:\V", @"D:\Photos\001_0087").ToArray());

    [Theory]
    [InlineData(@"C:\V\a", @"C:\V", true)]
    [InlineData(@"c:\v\A", @"C:\V\", true)]
    [InlineData(@"C:\V2\a", @"C:\V", false)]
    public void IsUnder_RespectsSeparators(string path, string root, bool expected)
        => Assert.Equal(expected, OffloadPaths.IsUnder(path, root));

    [Fact]
    public void TempOf_And_IsTemp()
    {
        Assert.Equal(@"C:\V\x.MP4.uas-sort.tmp", OffloadPaths.TempOf(@"C:\V\x.MP4"));
        Assert.True(OffloadPaths.IsTemp(@"C:\V\x.MP4.UAS-SORT.TMP"));
        Assert.False(OffloadPaths.IsTemp(@"C:\V\x.MP4"));
    }

    [Fact]
    public void Gb_TwoDecimals() => Assert.Equal("32.47 GB", OffloadPaths.Gb(32_473_741_824));
}
