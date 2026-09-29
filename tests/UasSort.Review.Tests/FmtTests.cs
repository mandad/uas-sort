// tests/UasSort.Review.Tests/FmtTests.cs
namespace UasSort.Review.Tests;

public class FmtTests
{
    [Theory]
    [InlineData(0.05, "<0.1 mi")]
    [InlineData(0.3, "0.3 mi")]
    [InlineData(7.8, "7.8 mi")]
    [InlineData(33.7, "34 mi")]
    [InlineData(50, "50 mi")]
    public void Fmt_Miles_FollowsTheUnitsTable(double miles, string expected)
        => Assert.Equal(expected, Fmt.Miles(Distance.FromMiles(miles)));

    [Theory]
    [InlineData(31_400_000_000L, "31.4 GB")]
    [InlineData(317_000_000_000L, "317 GB")]
    [InlineData(256_060_514_304L, "256.1 GB")]
    [InlineData(400_000_000L, "0.4 GB")]
    [InlineData(7_000_000L, "7 MB")]
    [InlineData(12_700L, "13 KB")]
    [InlineData(512L, "512 B")]
    public void Fmt_Size_IsDecimal(long bytes, string expected) => Assert.Equal(expected, Fmt.Size(bytes));

    [Fact]
    public void Fmt_ClipLength_MinutesOrHours()
    {
        Assert.Equal("3:42", Fmt.ClipLength(TimeSpan.FromSeconds(222)));
        Assert.Equal("1:02:05", Fmt.ClipLength(new TimeSpan(1, 2, 5)));
        Assert.Equal("0:07", Fmt.ClipLength(TimeSpan.FromSeconds(7.9)));
    }

    [Fact]
    public void Fmt_DatesAndRanges()
    {
        Assert.Equal("Jul 25", Fmt.DateRange(new(2026, 7, 25), new(2026, 7, 25)));
        Assert.Equal("Jul 25–26", Fmt.DateRange(new(2026, 7, 25), new(2026, 7, 26)));
        Assert.Equal("Jul 31 – Aug 2", Fmt.DateRange(new(2026, 7, 31), new(2026, 8, 2)));
        Assert.Equal("Dec 31, 2025 – Jan 1, 2026", Fmt.DateRange(new(2025, 12, 31), new(2026, 1, 1)));
        Assert.Equal("Jul 25 (Sat)", Fmt.DayWithWeekday(new(2026, 7, 25)));
        Assert.Equal("Jul 26, 2026", Fmt.DayYear(new(2026, 7, 26)));
    }

    [Fact]
    public void Fmt_GapsOffsetsAndCard()
    {
        Assert.Equal("21 h", Fmt.Gap(TimeSpan.FromHours(21.2)));
        Assert.Equal("62 days", Fmt.Gap(TimeSpan.FromHours(1488.5)));
        Assert.Equal("40 min", Fmt.Gap(TimeSpan.FromMinutes(40)));
        Assert.Equal("UTC−4", Fmt.Offset(TimeSpan.FromHours(-4)));
        Assert.Equal("UTC+5:30", Fmt.Offset(new TimeSpan(5, 30, 0)));
        Assert.Equal("UTC+0", Fmt.Offset(TimeSpan.Zero));
        Assert.Equal("1A2B-3C4D", Fmt.Serial(0x1A2B3C4D));
        Assert.Equal("E:\\ · DJI Air 3S · serial 1A2B-3C4D · 214 files · 61.3 GB",
                     Fmt.CardChip(@"E:\", TestPlans.Card, "FC9113", 214, 61_300_000_000));
        Assert.Equal("C:", Fmt.Drive(@"C:\Lib\UAS Videos"));
        Assert.Equal("1 conflict", Fmt.Count(1, "conflict", "conflicts"));
        Assert.Equal("3 conflicts", Fmt.Count(3, "conflict", "conflicts"));
    }

    [Fact]
    public void Fmt_Miles_OfTheCoreHaversine_CouncilToAnvilIs34Miles()
    {
        var d = GeoMath.Haversine(new GeoPoint(64.6935, -164.2657), new GeoPoint(64.5627, -165.3696));
        Assert.InRange(d.Miles, 33.5, 34.2);
        Assert.Equal("34 mi", Fmt.Miles(d));
    }
}
