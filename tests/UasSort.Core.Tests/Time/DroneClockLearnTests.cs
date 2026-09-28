// tests/UasSort.Core.Tests/Time/DroneClockLearnTests.cs
using static UasSort.Testing.RawItemBuilder;

namespace UasSort.Core.Tests.Time;

public sealed class DroneClockLearnTests
{
    const string NewYork = "America/New_York";
    static readonly ITimeZoneResolver Tz = new GeoTimeZoneResolver();
    static readonly TimeZoneInfo AlaskaPc = Zones.Find("America/Anchorage");
    static readonly GeoPoint Ocean = new(56.0, -148.0);

    static ClockModel Learn(IEnumerable<RawItem> items, StoredClockMode mode = StoredClockMode.Zone, string zone = NewYork,
                            TimeZoneInfo? pc = null, ITimeZoneResolver? tz = null)
        => DroneClock.Learn(items, mode, zone, tz ?? Tz, pc ?? AlaskaPc);

    static List<RawItem> CardZ() =>
    [
        Vid("20260927140127", 123, FixturePoints.Zachar),
        Vid("20260927140144", 124, FixturePoints.Zachar),
        Vid("20260927142416", 148, FixturePoints.Zachar),
    ];

    [Fact]
    public void Learn_SummerMinus4AtAlaskaSites_FitsNewYork()
    {
        var c = Learn(CardZ());
        Assert.Equal(ClockMode.Zone, c.Mode);
        Assert.Equal(NewYork, c.ZoneId);
        Assert.Equal(3, c.Samples.Length);
        Assert.All(c.Samples, s =>
        {
            Assert.Equal(TimeSpan.FromHours(-4), s.Offset);
            Assert.Equal("America/Anchorage", s.SiteZoneId);
        });
        Assert.Equal(TimeSpan.FromHours(-4), c.Modal);
        Assert.Equal(StoredClockMode.Zone, c.SettingMode);
        Assert.Equal(NewYork, c.SettingZoneId);
    }

    [Fact]
    public void Learn_SameSamplesAtEasternSites_SiteLocalWinsByOrder()
    {
        var c = Learn([Vid("20260510104103", 2, FixturePoints.NewportAm), Vid("20260510193745", 30, FixturePoints.NewportPm)]);
        Assert.Equal(ClockMode.SiteLocal, c.Mode);
        Assert.Null(c.ZoneId);
    }

    [Fact]
    public void Learn_WinterMinus5InAlaska_FitsNewYork()
    {
        var minus5 = TimeSpan.FromHours(-5);
        var c = Learn([Vid("20260115120000", 1, FixturePoints.Zachar, minus5), Vid("20260116090000", 2, FixturePoints.Zachar, minus5)]);
        Assert.Equal(ClockMode.Zone, c.Mode);
        Assert.Equal(NewYork, c.ZoneId);
    }

    [Fact]
    public void Learn_FixedMinus4InWinter_NothingFits_NearestSample()
    {
        var c = Learn([Vid("20260115120000", 1, FixturePoints.Zachar), Vid("20260116090000", 2, FixturePoints.Zachar)]);
        Assert.Equal(ClockMode.NearestSample, c.Mode);
        Assert.Null(c.ZoneId);
        Assert.Equal(TimeSpan.FromHours(-4), c.Modal);
    }

    [Fact]
    public void Learn_PhotoOnlyWinterCard_UsesStoredZone()
    {
        var c = Learn([Dng("20260115120000", 1, FixturePoints.Zachar)]);
        Assert.Equal(ClockMode.Setting, c.Mode);
        Assert.Equal(NewYork, c.ZoneId);
        Assert.True(c.Samples.IsEmpty);
        Assert.Null(c.Modal);
        var r = c.ToUtc(Stamp("20260115120000"), null);
        Assert.NotNull(r);
        Assert.Equal(Utc(2026, 1, 15, 17, 0), r.Value.Utc);   // −5 in winter
        Assert.Equal(TimeSource.DroneClockSetting, r.Value.Src);
    }

