// tests/UasSort.Core.Tests/Time/ClockScenarioTests.cs
using static UasSort.Testing.RawItemBuilder;

namespace UasSort.Core.Tests.Time;

/// <summary>The clock-learner, SiteLocal and ClockMismatch cases of Ref §13 "Time", end to end through Learn → Resolve → Summarize.</summary>
public sealed class ClockScenarioTests
{
    const string NewYork = "America/New_York";
    static readonly ITimeZoneResolver Tz = new GeoTimeZoneResolver();
    static readonly TimeZoneInfo AlaskaPc = Zones.Find("America/Anchorage");
    static readonly DateTime Now = new FakeTimeProvider(new DateTimeOffset(2027, 6, 1, 0, 0, 0, TimeSpan.Zero)).GetUtcNow().UtcDateTime;
    static readonly TimeSpan Alaska = TimeSpan.FromHours(-8);

    static (ClockModel Clock, ImmutableArray<ResolvedItem> Items, ClockSummary Summary) Run(
        IReadOnlyList<RawItem> raw, StoredClockMode mode = StoredClockMode.Zone, string zone = NewYork)
    {
        var clock = DroneClock.Learn(raw, mode, zone, Tz, AlaskaPc);
        var items = TimeResolver.Resolve(raw, clock, Tz, null, AlaskaPc, Now);
        return (clock, items, DroneClock.Summarize(clock, items));
    }

    static Settings Stored(StoredClockMode mode, string zone) => new(
        1, @"C:\Videos", @"C:\Videos\Picture Offload", [], 50, 1, mode, zone, true,
        new MapSettings("streets", "https://tiles.openfreemap.org/styles/liberty", "https://tiles.openfreemap.org/styles/dark",
                        "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}",
                        ImmutableDictionary<string, string>.Empty),
        new LayoutSettings(380, 0.45), true);

    static bool Mismatch(ResolvedItem i) => i.Flags.HasFlag(ItemFlags.ClockMismatch);

    [Fact]
    public void Clock_Scenario_SiteLocalFit_AlaskaClock()
    {
        var (clock, items, summary) = Run(
        [
            Vid("20260927100127", 123, FixturePoints.Zachar, Alaska),
            Vid("20260927100144", 124, FixturePoints.Zachar, Alaska),
            Dng("20260927101000", 1, FixturePoints.Zachar),
        ]);
        Assert.Equal(ClockMode.SiteLocal, clock.Mode);
        Assert.Equal(TimeSource.DroneClockSiteLocal, items[2].Time.Source);
        Assert.Equal(Utc(2026, 9, 27, 18, 10), items[2].Time.CaptureUtc);
        Assert.DoesNotContain(items, Mismatch);
        Assert.Equal("Drone clock: follows local time at each site, learned from 2 videos.", summary.Headline);
        Assert.Equal(0, summary.MismatchItems);

        var saved = DroneClock.ApplyLearned(Stored(StoredClockMode.Zone, NewYork), clock);
        Assert.Equal(StoredClockMode.SiteLocal, saved.DroneClockMode);
        Assert.Equal(NewYork, saved.DroneClockZone);   // left as it was
    }

    [Fact]
    public void Clock_Scenario_EasternClockInAlaska_MismatchOnEveryItem()
    {
        var (clock, items, summary) = Run(
        [
            Vid("20260927140127", 123, FixturePoints.Zachar),
            Vid("20260927140144", 124, FixturePoints.Zachar),
            Vid("20260927142416", 148, FixturePoints.Zachar),
            Dng("20260927142000", 1, FixturePoints.Zachar),
        ]);
        Assert.Equal(ClockMode.Zone, clock.Mode);
        Assert.Equal(NewYork, clock.ZoneId);
        Assert.All(items, i => Assert.True(Mismatch(i)));
        Assert.Equal(4, summary.MismatchItems);
        string[] expectedZones = ["America/Anchorage"];
        Assert.Equal(expectedZones, summary.MismatchSiteZones.ToArray());
        Assert.True(summary.Changes.IsEmpty);
        Assert.Equal(3, summary.SampleCount);
        Assert.Equal("Drone clock: US Eastern (America/New_York), learned from 3 videos. Folder dates use local time at each site."
                     + " It doesn't match local time where this card was shot (Alaska).", summary.Headline);

        var saved = DroneClock.ApplyLearned(Stored(StoredClockMode.SiteLocal, "America/Chicago"), clock);
        Assert.Equal(StoredClockMode.Zone, saved.DroneClockMode);
        Assert.Equal(NewYork, saved.DroneClockZone);
    }

