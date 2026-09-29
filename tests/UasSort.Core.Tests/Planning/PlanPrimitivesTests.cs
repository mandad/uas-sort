// tests/UasSort.Core.Tests/Planning/PlanPrimitivesTests.cs
using System.Collections.Immutable;
using UasSort.Core.Planning;

namespace UasSort.Core.Tests.Planning;

public sealed class PlanPrimitivesTests
{
    [Theory]
    [InlineData("DJI_20260927140127_0123_D.MP4", "dji_20260927140127_0123_d.mp4")]
    [InlineData("DJI_20260927140127_0123_D (2).MP4", "dji_20260927140127_0123_d.mp4")]
    [InlineData("best shot (12).mp4", "best shot.mp4")]
    [InlineData("PANO_0001.DNG", "pano_0001.dng")]
    [InlineData("name (x).MP4", "name (x).mp4")]
    public void NormName_LowercasesAndDropsTrailingCounter(string name, string expected)
        => Assert.Equal(expected, PlanKeys.NormName(name));

    [Fact]
    public void Key_UsesNormNameAndSize()
        => Assert.Equal(new FileKey("x.mp4", 42), PlanKeys.Key("X (3).MP4", 42));

    [Fact]
    public void DjiStamp_ParsesFilenameStamp()
    {
        Assert.Equal(new DateTime(2026, 9, 27, 14, 6, 27), PlanKeys.DjiStamp("DJI_20260927140627_0128_D.MP4"));
        Assert.Null(PlanKeys.DjiStamp("MAX_0061.MP4"));
        Assert.Equal("DJI_1.MP4", PlanKeys.FileName("DCIM/DJI_001/DJI_1.MP4"));
        Assert.Equal("b.mp4", PlanKeys.FileName(@"C:\a\b.mp4"));
    }

    [Fact]
    public void Haversine_MatchesCalibrationTable()
    {
        var council = new GeoPoint(64.6935, -164.2657);
        var anvil = new GeoPoint(64.5627, -165.3696);
        var zachar = new GeoPoint(57.5368, -153.7484);
        var kodiak = new GeoPoint(57.7996, -152.3902);
        Assert.Equal(33.9, PlanningGeo.Haversine(council, anvil).Miles, 1);
        Assert.Equal(53.4, PlanningGeo.Haversine(zachar, kodiak).Miles, 1);   // the 3.4 mi margin over R = 50
    }

    [Fact]
    public void Centroid_IsMeanUnitVector_AndNullWhenEmpty()
    {
        Assert.Null(PlanningGeo.Centroid([]));
        var c = PlanningGeo.Centroid([new GeoPoint(10, 179.9), new GeoPoint(10, -179.9)])!.Value;
        Assert.Equal(10, c.Lat, 3);
        Assert.Equal(180, Math.Abs(c.Lon), 3);                                  // antimeridian safe
    }

    [Theory]
    [InlineData(0.05, "<0.1 mi")]
    [InlineData(7.84, "7.8 mi")]
    [InlineData(33.7, "34 mi")]
    [InlineData(53.4, "53 mi")]
    public void Miles_FormatsPerUnitsTable(double miles, string expected)
        => Assert.Equal(expected, PlanText.Miles(Distance.FromMiles(miles)));

    [Fact]
    public void Dates_ZonesAndOffsets()
    {
        Assert.Equal("Sep 27", PlanText.ShortDate(new DateOnly(2026, 9, 27)));
        Assert.Equal("Jul 25–26", PlanText.DateRange(new DateOnly(2026, 7, 25), new DateOnly(2026, 7, 26)));
        Assert.Equal("Jul 31–Aug 2", PlanText.DateRange(new DateOnly(2026, 7, 31), new DateOnly(2026, 8, 2)));
        Assert.Equal("Sep 27", PlanText.DateRange(new DateOnly(2026, 9, 27), new DateOnly(2026, 9, 27)));
        Assert.Equal("UTC\u22124", PlanText.Offset(TimeSpan.FromHours(-4)));
        Assert.Equal("UTC+5:30", PlanText.Offset(new TimeSpan(5, 30, 0)));
        var utc = new DateTime(2026, 9, 27, 18, 24, 16, DateTimeKind.Utc);
        Assert.Equal("AKDT", PlanText.ZoneAbbrev("America/Anchorage", utc));
        Assert.Equal("Sep 27 10:24 AKDT", PlanText.LocalTime(utc, "America/Anchorage"));
        Assert.Equal("Alaska", PlanText.ZoneName("America/Anchorage"));
        Assert.Equal("US Eastern", PlanText.ClockZoneName("America/New_York"));
        Assert.Equal("1 conflict", PlanText.Count(1, "conflict", "conflicts"));
        Assert.Equal("2 conflicts", PlanText.Count(2, "conflict", "conflicts"));
    }

    [Fact]
    public void EditRefs_ListEveryReferencedItem()
    {
        var a = new ItemId("a"); var b = new ItemId("b");
        Assert.Equal(new[] { a, b }, PlanEditRefs.Referenced(new Merge(a, b)));
        Assert.Equal(new[] { a, b }, PlanEditRefs.Referenced(new MoveToGroup([a], b)));
        Assert.Equal(new[] { a }, PlanEditRefs.Referenced(new Rename(a, "x", [b])));
        Assert.True(PlanEditRefs.IsStructural(new SplitBefore(a)));
        Assert.False(PlanEditRefs.IsStructural(new SetDayIncluded(new DateOnly(2026, 1, 1), true)));
    }

    [Fact]
    public void IssueCatalogue_HasNothingNew()
        => Assert.True(Enum.IsDefined(IssueCode.NothingNew));
}
