// tests/UasSort.Core.Tests/Time/TimeResolverTests.cs
using static UasSort.Testing.RawItemBuilder;

namespace UasSort.Core.Tests.Time;

public sealed class TimeResolverTests
{
    const string NewYork = "America/New_York";
    static readonly ITimeZoneResolver Tz = new GeoTimeZoneResolver();
    static readonly TimeZoneInfo AlaskaPc = Zones.Find("America/Anchorage");
    static readonly DateTime Now = new FakeTimeProvider(new DateTimeOffset(2027, 6, 1, 0, 0, 0, TimeSpan.Zero)).GetUtcNow().UtcDateTime;
    static readonly GeoPoint Ocean = new(56.0, -148.0);
    static readonly FakeTimeZoneResolver OceanTz = new FakeTimeZoneResolver(Tz).With(Ocean, new TzLookup("Etc/GMT+10", [], true));

    static ImmutableArray<ResolvedItem> Resolve(IReadOnlyList<RawItem> raw, ITimeZoneResolver? tz = null, IPlaceIndex? places = null,
                                                StoredClockMode mode = StoredClockMode.Zone, string zone = NewYork)
    {
        var resolver = tz ?? Tz;
        var clock = DroneClock.Learn(raw, mode, zone, resolver, AlaskaPc);
        return TimeResolver.Resolve(raw, clock, resolver, places, AlaskaPc, Now);
    }

    [Fact]
    public void Resolve_Midnight_LocalDateFromSiteZone()
    {
        var r = Resolve([Vid("20260726035000", 1, FixturePoints.Anvil), Vid("20260726041000", 2, FixturePoints.Anvil)]);
        var t = r[0].Time;
        Assert.Equal(Utc(2026, 7, 26, 7, 50), t.CaptureUtc);
        Assert.Equal(TimeSource.Mvhd, t.Source);
        Assert.Equal("America/Nome", t.TzId);
        Assert.Equal(TzSource.Gps, t.TzSource);
        Assert.Equal(new DateOnly(2026, 7, 25), t.LocalDate);                 // never the drone clock's Jul 26
        Assert.Equal(new DateTime(2026, 7, 25, 23, 50, 0), t.LocalTime);
        Assert.False(r[0].Flags.HasFlag(ItemFlags.TzFallback));
        Assert.False(r[0].Flags.HasFlag(ItemFlags.NoGps));
    }

    [Fact]
    public void Resolve_Hawaii_SiteZoneNotPcZone()
    {
        var t = Resolve([Vid("20260301053000", 1, FixturePoints.Makaha)])[0].Time;
        Assert.Equal("Pacific/Honolulu", t.TzId);
        Assert.Equal(new DateOnly(2026, 2, 28), t.LocalDate);
    }

    [Fact]
    public void Resolve_Dng_UsesClockZoneAndSiteZone()
    {
        var t = Resolve([Vid("20260726035000", 1, FixturePoints.Anvil), Dng("20260726035500", 2, FixturePoints.Anvil)])[1].Time;
        Assert.Equal(TimeSource.DroneClockZone, t.Source);
        Assert.Equal(Utc(2026, 7, 26, 7, 55), t.CaptureUtc);
        Assert.Equal(new DateOnly(2026, 7, 25), t.LocalDate);
    }

    [Fact]
    public void Resolve_ZoneClock_HandlesDstChangeOnTheCard()
    {
        var r = Resolve(
        [
            Vid("20261020120000", 1, FixturePoints.Anvil),
            Vid("20261110120000", 2, FixturePoints.Anvil, TimeSpan.FromHours(-5)),
            Dng("20261110121000", 3, FixturePoints.Anvil),
        ]);
        Assert.Equal(TimeSource.DroneClockZone, r[2].Time.Source);
        Assert.Equal(Utc(2026, 11, 10, 17, 10), r[2].Time.CaptureUtc);
    }