    [Fact]
    public void Clock_Scenario_EasternClockInNewport_SiteLocalNoMismatch()
    {
        var (clock, items, summary) = Run([Vid("20260510104103", 2, FixturePoints.NewportAm), Vid("20260510193745", 30, FixturePoints.NewportPm)]);
        Assert.Equal(ClockMode.SiteLocal, clock.Mode);
        Assert.DoesNotContain(items, Mismatch);
        Assert.Equal(0, summary.MismatchItems);
        Assert.DoesNotContain("doesn't match", summary.Headline, StringComparison.Ordinal);
    }

    [Fact]
    public void Clock_Scenario_TripAcrossZones_SiteLocalClock()
    {
        var (clock, items, _) = Run(
        [
            Vid("20260510104103", 1, FixturePoints.NewportAm),
            Vid("20260726195000", 2, FixturePoints.Anvil, Alaska),
            Dng("20260726195500", 3, null),
        ]);
        Assert.Equal(ClockMode.SiteLocal, clock.Mode);
        var dng = items[2];
        Assert.Equal(TimeSource.DroneClockSiteLocal, dng.Time.Source);
        Assert.Equal(Utc(2026, 7, 27, 3, 55), dng.Time.CaptureUtc);
        Assert.Equal("America/Nome", dng.Time.TzId);
        Assert.Equal(TzSource.NearestGpsWithin12h, dng.Time.TzSource);
        Assert.DoesNotContain(items, Mismatch);
    }

    [Theory]
    [InlineData(StoredClockMode.SiteLocal, 19, false)]
    [InlineData(StoredClockMode.Zone, 15, true)]
    public void Clock_Scenario_PhotoOnlyCard_UsesStoredMode(StoredClockMode mode, int utcHour, bool mismatch)
    {
        var (clock, items, summary) = Run([Dng("20260927110000", 1, FixturePoints.Zachar)], mode);
        Assert.Equal(ClockMode.Setting, clock.Mode);
        var i = Assert.Single(items);
        Assert.Equal(TimeSource.DroneClockSetting, i.Time.Source);
        Assert.True(i.Flags.HasFlag(ItemFlags.ClockFromSetting));
        Assert.Equal(Utc(2026, 9, 27, utcHour, 0), i.Time.CaptureUtc);
        Assert.Equal("America/Anchorage", i.Time.TzId);
        Assert.Equal(mismatch, Mismatch(i));
        Assert.StartsWith("Drone clock: no videos on this card: using the last learned clock", summary.Headline, StringComparison.Ordinal);
        var stored = Stored(mode, NewYork);
        Assert.Same(stored, DroneClock.ApplyLearned(stored, clock));   // Setting saves nothing
    }

    [Fact]
    public void Clock_Scenario_NoMp4_UsesStoredZone()
    {
        var (clock, items, _) = Run([Dng("20260927150000", 1, FixturePoints.Zachar)]);
        Assert.Equal(ClockMode.Setting, clock.Mode);
        var i = Assert.Single(items);
        Assert.Equal(TimeSource.DroneClockSetting, i.Time.Source);
        Assert.Equal(Utc(2026, 9, 27, 19, 0), i.Time.CaptureUtc);
        Assert.Equal(ItemFlags.ClockFromSetting | ItemFlags.ClockMismatch, i.Flags);
    }

