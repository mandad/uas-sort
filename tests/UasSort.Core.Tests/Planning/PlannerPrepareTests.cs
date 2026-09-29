// tests/UasSort.Core.Tests/Planning/PlannerPrepareTests.cs
using UasSort.Testing.Planning;

namespace UasSort.Core.Tests.Planning;

public sealed class PlannerPrepareTests
{
    private static readonly RawItem[] Z =
    [
        Clip.Vid("20260927140127", 123, Sites.Zachar), Clip.Vid("20260927140144", 124, Sites.Zachar), Clip.Vid("20260927142416", 148, Sites.Zachar),
    ];

    [Fact]
    public void Prepare_BuildsItemsNewnessPhotoDaysAndSets()
    {
        var dng = Clip.Dng("20260927141000", 125, Sites.Zachar);
        var pano = Clip.Set("001_0087", "2026-05-25 09:30:28", Sites.KodiakTown,
            ("PANO_0001.DNG", 13_751_808, new DateTime(2026, 5, 25, 13, 30, 28, DateTimeKind.Utc)));
        var b = new PlanScenario().Card([.. Z, dng, pano]).Prepare();

        Assert.Equal(5, b.Items.Length);
        Assert.Equal(pano.Id(), b.Items[0].Raw.Unit.Id);                              // oldest first
        Assert.All(b.Items.Where(i => i.Raw.Kind == ItemKind.Video), i => Assert.Equal(new IsNew(NewReason.NoMatch, null), i.Newness));
        Assert.Null(b.WatermarkUtc);                                                  // no library videos
        Assert.Equal(new IsNew(NewReason.AfterWatermark, null), b.Items.Single(i => i.Raw.Unit.Id == dng.Id()).Newness);
        Assert.Equal(new[] { new DateOnly(2026, 5, 25), new DateOnly(2026, 9, 27) }, b.PhotoDays.Select(d => d.Date));
        Assert.Equal(SetResolution.Plain, b.Sets[pano.Id()].Resolution);
    }

    [Fact]
    public void Prepare_SetsTruncatedAndProbeFailedFlags()
    {
        var t = Clip.Vid("20260727002013", 14, Sites.Anvil, moov: false);
        var b = new PlanScenario().Card(Clip.Vid("20260726235645", 1, Sites.Anvil), t).Prepare();
        Assert.True(b.Items.Single(i => i.Raw.Unit.Id == t.Id()).Flags.HasFlag(ItemFlags.Truncated));
    }

    [Fact] // Eastern clock in Alaska → Zone America/New_York, ClockMismatch on every item; the summary is Part 04's DroneClock.Summarize
    public void Prepare_ClockSummary_EasternClockInAlaska()
    {
        var scan = new PlanScenario().Card([.. Z, Clip.Dng("20260927141000", 125, Sites.Zachar)]).Build();
        var b = PlanScenario.CreatePlanner().Prepare(scan);
        Assert.Equal(ClockMode.Zone, b.Clock.Mode);
        Assert.Equal("America/New_York", b.Clock.ZoneId);
        Assert.Equal(4, b.Clock.MismatchItems);
        Assert.Equal("America/Anchorage", Assert.Single(b.Clock.MismatchSiteZones));
        Assert.StartsWith("Drone clock: US Eastern (America/New_York), learned from 3 videos.", b.Clock.Headline);
        Assert.Contains("(Alaska)", b.Clock.Headline);
        var resolved = TimeResolver.Resolve(scan.Raw, scan.Clock, new GeoTimeZoneResolver(), null, PlanScenario.PcZone, PlanScenario.NowUtc);
        Assert.Equal(DroneClock.Summarize(scan.Clock, resolved).Headline, b.Clock.Headline);
    }

    [Fact]
    public void Prepare_PhotoDayReasonFromProbablyImported()
    {
        const string zrel = @"2026\2026-09\2026-09-27 Zachar Bay";
        var b = new PlanScenario().Library(zrel, Z).Card(Clip.Dng("20260815200000", 119, Sites.Anvil)).Prepare();
        Assert.Equal("photo-only day before the last imported video (Sep 27)", Assert.Single(b.PhotoDays).Reason);
    }
}
