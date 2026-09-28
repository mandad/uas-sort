// tests/UasSort.Core.Tests/Time/ClockModelTests.cs
namespace UasSort.Core.Tests.Time;

public sealed class ClockModelTests
{
    const string NewYork = "America/New_York";
    static DateTime L(int y, int mo, int d, int h, int mi) => new(y, mo, d, h, mi, 0, DateTimeKind.Unspecified);
    static DateTime Utc(int y, int mo, int d, int h, int mi) => new(y, mo, d, h, mi, 0, DateTimeKind.Utc);
    static ClockSample Sample(DateTime stamp, DateTime mvhd, double offsetHours, string? zone = null)
        => new(stamp, mvhd, TimeSpan.FromHours(offsetHours), zone);

    static void AssertUtc(ClockModel m, DateTime stamp, string? site, DateTime expected, TimeSource src)
    {
        var r = m.ToUtc(stamp, site);
        Assert.NotNull(r);
        Assert.Equal(expected, r.Value.Utc);
        Assert.Equal(DateTimeKind.Utc, r.Value.Utc.Kind);
        Assert.Equal(src, r.Value.Src);
    }

    [Fact]
    public void ClockModel_Zone_ConvertsThroughZoneWithDst()
    {
        var m = new ClockModel(ClockMode.Zone, NewYork, [], TimeSpan.FromHours(-4), StoredClockMode.Zone, NewYork);
        AssertUtc(m, L(2026, 9, 27, 14, 6), "America/Anchorage", Utc(2026, 9, 27, 18, 6), TimeSource.DroneClockZone);   // site zone ignored
        AssertUtc(m, L(2026, 1, 15, 12, 0), null, Utc(2026, 1, 15, 17, 0), TimeSource.DroneClockZone);
        Assert.Equal(TimeSpan.FromHours(-4), m.OffsetAt(L(2026, 9, 27, 14, 6), null));
        Assert.Equal(TimeSpan.FromHours(-5), m.OffsetAt(L(2026, 1, 15, 12, 0), null));
    }

    [Fact]
    public void ClockModel_SiteLocal_UsesSiteZoneElseStoredZone()
    {
        var m = new ClockModel(ClockMode.SiteLocal, null, [], TimeSpan.FromHours(-8), StoredClockMode.Zone, "America/Anchorage");
        AssertUtc(m, L(2026, 7, 26, 19, 55), "America/Nome", Utc(2026, 7, 27, 3, 55), TimeSource.DroneClockSiteLocal);
        AssertUtc(m, L(2026, 5, 10, 10, 41), NewYork, Utc(2026, 5, 10, 14, 41), TimeSource.DroneClockSiteLocal);
        // a library member with no ledger tz converts through the stored zone (Ref §6.1, §7.1)
        AssertUtc(m, L(2026, 9, 27, 10, 0), null, Utc(2026, 9, 27, 18, 0), TimeSource.DroneClockSiteLocal);
    }

    [Fact]
    public void ClockModel_NearestSample_UsesNearestByStampThenModal()
    {
        ImmutableArray<ClockSample> samples =
        [
            Sample(L(2026, 9, 27, 14, 0), Utc(2026, 9, 27, 18, 0), -4),
            Sample(L(2026, 9, 27, 11, 0), Utc(2026, 9, 27, 19, 0), -8),
        ];
        var m = new ClockModel(ClockMode.NearestSample, null, samples, TimeSpan.FromHours(-4), StoredClockMode.Zone, NewYork);
        AssertUtc(m, L(2026, 9, 27, 13, 50), null, Utc(2026, 9, 27, 17, 50), TimeSource.DroneClockSample);
        AssertUtc(m, L(2026, 9, 27, 11, 10), null, Utc(2026, 9, 27, 19, 10), TimeSource.DroneClockSample);
        // equidistant (12:30): tie → the sample with the earlier mvhd (−4 at 18:00Z)
        Assert.Equal(TimeSpan.FromHours(-4), m.OffsetAt(L(2026, 9, 27, 12, 30), null));
        // more than 60 days from every sample → the modal offset
        AssertUtc(m, L(2027, 3, 1, 12, 0), null, Utc(2027, 3, 1, 16, 0), TimeSource.DroneClockSample);
    }

    [Fact]
    public void ClockModel_Setting_FollowsStoredMode()
    {
        var siteLocal = new ClockModel(ClockMode.Setting, null, [], null, StoredClockMode.SiteLocal, NewYork);
        AssertUtc(siteLocal, L(2026, 9, 27, 11, 0), "America/Anchorage", Utc(2026, 9, 27, 19, 0), TimeSource.DroneClockSetting);
        AssertUtc(siteLocal, L(2026, 9, 27, 11, 0), null, Utc(2026, 9, 27, 15, 0), TimeSource.DroneClockSetting);
        var zone = new ClockModel(ClockMode.Setting, NewYork, [], null, StoredClockMode.Zone, NewYork);
        AssertUtc(zone, L(2026, 9, 27, 11, 0), "America/Anchorage", Utc(2026, 9, 27, 15, 0), TimeSource.DroneClockSetting);
    }

    [Fact]
    public void ClockModel_Zone_RepeatedDstHourUsesNearestSampleOffset()
    {
        ImmutableArray<ClockSample> samples =
        [
            Sample(L(2026, 11, 1, 1, 20), Utc(2026, 11, 1, 5, 20), -4),   // first pass through 01:00–02:00 (EDT)
            Sample(L(2026, 11, 1, 1, 30), Utc(2026, 11, 1, 6, 30), -5),   // second pass (EST)
        ];
        var m = new ClockModel(ClockMode.Zone, NewYork, samples, TimeSpan.FromHours(-4), StoredClockMode.Zone, NewYork);
        AssertUtc(m, L(2026, 11, 1, 1, 22), null, Utc(2026, 11, 1, 5, 22), TimeSource.DroneClockZone);
        AssertUtc(m, L(2026, 11, 1, 1, 28), null, Utc(2026, 11, 1, 6, 28), TimeSource.DroneClockZone);
        Assert.Equal(TimeSpan.FromHours(-4), m.OffsetAt(L(2026, 11, 1, 1, 22), null));

        var noSamples = m with { Samples = [] };   // no video to choose by → standard time
        AssertUtc(noSamples, L(2026, 11, 1, 1, 22), null, Utc(2026, 11, 1, 6, 22), TimeSource.DroneClockZone);
    }

    [Fact]
    public void ClockModel_Zone_SkippedSpringHourUsesStandardOffset()
    {
        var m = new ClockModel(ClockMode.Zone, NewYork, [], null, StoredClockMode.Zone, NewYork);
        AssertUtc(m, L(2026, 3, 8, 2, 30), null, Utc(2026, 3, 8, 7, 30), TimeSource.DroneClockZone);
    }

    [Fact]
    public void ClockModel_UnknownZone_GivesNull()
    {
        var m = new ClockModel(ClockMode.Zone, "Not/AZone", [], null, StoredClockMode.Zone, "Not/AZone");
        Assert.Null(m.ToUtc(L(2026, 9, 27, 12, 0), null));
    }
}