    [Fact]
    public void Clock_Scenario_ClockCorrectedMidCard_NearestSampleWithOneChange()
    {
        List<RawItem> raw =
        [
            Vid("20260927140000", 1, FixturePoints.Zachar), Vid("20260927141000", 2, FixturePoints.Zachar),
            Vid("20260927142000", 3, FixturePoints.Zachar), Vid("20260927143000", 4, FixturePoints.Zachar),
            Vid("20260927144000", 5, FixturePoints.Zachar), Vid("20260927145000", 6, FixturePoints.Zachar),   // Eastern clock, 18:00–18:50Z
            Vid("20260927110000", 7, FixturePoints.Zachar, Alaska), Vid("20260927111000", 8, FixturePoints.Zachar, Alaska),
            Vid("20260927112000", 9, FixturePoints.Zachar, Alaska), Vid("20260927113000", 10, FixturePoints.Zachar, Alaska),
            Vid("20260927114000", 11, FixturePoints.Zachar, Alaska),                                           // Alaska clock, 19:00–19:40Z
            Dng("20260927135500", 90, FixturePoints.Zachar),   // before: nearest #1 (−4) → 17:55Z
            Dng("20260927145300", 91, FixturePoints.Zachar),   // between, old clock: nearest #6 (−4) → 18:53Z
            Dng("20260927105800", 92, FixturePoints.Zachar),   // between, new clock: nearest #7 (−8) → 18:58Z
            Dng("20260927114500", 93, FixturePoints.Zachar),   // after: nearest #11 (−8) → 19:45Z
        ];
        var (clock, items, summary) = Run(raw);
        Assert.Equal(ClockMode.NearestSample, clock.Mode);
        Assert.Equal(TimeSpan.FromHours(-4), clock.Modal);

        var change = Assert.Single(summary.Changes);
        Assert.Equal(Utc(2026, 9, 27, 18, 55), change.AtUtc);
        Assert.Equal(TimeSpan.FromHours(-4), change.From);
        Assert.Equal(Alaska, change.To);

        DateTime[] dngUtc = [Utc(2026, 9, 27, 17, 55), Utc(2026, 9, 27, 18, 53), Utc(2026, 9, 27, 18, 58), Utc(2026, 9, 27, 19, 45)];
        Assert.Equal(dngUtc, items.Skip(11).Select(i => i.Time.CaptureUtc).ToArray());
        Assert.All(items.Skip(11), i => Assert.Equal(TimeSource.DroneClockSample, i.Time.Source));
        Assert.All(items, i => Assert.Equal(i.Time.CaptureUtc < change.AtUtc, Mismatch(i)));   // only items before the change

        Assert.Equal("Drone clock: no single time zone fits these videos, so each item uses the offset of the nearest video in time."
                     + " Drone clock changed during this card: UTC\u22124 until Sep 27 10:55 AKDT, then UTC\u22128."
                     + " It doesn't match local time where this card was shot (Alaska).", summary.Headline);
        var stored = Stored(StoredClockMode.Zone, NewYork);
        Assert.Same(stored, DroneClock.ApplyLearned(stored, clock));   // NearestSample saves nothing
    }

    [Fact]
    public void Clock_Scenario_KolkataClockInKathmandu_Exactly15MinMismatch()
    {
        var kolkataClock = new TimeSpan(5, 30, 0);
        var (clock, items, summary) = Run(
        [
            Vid("20260927100000", 1, FixturePoints.Kathmandu, kolkataClock),
            Vid("20260927110000", 2, FixturePoints.Kathmandu, kolkataClock),
        ]);
        Assert.Equal(ClockMode.NearestSample, clock.Mode);
        Assert.Equal(kolkataClock, clock.Modal);
        Assert.All(items, i =>
        {
            Assert.Equal("Asia/Kathmandu", i.Time.TzId);
            Assert.True(Mismatch(i));
        });
        Assert.EndsWith(" It doesn't match local time where this card was shot (Asia/Kathmandu).", summary.Headline, StringComparison.Ordinal);
    }

