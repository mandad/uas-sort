// tests/UasSort.Core.Tests/Cleanup/CleanupHelpersTests.cs
namespace UasSort.Core.Tests.Cleanup;

public class CleanupHelpersTests
{
    [Theory]
    [InlineData("DJI_20260725232655_0117_D (2).MP4", "dji_20260725232655_0117_d.mp4")]
    [InlineData("DJI_20260725232655_0117_D.MP4", "dji_20260725232655_0117_d.mp4")]
    [InlineData("PANO_0001 (12).DNG", "pano_0001.dng")]
    [InlineData("A (x).JPG", "a (x).jpg")]
    public void NormName_lowercases_and_drops_trailing_copy_number(string name, string expected)
        => Assert.Equal(expected, CleanupKeys.NormName(name));

    [Fact]
    public void Key_uses_the_file_name_of_a_card_path()
        => Assert.Equal(new FileKey("dji_20260725232655_0117_d.mp4", 42),
                        CleanupKeys.Key("DCIM/DJI_001/DJI_20260725232655_0117_D.MP4", 42));

    [Theory]
    [InlineData(0L, 0L)]
    [InlineData(1L, 131_072L)]
    [InlineData(131_072L, 131_072L)]
    [InlineData(131_073L, 262_144L)]
    [InlineData(5_000_003_584L, 5_000_003_584L)]
    public void Allocated_rounds_up_to_whole_clusters(long size, long expected)
        => Assert.Equal(expected, CleanupPaths.Allocated(size, 131_072));

    [Fact]
    public void Paths_normalise_separators_and_compose_canonical_card_paths()
    {
        Assert.Equal("DCIM/DJI_001/A.MP4", CleanupPaths.Rel(@"\DCIM\DJI_001\A.MP4"));
        Assert.Equal("A.MP4", CleanupPaths.Name("DCIM/DJI_001/A.MP4"));
        Assert.Equal("DCIM/DJI_001", CleanupPaths.Dir(@"DCIM\DJI_001\A.MP4"));
        Assert.True(CleanupPaths.IsUnder("dcim/dji_001/a.mp4", "DCIM/DJI_001"));
        Assert.False(CleanupPaths.IsUnder("DCIM/DJI_0010/a.mp4", "DCIM/DJI_001"));
        Assert.Equal(@"E:\DCIM\DJI_001\A.MP4", CleanupPaths.Full(@"E:\", "DCIM/DJI_001/A.MP4"));
        Assert.Null(CleanupPaths.Full(@"E:\", "DCIM/../Windows/x.dll"));
        Assert.Null(CleanupPaths.Full(@"E:\", "DCIM//x.MP4"));
    }

    [Fact]
    public void Format_uses_decimal_gb_miles_and_zone_abbreviations()
    {
        Assert.Equal("12.4 GB", CleanupFormat.Gb(12_400_000_000));
        Assert.Equal("0.0 GB", CleanupFormat.Gb(1));
        Assert.Equal("<0.1 mi", CleanupFormat.Miles(Distance.FromMiles(0.05)));
        Assert.Equal("0.2 mi", CleanupFormat.Miles(Distance.FromMiles(0.2)));
        Assert.Equal("34 mi", CleanupFormat.Miles(Distance.FromMiles(33.7)));
        var utc = new DateTime(2026, 7, 26, 8, 10, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2026, 7, 26, 0, 10, 0), CleanupFormat.Local(utc, "America/Anchorage"));
        Assert.Equal("AKDT", CleanupFormat.Abbrev("America/Anchorage", utc));
        Assert.Equal("AKST", CleanupFormat.Abbrev("America/Anchorage", new DateTime(2026, 1, 5, 20, 0, 0, DateTimeKind.Utc)));
        Assert.Equal("HST", CleanupFormat.Abbrev("Pacific/Honolulu", utc));
        Assert.Equal("UTC+5:30", CleanupFormat.Abbrev("Asia/Kolkata", utc));
        Assert.Equal("Jul 26", CleanupFormat.MonthDay(new DateOnly(2026, 7, 26)));
        Assert.Equal("Jul 26, 2026", CleanupFormat.MonthDayYear(new DateOnly(2026, 7, 26)));
        Assert.Equal("Sep 27", CleanupFormat.DayOf(new DateTime(2026, 9, 27, 21, 7, 2, DateTimeKind.Utc), "America/Anchorage"));
    }
}
