using UasSort.Core;
using UasSort.Core.Offload;
using UasSort.Testing.Offload;
using static UasSort.Testing.Offload.OffloadPlanBuilder;

namespace UasSort.Core.Tests.Offload;

public class OffloadCompilerMetaTests
{
    [Fact]
    public void Describe_GivesKindTimeZoneAndSetPerJob()
    {
        var b = new OffloadPlanBuilder();
        var v = b.Video("DJI_20260927140000_0123_D.MP4", 10, T0, gps: Zachar);
        b.Group(new NewFolder(@"2026\2026-09\2026-09-27 Zachar Bay"), Zachar, v);
        b.Photo("DJI_20260927140500_0124_D.DNG", 20, T0.AddMinutes(5), twinSize: 3);
        b.Set("001_0087", [("PANO_0001.DNG", 5)], T0.AddMinutes(9), SetResolution.Plain);
        var plan = b.Build();
        var batch = OffloadCompiler.Compile(plan, "r");

        var meta = OffloadCompiler.Describe(plan, batch);

        Assert.Equal("video", meta["DCIM/DJI_001/DJI_20260927140000_0123_D.MP4"].Kind);
        Assert.Equal(Zachar, meta["DCIM/DJI_001/DJI_20260927140000_0123_D.MP4"].Point);
        Assert.Equal(TimeSource.Mvhd, meta["DCIM/DJI_001/DJI_20260927140000_0123_D.MP4"].TimeSource);
        Assert.Equal("photo", meta["DCIM/DJI_001/DJI_20260927140500_0124_D.DNG"].Kind);
        Assert.Equal("twin", meta["dcim/dji_001/DJI_20260927140500_0124_D.JPG"].Kind);
        var member = meta["DCIM/PANORAMA/001_0087/PANO_0001.DNG"];
        Assert.Equal("setMember", member.Kind);
        Assert.Equal("001_0087", member.Set);
        Assert.Equal(T0.AddMinutes(9), member.CaptureUtc);
        Assert.Equal(Tz, member.TzId);
        Assert.Equal(new DateOnly(2026, 9, 27), member.LocalDate);
    }

    [Fact]
    public void NewFolderDirs_CoversYearMonthEventAndSetFolders()
    {
        var b = new OffloadPlanBuilder().WithPhotoRoot(@"D:\Photos");
        var v = b.Video("DJI_20260927140000_0123_D.MP4", 10, T0);
        b.Group(new NewFolder(@"2026\2026-09\2026-09-27 Zachar Bay"), null, v);
        b.Set("001_0087", [("PANO_0001.DNG", 5)], T0, SetResolution.Plain);
        b.Photo("DJI_20260927140500_0124_D.DNG", 20, T0);

        var dirs = OffloadCompiler.NewFolderDirs(OffloadCompiler.Compile(b.Build(), "r"), b.VideoRoot);

        Assert.Equal(4, dirs.Count);
        Assert.Contains(@"c:\users\u\onedrive\pictures\uas videos\2026", dirs);
        Assert.Contains(@"C:\Users\u\OneDrive\Pictures\UAS Videos\2026\2026-09", dirs);
        Assert.Contains(@"C:\Users\u\OneDrive\Pictures\UAS Videos\2026\2026-09\2026-09-27 Zachar Bay", dirs);
        Assert.Contains(@"D:\Photos\001_0087", dirs);
    }
}