    [Fact]
    public void Learn_PhotoOnlyCard_StoredSiteLocal_HasNoZone()
    {
        var c = Learn([Dng("20260927110000", 1, FixturePoints.Zachar)], StoredClockMode.SiteLocal);
        Assert.Equal(ClockMode.Setting, c.Mode);
        Assert.Null(c.ZoneId);
        Assert.Equal(StoredClockMode.SiteLocal, c.SettingMode);
    }

    [Fact]
    public void Learn_StoredZoneIsTriedBeforeTheUsList()
    {
        var c = Learn(CardZ(), zone: "America/Puerto_Rico");   // fixed −4 fits the summer samples too
        Assert.Equal(ClockMode.Zone, c.Mode);
        Assert.Equal("America/Puerto_Rico", c.ZoneId);
    }

    [Fact]
    public void Learn_UsListIsTriedInOrder()
    {
        var c = Learn([Vid("20260115120000", 1, FixturePoints.NewportAm, TimeSpan.FromHours(-9))], pc: Zones.Find("Pacific/Honolulu"));
        Assert.Equal(ClockMode.Zone, c.Mode);
        Assert.Equal("America/Anchorage", c.ZoneId);
    }

    [Fact]
    public void Learn_PcZoneIsTheLastCandidate()
    {
        var c = Learn([Vid("20260715120000", 1, FixturePoints.NewportAm, TimeSpan.FromHours(-3))], pc: Zones.Find("America/Sao_Paulo"));
        Assert.Equal(ClockMode.Zone, c.Mode);
        Assert.Equal("America/Sao_Paulo", c.ZoneId);
    }

    [Fact]
    public void Learn_OffsetsRoundToQuarterHours()
    {
        var c = Learn([Vid("20260927140627", 128, FixturePoints.Zachar, mvhdUtc: Utc(2026, 9, 27, 18, 7, 7))]);   // 40 s drift
        Assert.Equal(TimeSpan.FromHours(-4), Assert.Single(c.Samples).Offset);
        Assert.Equal(TimeSpan.FromHours(-4), DroneClock.Round15(new TimeSpan(-4, -7, -29)));
        Assert.Equal(new TimeSpan(-4, -15, 0), DroneClock.Round15(new TimeSpan(-4, -7, -30)));
        Assert.Equal(new TimeSpan(5, 30, 0), DroneClock.Round15(new TimeSpan(5, 29, 58)));
    }

    [Fact]
    public void Learn_OnlyModelTableLandFixesGiveSiteZones()
    {
        var tz = new FakeTimeZoneResolver(Tz).With(Ocean, new TzLookup("Etc/GMT+10", [], true));
        var c = Learn(
        [
            Vid("20260927140127", 1, FixturePoints.Zachar, gpsSource: GpsSource.DjmdGenericSearch, last: FixturePoints.Zachar),
            Vid("20260927150000", 2, Ocean),
            Vid("20260927160000", 3, null),
        ], tz: tz);
        Assert.All(c.Samples, s => Assert.Null(s.SiteZoneId));
        Assert.Equal(ClockMode.Zone, c.Mode);   // SiteLocal needs at least one sample with a site zone
        Assert.Equal(NewYork, c.ZoneId);
    }

    [Fact]
    public void Learn_TruncatedClipsAndPhotosGiveNoSamples()
    {
        var c = Learn([Vid("20260727002013", 14, FixturePoints.Anvil, moov: false), Dng("20260727003000", 2, FixturePoints.Anvil)]);
        Assert.Equal(ClockMode.Setting, c.Mode);
        Assert.True(c.Samples.IsEmpty);
    }

    [Fact]
    public void Learn_SamplesAreSortedByMvhd()
    {
        var c = Learn([Vid("20260927142416", 148, FixturePoints.Zachar), Vid("20260927140127", 123, FixturePoints.Zachar)]);
        DateTime[] expected = [Utc(2026, 9, 27, 18, 1, 27), Utc(2026, 9, 27, 18, 24, 16)];
        Assert.Equal(expected, c.Samples.Select(s => s.MvhdUtc).ToArray());
    }

    [Fact]
    public void Learn_UsZones_AreExactlyTheIsUsZones()
    {
        Assert.Equal(7, DroneClock.UsZones.Length);
        Assert.All(DroneClock.UsZones, id => Assert.True(ZoneNames.IsUs(id)));
    }
}