    [Fact]
    public void Resolve_TruncatedClip_TimedByClockWithFallbackGps()
    {
        var r = Resolve(
        [
            Vid("20260726235645", 1, FixturePoints.Anvil),
            Vid("20260727002013", 14, FixturePoints.Anvil, moov: false),
            Vid("20260727002118", 15, FixturePoints.Anvil),
        ]);
        var t = r[1];
        Assert.Equal(TimeSource.DroneClockZone, t.Time.Source);
        Assert.Equal(Utc(2026, 7, 27, 4, 20, 13), t.Time.CaptureUtc);
        Assert.True(t.Flags.HasFlag(ItemFlags.Truncated));
        Assert.False(t.Flags.HasFlag(ItemFlags.NoGps));
        Assert.Equal(GpsSource.MdatHeadFallback, t.Gps?.Source);
    }

    [Fact]
    public void Resolve_ExifOffset_BeatsDroneClock()
    {
        var t = Resolve([Dng("20260927120000", 1, FixturePoints.Zachar, TimeSpan.FromHours(-8))])[0].Time;
        Assert.Equal(TimeSource.ExifWithOffset, t.Source);
        Assert.Equal(Utc(2026, 9, 27, 20, 0), t.CaptureUtc);
    }

    [Fact]
    public void Resolve_NoDroneStamp_UsesMtime()
    {
        var t = Resolve([Other("IMG_0001.JPG", Utc(2026, 9, 27, 19, 0), FixturePoints.Zachar)])[0].Time;
        Assert.Equal(TimeSource.Mtime, t.Source);
        Assert.Equal(Utc(2026, 9, 27, 19, 0), t.CaptureUtc);
    }

    [Fact]
    public void Resolve_ProbeFailedClip_TimedFromFilename()
    {
        var broken = Vid("20260927141000", 125, FixturePoints.Zachar) with { Mp4 = null, ProbeError = "moov unreadable" };
        var r = Resolve([Vid("20260927140127", 123, FixturePoints.Zachar), broken]);
        Assert.Equal(TimeSource.DroneClockZone, r[1].Time.Source);
        Assert.Equal(Utc(2026, 9, 27, 18, 10), r[1].Time.CaptureUtc);
        Assert.True(r[1].Flags.HasFlag(ItemFlags.ProbeFailed));
        Assert.True(r[1].Flags.HasFlag(ItemFlags.NoGps));
        Assert.False(r[1].Flags.HasFlag(ItemFlags.Truncated));
    }

    [Fact]
    public void Resolve_EtcZone_UsesNearestLandItemOnCard()
    {
        var r = Resolve([Vid("20260927120000", 1, Ocean), Vid("20260927150000", 2, FixturePoints.Zachar)], OceanTz);
        Assert.Equal("America/Anchorage", r[0].Time.TzId);
        Assert.Equal(TzSource.NearestLandGpsOnCard, r[0].Time.TzSource);
        Assert.True(r[0].Flags.HasFlag(ItemFlags.TzFallback));
        Assert.Equal(TzSource.Gps, r[1].Time.TzSource);
        Assert.False(r[1].Flags.HasFlag(ItemFlags.TzFallback));
    }

    [Fact]
    public void Resolve_EtcZone_NoLandWithin12h_UsesNearestGeoNamesPlace()
    {
        var places = new FakePlaceIndex(
        [
            new PlaceHit("Test Island", new GeoPoint(56.3, -148.2), PlaceClass.Populated, "P", 50, "America/Juneau", new Distance(0)),
        ]);
        var r = Resolve([Vid("20260927120000", 1, Ocean), Vid("20260928010000", 2, FixturePoints.Zachar)], OceanTz, places);   // 13 h apart
        Assert.Equal("America/Juneau", r[0].Time.TzId);
        Assert.Equal(TzSource.GeoNamesTz, r[0].Time.TzSource);
        Assert.True(r[0].Flags.HasFlag(ItemFlags.TzFallback));
    }

    [Fact]
    public void Resolve_EtcZone_NothingNearby_UsesPcZone()
    {
        var places = new FakePlaceIndex(
        [
            new PlaceHit("Kodiak", FixturePoints.KodiakTown, PlaceClass.Populated, "P", 5581, "America/Juneau", new Distance(0)),   // ~200 mi
        ]);
        var r = Resolve([Vid("20260927120000", 1, Ocean), Vid("20260928010000", 2, FixturePoints.Zachar)], OceanTz, places);
        Assert.Equal("America/Anchorage", r[0].Time.TzId);
        Assert.Equal(TzSource.PcZone, r[0].Time.TzSource);
        Assert.True(r[0].Flags.HasFlag(ItemFlags.TzFallback));
    }

