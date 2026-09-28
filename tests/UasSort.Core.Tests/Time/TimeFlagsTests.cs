// tests/UasSort.Core.Tests/Time/TimeFlagsTests.cs
using static UasSort.Testing.RawItemBuilder;

namespace UasSort.Core.Tests.Time;

public sealed class TimeFlagsTests
{
    const string NewYork = "America/New_York";
    static readonly ITimeZoneResolver Tz = new GeoTimeZoneResolver();
    static readonly TimeZoneInfo AlaskaPc = Zones.Find("America/Anchorage");
    static readonly GeoPoint Stewart = new(55.94, -129.99);   // border point: GeoTimeZone gives an alternative (Ref research §4)

    static ImmutableArray<ResolvedItem> Resolve(IReadOnlyList<RawItem> raw, DateTime nowUtc, ITimeZoneResolver? tz = null,
                                                StoredClockMode mode = StoredClockMode.Zone, string zone = NewYork)
    {
        var resolver = tz ?? Tz;
        var clock = DroneClock.Learn(raw, mode, zone, resolver, AlaskaPc);
        return TimeResolver.Resolve(raw, clock, resolver, null, AlaskaPc, nowUtc);
    }

    static DateTime NowAt(int y, int mo, int d, int h) => new FakeTimeProvider(new DateTimeOffset(y, mo, d, h, 0, 0, TimeSpan.Zero)).GetUtcNow().UtcDateTime;
    static readonly DateTime Later = NowAt(2027, 6, 1, 0);

    [Fact]
    public void Flags_MismatchPredicate_TrueAt15MinFalseAt14m59s()
    {
        var clock = new TimeSpan(5, 30, 0);
        Assert.True(TimeFlags.IsClockMismatch(clock, new TimeSpan(5, 45, 0)));
        Assert.False(TimeFlags.IsClockMismatch(clock, new TimeSpan(5, 44, 59)));
        Assert.True(TimeFlags.IsClockMismatch(new TimeSpan(5, 45, 0), clock));
        Assert.True(TimeFlags.IsClockMismatch(TimeSpan.FromHours(-4), TimeSpan.FromHours(-8)));
        Assert.False(TimeFlags.IsClockMismatch(TimeSpan.FromHours(-4), TimeSpan.FromHours(-4)));
    }

    [Fact]
    public void Flags_MinutesFromMidnight_IsTheNearerMidnight()
    {
        Assert.Equal(50, TimeFlags.MinutesFromMidnight(new DateTime(2026, 9, 27, 23, 10, 0)));
        Assert.Equal(40, TimeFlags.MinutesFromMidnight(new DateTime(2026, 9, 28, 0, 40, 0)));
        Assert.Equal(720, TimeFlags.MinutesFromMidnight(new DateTime(2026, 9, 28, 12, 0, 0)));
    }

    [Fact]
    public void Flags_CheckDate_ZoneAlternativesWindowIs60Min()
    {
        var tz = new FakeTimeZoneResolver(Tz).With(Stewart, new TzLookup("America/Sitka", ["America/Vancouver"], false));
        var r = Resolve(
        [
            Other("IMG_0001.JPG", Utc(2026, 9, 28, 7, 10), Stewart),               // 23:10 AKDT, 50 min
            Other("IMG_0002.JPG", Utc(2026, 9, 28, 6, 55), Stewart),               // 22:55 AKDT, 65 min
            Other("IMG_0003.JPG", Utc(2026, 9, 28, 7, 10), FixturePoints.Zachar),  // 23:10 AKDT, no alternatives, Mtime
        ], Later, tz);
        Assert.True(r[0].Flags.HasFlag(ItemFlags.CheckDate));
        Assert.False(r[1].Flags.HasFlag(ItemFlags.CheckDate));
        Assert.False(r[2].Flags.HasFlag(ItemFlags.CheckDate));
    }