    [Fact]
    public void Clock_Scenario_KolkataClockInKolkata_NoMismatch()
    {
        var kolkataClock = new TimeSpan(5, 30, 0);
        var (clock, items, _) = Run(
        [
            Vid("20260927100000", 1, FixturePoints.Kolkata, kolkataClock),
            Vid("20260927110000", 2, FixturePoints.Kolkata, kolkataClock),
        ]);
        Assert.Equal(ClockMode.SiteLocal, clock.Mode);
        Assert.DoesNotContain(items, Mismatch);
    }

    [Fact] // [Review Focus] 4: DST end 2026-11-01 with a zone-fitted America/New_York clock
    public void Clock_Scenario_DstEnd_RepeatedHourUsesNearestSample_LocalDatesAndCheckDateWindows()
    {
        var minus5 = TimeSpan.FromHours(-5);
        List<RawItem> raw =
        [
            Vid("20261031140000", 1, FixturePoints.Zachar),           // 18:00Z = Oct 31 10:00 AKDT
            Vid("20261101012000", 2, FixturePoints.Zachar),           // first 01:20 (EDT) = 05:20Z = Oct 31 21:20 AKDT
            Vid("20261101013000", 3, FixturePoints.Zachar, minus5),   // second 01:30 (EST) = 06:30Z = Oct 31 22:30 AKDT
            Vid("20261101040000", 4, FixturePoints.Zachar, minus5),   // 09:00Z = Nov 1 01:00 AKDT (60 min)
            Dng("20261101012200", 11, FixturePoints.Zachar),          // repeated hour, nearest #2 (−4) → 05:22Z
            Dng("20261101012800", 12, FixturePoints.Zachar),          // repeated hour, nearest #3 (−5) → 06:28Z
            Dng("20261101041500", 13, FixturePoints.Zachar),          // 09:15Z = Nov 1 01:15 AKDT (75 min)
            Dng("20261101041600", 14, FixturePoints.Zachar),          // 09:16Z = Nov 1 01:16 AKDT (76 min)
            Dng("20261101043000", 15, FixturePoints.Zachar),          // 09:30Z = Nov 1 01:30 AKDT (90 min)
        ];
        var (clock, items, summary) = Run(raw);

        Assert.Equal(ClockMode.Zone, clock.Mode);
        Assert.Equal(NewYork, clock.ZoneId);

        DateTime[] expectedUtc =
        [
            Utc(2026, 10, 31, 18, 0), Utc(2026, 11, 1, 5, 20), Utc(2026, 11, 1, 6, 30), Utc(2026, 11, 1, 9, 0),
            Utc(2026, 11, 1, 5, 22), Utc(2026, 11, 1, 6, 28), Utc(2026, 11, 1, 9, 15), Utc(2026, 11, 1, 9, 16), Utc(2026, 11, 1, 9, 30),
        ];
        Assert.Equal(expectedUtc, items.Select(i => i.Time.CaptureUtc).ToArray());
        Assert.All(items.Skip(4), i => Assert.Equal(TimeSource.DroneClockZone, i.Time.Source));

        DateOnly oct31 = new(2026, 10, 31), nov1 = new(2026, 11, 1);
        DateOnly[] expectedDates = [oct31, oct31, oct31, nov1, oct31, oct31, nov1, nov1, nov1];
        Assert.Equal(expectedDates, items.Select(i => i.Time.LocalDate).ToArray());   // the drone's Nov 1 01:xx stamps are Oct 31 locally

        bool[] expectedCheckDate = [false, false, false, true, false, false, true, false, false];
        Assert.Equal(expectedCheckDate, items.Select(i => i.Flags.HasFlag(ItemFlags.CheckDate)).ToArray());

        Assert.Equal("Drone clock: US Eastern (America/New_York), learned from 4 videos. Folder dates use local time at each site."
                     + " It doesn't match local time where this card was shot (Alaska).", summary.Headline);
    }
}
