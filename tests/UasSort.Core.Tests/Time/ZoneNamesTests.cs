// tests/UasSort.Core.Tests/Time/ZoneNamesTests.cs
namespace UasSort.Core.Tests.Time;

public sealed class ZoneNamesTests
{
    static DateTime Utc(int y, int mo, int d, int h, int mi) => new(y, mo, d, h, mi, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("America/New_York", "Eastern")]
    [InlineData("America/Chicago", "Central")]
    [InlineData("America/Denver", "Mountain")]
    [InlineData("America/Phoenix", "Arizona")]
    [InlineData("America/Los_Angeles", "Pacific")]
    [InlineData("America/Anchorage", "Alaska")]
    [InlineData("America/Nome", "Alaska")]
    [InlineData("Pacific/Honolulu", "Hawaii")]
    [InlineData("Asia/Kathmandu", "Asia/Kathmandu")]
    public void ZoneNames_Region_UsesShortUsTable(string id, string expected) => Assert.Equal(expected, ZoneNames.Region(id));

    [Theory]
    [InlineData("America/New_York", "US Eastern")]
    [InlineData("America/Los_Angeles", "US Pacific")]
    [InlineData("America/Anchorage", "Alaska")]
    [InlineData("America/Puerto_Rico", "America/Puerto_Rico")]
    public void ZoneNames_ClockName(string id, string expected) => Assert.Equal(expected, ZoneNames.ClockName(id));

    [Fact]
    public void ZoneNames_Abbreviation_FollowsDst()
    {
        Assert.Equal("AKDT", ZoneNames.Abbreviation("America/Anchorage", Utc(2026, 9, 27, 18, 55)));
        Assert.Equal("AKST", ZoneNames.Abbreviation("America/Anchorage", Utc(2026, 12, 1, 12, 0)));
        Assert.Equal("EDT", ZoneNames.Abbreviation("America/New_York", Utc(2026, 11, 1, 5, 20)));
        Assert.Equal("EST", ZoneNames.Abbreviation("America/New_York", Utc(2026, 11, 1, 6, 20)));
        Assert.Equal("MST", ZoneNames.Abbreviation("America/Phoenix", Utc(2026, 7, 1, 12, 0)));
        Assert.Equal("HST", ZoneNames.Abbreviation("Pacific/Honolulu", Utc(2026, 7, 1, 12, 0)));
        Assert.Equal("UTC+5:45", ZoneNames.Abbreviation("Asia/Kathmandu", Utc(2026, 9, 27, 4, 30)));
    }

    [Fact]
    public void ZoneNames_FormatOffset_UsesUnicodeMinusAndMinutesWhenNeeded()
    {
        Assert.Equal("UTC\u22124", ZoneNames.FormatOffset(TimeSpan.FromHours(-4)));
        Assert.Equal("UTC\u22129:30", ZoneNames.FormatOffset(new TimeSpan(-9, -30, 0)));
        Assert.Equal("UTC+5:30", ZoneNames.FormatOffset(new TimeSpan(5, 30, 0)));
        Assert.Equal("UTC", ZoneNames.FormatOffset(TimeSpan.Zero));
    }

    [Fact]
    public void ZoneNames_FormatLocal_ShowsSiteLocalTimeWithAbbreviation()
    {
        Assert.Equal("Sep 27 10:55 AKDT", ZoneNames.FormatLocal(Utc(2026, 9, 27, 18, 55), "America/Anchorage"));
        Assert.Equal("Sep 27 18:55 UTC", ZoneNames.FormatLocal(Utc(2026, 9, 27, 18, 55), null));
        Assert.Equal("Sep 27 18:55 UTC", ZoneNames.FormatLocal(Utc(2026, 9, 27, 18, 55), "Not/AZone"));
    }

    [Fact]
    public void ZoneNames_OffsetAt_FollowsDstAndIsUtcForUnknownIds()
    {
        Assert.Equal(TimeSpan.FromHours(-8), ZoneNames.OffsetAt("America/Anchorage", Utc(2026, 9, 27, 18, 55)));
        Assert.Equal(TimeSpan.FromHours(-9), ZoneNames.OffsetAt("America/Anchorage", Utc(2026, 12, 1, 12, 0)));
        Assert.Equal(TimeSpan.FromHours(-4), ZoneNames.OffsetAt("America/New_York", Utc(2026, 11, 1, 5, 20)));
        Assert.Equal(TimeSpan.FromHours(-5), ZoneNames.OffsetAt("America/New_York", Utc(2026, 11, 1, 6, 20)));
        Assert.Equal(new TimeSpan(5, 45, 0), ZoneNames.OffsetAt("Asia/Kathmandu", Utc(2026, 9, 27, 4, 30)));
        Assert.Equal(TimeSpan.Zero, ZoneNames.OffsetAt("Not/AZone", Utc(2026, 9, 27, 18, 55)));
    }

    [Theory]
    [InlineData("America/New_York", true)]
    [InlineData("America/Chicago", true)]
    [InlineData("America/Denver", true)]
    [InlineData("America/Phoenix", true)]
    [InlineData("America/Los_Angeles", true)]
    [InlineData("America/Anchorage", true)]
    [InlineData("Pacific/Honolulu", true)]
    [InlineData("America/Puerto_Rico", false)]
    [InlineData("Europe/Berlin", false)]
    [InlineData("Asia/Kathmandu", false)]
    public void ZoneNames_IsUs_IsTheSevenUsClockZones(string id, bool expected) => Assert.Equal(expected, ZoneNames.IsUs(id));

    [Fact]
    public void ZoneNames_Zones_TryFind_CachesAndRejectsUnknownIds()
    {
        Assert.True(Zones.TryFind("America/Nome", out var nome));
        Assert.Equal("America/Nome", nome.Id);
        Assert.False(Zones.TryFind("Not/AZone", out _));
        Assert.False(Zones.TryFind(null, out _));
        Assert.Throws<TimeZoneNotFoundException>(() => Zones.Find("Not/AZone"));
    }

    [Fact]
    public void ZoneNames_Zones_IanaId_ConvertsWindowsIds()
    {
        Assert.Equal("America/Anchorage", Zones.IanaId(TimeZoneInfo.FindSystemTimeZoneById("Alaskan Standard Time")));
        Assert.Equal("America/Anchorage", Zones.IanaId(Zones.Find("America/Anchorage")));
    }
}