    [Fact]
    public void Resolve_NoGps_SameSessionBeatsNearerItem_ThenNearestWithin12h()
    {
        var sessionA = new SessionKey(Serial, Utc(2026, 9, 27, 17, 55, 0));
        var sessionX = new SessionKey(Serial, Utc(2026, 9, 27, 17, 55, 1));   // same power-on (|Δ| ≤ 2 s)
        var r = Resolve(
        [
            Vid("20260927140000", 1, FixturePoints.Zachar, session: sessionA),   // 18:00Z, Alaska
            Vid("20260927200000", 2, FixturePoints.NewportAm),                  // 00:00Z Sep 28, Eastern
            Vid("20260927190000", 3, null, session: sessionX),                  // 23:00Z, no GPS
            Dng("20260927195000", 4, null),                                     // 23:50Z, no GPS, no session
        ]);
        Assert.Equal("America/Anchorage", r[2].Time.TzId);
        Assert.Equal(TzSource.SameSession, r[2].Time.TzSource);
        Assert.Equal(sessionX, r[2].Session);
        Assert.False(r[2].Flags.HasFlag(ItemFlags.TzFallback));
        Assert.True(r[2].Flags.HasFlag(ItemFlags.NoGps));
        Assert.Equal(NewYork, r[3].Time.TzId);
        Assert.Equal(TzSource.NearestGpsWithin12h, r[3].Time.TzSource);
        Assert.False(r[3].Flags.HasFlag(ItemFlags.TzFallback));
    }

    [Fact]
    public void Resolve_NoGps_NothingWithin12h_UsesPcZone()
    {
        var r = Resolve([Vid("20260927140000", 1, FixturePoints.Zachar), Dng("20260928090000", 2, null)]);   // 19 h apart
        Assert.Equal("America/Anchorage", r[1].Time.TzId);
        Assert.Equal(TzSource.PcZone, r[1].Time.TzSource);
        Assert.True(r[1].Flags.HasFlag(ItemFlags.TzFallback));
    }

    [Fact]
    public void Resolve_GenericHit_PlausibleIsGuessed()
    {
        var r = Resolve(
        [
            Vid("20260927140127", 123, FixturePoints.Zachar),
            Vid("20260927141000", 125, new GeoPoint(57.54, -153.74), gpsSource: GpsSource.DjmdGenericSearch,
                last: new GeoPoint(57.545, -153.742)),
        ]);
        Assert.NotNull(r[1].Gps);
        Assert.True(r[1].Flags.HasFlag(ItemFlags.GpsGuessed));
        Assert.False(r[1].Flags.HasFlag(ItemFlags.NoGps));
        Assert.Equal(TzSource.Gps, r[1].Time.TzSource);
    }

    [Fact]
    public void Resolve_GenericHit_ImplausibleIsNoGps()
    {
        var r = Resolve(
        [
            Vid("20260927140127", 123, FixturePoints.Zachar),
            Vid("20260927141000", 125, new GeoPoint(57.54, -153.74), gpsSource: GpsSource.DjmdGenericSearch,
                last: FixturePoints.KodiakTown),   // first↔last ≈ 53 mi
        ]);
        Assert.Null(r[1].Gps);
        Assert.True(r[1].Flags.HasFlag(ItemFlags.NoGps));
        Assert.False(r[1].Flags.HasFlag(ItemFlags.GpsGuessed));
    }

    [Fact]
    public void Resolve_PreservesInputOrder()
    {
        List<RawItem> raw = [Dng("20260927150000", 9, FixturePoints.Zachar), Vid("20260927140127", 123, FixturePoints.Zachar)];
        var r = Resolve(raw);
        Assert.Equal(raw, r.Select(x => x.Raw).ToList());
    }
}