    [Fact]
    public void Flags_CheckDate_ClockSourceWindowIs75Min()
    {
        var r = Resolve(
        [
            Vid("20260928024500", 1, FixturePoints.Zachar),   // 06:45Z = 22:45 AKDT, 75 min
            Vid("20260928024400", 2, FixturePoints.Zachar),   // 06:44Z = 22:44 AKDT, 76 min
            Vid("20260928044000", 3, FixturePoints.Zachar),   // 08:40Z = 00:40 AKDT, 40 min
        ], Later);
        Assert.True(r[0].Flags.HasFlag(ItemFlags.CheckDate));
        Assert.False(r[1].Flags.HasFlag(ItemFlags.CheckDate));
        Assert.True(r[2].Flags.HasFlag(ItemFlags.CheckDate));
    }

    [Fact]
    public void Flags_ClockNotSet_Pre2015AndMoreThanADayAhead()
    {
        var now = NowAt(2026, 9, 27, 20);
        var r = Resolve(
        [
            Dng("20140601120000", 1, null),   // 2014-06-01T16:00Z
            Dng("20260928150000", 2, null),   // 2026-09-28T19:00Z, within now + 1 day
            Dng("20260928170000", 3, null),   // 2026-09-28T21:00Z, beyond now + 1 day
        ], now);
        Assert.True(r[0].Flags.HasFlag(ItemFlags.ClockNotSet));
        Assert.False(r[1].Flags.HasFlag(ItemFlags.ClockNotSet));
        Assert.True(r[2].Flags.HasFlag(ItemFlags.ClockNotSet));
    }

    [Fact]
    public void Flags_ClockFromSetting_OnlyWhenTheCardHasNoClockSample()
    {
        var photoOnly = Resolve([Dng("20260927150000", 1, FixturePoints.Zachar)], Later);
        Assert.True(photoOnly[0].Flags.HasFlag(ItemFlags.ClockFromSetting));
        var withVideo = Resolve([Vid("20260927140127", 123, FixturePoints.Zachar), Dng("20260927150000", 1, FixturePoints.Zachar)], Later);
        Assert.All(withVideo, i => Assert.False(i.Flags.HasFlag(ItemFlags.ClockFromSetting)));
    }

    [Fact]
    public void Flags_ClockMismatch_WorkedExampleZachar0128()
    {
        var r = Resolve([Vid("20260927140627", 128, new GeoPoint(57.55044, -153.73897))], Later);
        var t = r[0].Time;
        Assert.Equal("America/Anchorage", t.TzId);
        Assert.Equal(Utc(2026, 9, 27, 18, 6, 27), t.CaptureUtc);
        Assert.Equal(new DateTime(2026, 9, 27, 10, 6, 27), t.LocalTime);
        Assert.Equal(new DateOnly(2026, 9, 27), t.LocalDate);
        Assert.True(r[0].Flags.HasFlag(ItemFlags.ClockMismatch));
    }

    [Fact]
    public void Flags_ClockMismatch_NotForPcZoneFallback()
    {
        var r = Resolve([Dng("20260927150000", 1, null)], Later);   // Eastern stored clock, no GPS → PC zone (Alaska)
        Assert.Equal(TzSource.PcZone, r[0].Time.TzSource);
        Assert.True(r[0].Flags.HasFlag(ItemFlags.TzFallback));
        Assert.False(r[0].Flags.HasFlag(ItemFlags.ClockMismatch));
    }

    [Fact]
    public void Flags_ClockMismatch_NotForMtimeItems()
    {
        var r = Resolve([Vid("20260927140127", 123, FixturePoints.Zachar), Other("IMG_0001.JPG", Utc(2026, 9, 27, 19, 0), FixturePoints.Zachar)], Later);
        Assert.True(r[0].Flags.HasFlag(ItemFlags.ClockMismatch));
        Assert.Equal(TimeSource.Mtime, r[1].Time.Source);
        Assert.False(r[1].Flags.HasFlag(ItemFlags.ClockMismatch));
    }

    [Fact]
    public void Flags_TruncatedClip_ExactFlagSet()
    {
        var r = Resolve(
        [
            Vid("20260726235645", 1, FixturePoints.Anvil),
            Vid("20260727002013", 14, FixturePoints.Anvil, moov: false),
            Vid("20260727002118", 15, FixturePoints.Anvil),
        ], Later);
        Assert.Equal(ItemFlags.Truncated | ItemFlags.ClockMismatch, r[1].Flags);
    }
}
