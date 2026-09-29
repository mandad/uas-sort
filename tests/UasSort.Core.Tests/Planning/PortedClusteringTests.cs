// tests/UasSort.Core.Tests/Planning/PortedClusteringTests.cs
using UasSort.Core.Naming;
using UasSort.Testing.Planning;
using static UasSort.Testing.Planning.PlanAsserts;

namespace UasSort.Core.Tests.Planning;

#pragma warning disable CA1861 // expected partitions read best inline next to their scenario; each test runs once
/// <summary>Ref §13 table rows #1–#21 (spike: test_grouping.py Normalisation + Clustering).</summary>
public sealed class PortedClusteringTests
{
    private static readonly GeoPoint SiteW = Sites.Anvil;
    private static readonly GeoPoint SiteE70 = new(64.5627, -165.3696 + 2.36);    // ~70.0 mi east of SiteW

    private static (PlanBase B, Plan P) Run(Tuning t, params RawItem[] card)
    {
        var b = new PlanScenario().Card(card).Prepare();
        return (b, PlanScenario.Derive(b, t));
    }

    private static Plan Plan(Tuning t, params RawItem[] card) => Run(t, card).P;
    private static readonly Tuning R50 = new(50, 1);
    private static readonly Tuning R25 = new(25, 1);

    [Fact] // #1
    public void Midnight_LocalDateFromSiteZone()
    {
        var p = Plan(R50, Clip.Vid("20260726035000", 1, Sites.Anvil), Clip.Vid("20260726041000", 2, Sites.Anvil));
        var g = Assert.Single(p.Groups);
        Assert.Equal(new DateOnly(2026, 7, 25), g.Start);
        Assert.Equal(@"2026\2026-07\2026-07-25 Anvil", FolderNamer.NewFolderRel(g.Start, "Anvil"));
    }

    [Fact] // #2
    public void Midnight_G0_HoursGuardKeepsOneGroup()
        => Assert.Single(Plan(new Tuning(50, 0), Clip.Vid("20260726035000", 1, Sites.Anvil), Clip.Vid("20260726041000", 2, Sites.Anvil)).Groups);

    [Fact] // #3
    public void Dng_UsesClockZoneAndSiteZone()
    {
        var dng = Clip.Dng("20260726035500", 2, Sites.Anvil);
        var (b, _) = Run(R50, Clip.Vid("20260726035000", 1, Sites.Anvil), dng);
        var d = ItemOf(b, dng);
        Assert.Equal(("America/New_York", TimeSource.DroneClockZone), (b.Scan.Clock.ZoneId, d.Time.Source));
        Assert.Equal(new DateTime(2026, 7, 26, 7, 55, 0, DateTimeKind.Utc), d.Time.CaptureUtc);
        Assert.Equal(new DateOnly(2026, 7, 25), d.Time.LocalDate);
    }

    [Fact] // #4
    public void Hawaii_SiteZoneNotPcZone()
    {
        var v = Clip.Vid("20260301053000", 1, Sites.Makaha);
        var i = ItemOf(Run(R50, v).B, v);
        Assert.Equal(("Pacific/Honolulu", new DateOnly(2026, 2, 28)), (i.Time.TzId, i.Time.LocalDate));
    }

    [Fact] // #5 (Changed: the zone learner handles the DST change)
    public void ZoneLearner_DstChangeStillFitsNewYork()
    {
        var dng = Clip.Dng("20261110121000", 3, Sites.Anvil);
        var (b, _) = Run(R50, Clip.Vid("20261020120000", 1, Sites.Anvil, clockMinusUtcHours: -4),
                              Clip.Vid("20261110120000", 2, Sites.Anvil, clockMinusUtcHours: -5), dng);
        Assert.Equal((ClockMode.Zone, "America/New_York"), (b.Scan.Clock.Mode, b.Scan.Clock.ZoneId));
        var d = ItemOf(b, dng);
        Assert.Equal(TimeSource.DroneClockZone, d.Time.Source);
        Assert.Equal(new DateTime(2026, 11, 10, 17, 10, 0, DateTimeKind.Utc), d.Time.CaptureUtc);
    }

    [Theory] // #6 (Changed: Setting mode always has a zone; never Mtime)
    [InlineData(true)]
    [InlineData(false)]
    public void NoMp4_UsesStoredZone(bool explicitZone)
    {
        var dng = Clip.Dng("20260927150000", 1, Sites.Zachar);
        var s = explicitZone ? new PlanScenario { ClockMode = StoredClockMode.Zone, ClockZone = "America/New_York" } : new PlanScenario();
        var b = s.Card(dng).Prepare();
        var d = ItemOf(b, dng);
        Assert.Equal(ClockMode.Setting, b.Scan.Clock.Mode);
        Assert.Equal(TimeSource.DroneClockSetting, d.Time.Source);
        Assert.True(d.Flags.HasFlag(ItemFlags.ClockFromSetting));
        Assert.True(d.Flags.HasFlag(ItemFlags.ClockMismatch));
        Assert.Equal(new DateTime(2026, 9, 27, 19, 0, 0, DateTimeKind.Utc), d.Time.CaptureUtc);
    }

    [Fact] // #7
    public void Truncated_TimedByClockAndJoins()
    {
        var t = Clip.Vid("20260727002013", 14, Sites.Anvil, moov: false);
        var (b, p) = Run(R50, Clip.Vid("20260726235645", 1, Sites.Anvil), t, Clip.Vid("20260727002118", 15, Sites.Anvil));
        var i = ItemOf(b, t);
        Assert.Equal(TimeSource.DroneClockZone, i.Time.Source);
        Assert.Equal(new DateTime(2026, 7, 27, 4, 20, 13, DateTimeKind.Utc), i.Time.CaptureUtc);
        Assert.True(i.Flags.HasFlag(ItemFlags.Truncated));
        Assert.True(i.Flags.HasFlag(ItemFlags.ClockMismatch));
        Assert.Single(p.Groups);
    }

    [Fact] // #9
    public void Trip_AcrossMonth()
    {
        var g = Assert.Single(Plan(R50, Clip.Vid("20260731230000", 1, Sites.Anvil), Clip.Vid("20260801230000", 2, new GeoPoint(64.58, -165.40)),
                                   Clip.Vid("20260802230000", 3, Sites.Anvil)).Groups);
        Assert.Equal(@"2026\2026-07\2026-07-31 Trip", FolderNamer.NewFolderRel(g.Start, "Trip"));
    }

    [Fact] // #10
    public void Trip_AcrossYear()
    {
        var g = Assert.Single(Plan(R50, Clip.Vid("20261231230000", 1, Sites.Anvil), Clip.Vid("20270101230000", 2, Sites.Anvil)).Groups);
        Assert.Equal(@"2026\2026-12\2026-12-31 NY", FolderNamer.NewFolderRel(g.Start, "NY"));
    }

    [Fact] // #11
    public void TwoDayGap_Splits()
    {
        var p = Plan(R50, Clip.Vid("20260722230000", 1, Sites.Anvil), Clip.Vid("20260725230000", 2, Sites.Anvil));
        Assert.Equal(2, p.Groups.Length);
        Assert.Equal(BoundaryCause.DayGap, Assert.Single(p.Boundaries).Cause);
    }

    private static RawItem[] CouncilAnvil() =>
    [
        Clip.Vid("20260725232655", 117, Sites.Council), Clip.Vid("20260726022937", 118, Sites.Council),
        Clip.Vid("20260726235645", 1, Sites.Anvil), Clip.Vid("20260727000012", 2, Sites.Anvil),
    ];

    [Fact] // #12 (R-dependent) at 50 mi
    public void CouncilAnvil_R50_OneGroupEmphasisedSplit()
    {
        var card = CouncilAnvil();
        var p = Plan(R50, card);
        var g = Assert.Single(p.Groups);
        Assert.Equal(4, g.Videos.Length);
        var ds = Assert.Single(g.DaySplits);
        Assert.Equal(card[2].Id(), ds.FirstOfDay);
        Assert.Equal(33.9, ds.Apart!.Value.Miles, 1);
        Assert.True(ds.Emphasised);
        var w = Assert.Single(p.Issues, i => i.Code == IssueCode.EmphasisedDaySplit);
        Assert.Equal(IssueSeverity.Warning, w.Severity);
    }

    [Fact] // #12 (R-dependent) at 25 mi
    public void CouncilAnvil_R25_TwoGroups()
    {
        var p = Plan(R25, CouncilAnvil());
        Assert.Equal(new[] { new[] { "20260725232655", "20260726022937" }, new[] { "20260726235645", "20260727000012" } }, Ids(p));
        Assert.Equal(BoundaryCause.Distance, Assert.Single(p.Boundaries).Cause);
    }

    [Fact] // #13
    public void Kodiak_MultiDayJoins()
    {
        var g = Assert.Single(Plan(R50, Clip.Vid("20260523015251", 40, Sites.KodiakTown), Clip.Vid("20260523201928", 52, Sites.KodiakTown),
                                   Clip.Vid("20260524190521", 64, new GeoPoint(57.75, -152.50)), Clip.Vid("20260525092718", 85, Sites.KodiakTown)).Groups);
        Assert.Equal((new DateOnly(2026, 5, 22), new DateOnly(2026, 5, 25)), (g.Start, g.End));
    }

    [Fact] // #14 (53.4 mi > 50; the 3.4 mi margin is pinned)
    public void ZacharKodiak_SameDay_R50_Splits()
    {
        Assert.Equal(53.4, UasSort.Core.Planning.PlanningGeo.Haversine(Sites.Zachar, Sites.KodiakTown).Miles, 1);
        var p = Plan(R50, Clip.Vid("20260927140127", 123, Sites.Zachar), Clip.Vid("20260927140144", 124, Sites.Zachar),
                     Clip.Vid("20260927190000", 150, Sites.KodiakTown));
        Assert.Equal(2, p.Groups.Length);
        Assert.All(p.Groups, g => Assert.Equal(new DateOnly(2026, 9, 27), g.Start));
    }

    [Fact] // #15
    public void SameDay_ABA_NotReMerged()
        => Assert.Equal(3, Plan(R50, Clip.Vid("20260927140127", 1, Sites.Zachar), Clip.Vid("20260927190000", 2, Sites.KodiakTown),
                                Clip.Vid("20260927230000", 3, Sites.Zachar)).Groups.Length);

    [Fact] // #16 + other explicit R values (Nome A+B 7.5 mi, Newport AM+PM)
    public void NearSites_Join()
    {
        Assert.Single(Plan(R50, Clip.Vid("20260510104103", 2, Sites.NewportAm), Clip.Vid("20260510193745", 30, Sites.NewportPm)).Groups);
        Assert.Single(Plan(R50, Clip.Vid("20260704010948", 101, Sites.NomeA), Clip.Vid("20260704013615", 102, Sites.NomeB)).Groups);
        Assert.Single(Plan(new Tuning(8, 1), Clip.Vid("20260704010948", 101, Sites.NomeA), Clip.Vid("20260704013615", 102, Sites.NomeB)).Groups);
    }

    [Fact] // #17 (R-dependent)
    public void NoGps_NearerNeighbour_R25()
        => Assert.Equal(new[] { new[] { "20260725232655" }, new[] { "20260726234000", "20260726235645" } },
            Ids(Plan(R25, Clip.Vid("20260725232655", 117, Sites.Council), Clip.Vid("20260726234000", 99), Clip.Vid("20260726235645", 1, Sites.Anvil))));

    [Fact] // #17 duplicated on a synthetic pair ~70 mi apart, at R 50
    public void NoGps_NearerNeighbour_Synthetic70mi()
        => Assert.Equal(new[] { new[] { "20260725232655" }, new[] { "20260726234000", "20260726235645" } },
            Ids(Plan(R50, Clip.Vid("20260725232655", 117, SiteW), Clip.Vid("20260726234000", 99), Clip.Vid("20260726235645", 1, SiteE70))));

    private static readonly DateTime S1 = new(2026, 7, 26, 3, 20, 0, DateTimeKind.Utc);

    [Fact] // #18 (R-dependent)
    public void NoGps_SessionBeatsTime_R25()
        => Assert.Equal(new[] { new[] { "20260725232655", "20260726234000" }, new[] { "20260726235645" } },
            Ids(Plan(R25, Clip.Vid("20260725232655", 117, Sites.Council, session: S1), Clip.Vid("20260726234000", 99, session: S1),
                     Clip.Vid("20260726235645", 1, Sites.Anvil))));

    [Fact] // #18 synthetic duplicate at R 50
    public void NoGps_SessionBeatsTime_Synthetic70mi()
        => Assert.Equal(new[] { new[] { "20260725232655", "20260726234000" }, new[] { "20260726235645" } },
            Ids(Plan(R50, Clip.Vid("20260725232655", 117, SiteW, session: S1), Clip.Vid("20260726234000", 99, session: S1),
                     Clip.Vid("20260726235645", 1, SiteE70))));

    [Fact] // session-link regression: only GPS-bearing members' sessions block a distance split
    public void SessionLink_OnlyGpsBearingMembers_R25()
    {
        var s2 = new DateTime(2026, 7, 27, 3, 30, 0, DateTimeKind.Utc);
        Assert.Equal(new[] { new[] { "20260725232655" }, new[] { "20260726234000", "20260726235645" } },
            Ids(Plan(R25, Clip.Vid("20260725232655", 117, Sites.Council, session: S1), Clip.Vid("20260726234000", 99, session: s2),
                     Clip.Vid("20260726235645", 1, Sites.Anvil, session: s2))));
    }

    [Fact] // #19
    public void NoGps_FirstClipOfNewSessionFollowsSession()
    {
        var s1 = new DateTime(2026, 9, 27, 17, 55, 0, DateTimeKind.Utc);
        var s2 = new DateTime(2026, 9, 27, 18, 18, 0, DateTimeKind.Utc);
        Assert.Equal(new[] { new[] { "20260927140000" }, new[] { "20260927142000", "20260927150000" } },
            Ids(Plan(R50, Clip.Vid("20260927140000", 1, Sites.Zachar, session: s1), Clip.Vid("20260927142000", 2, session: s2),
                     Clip.Vid("20260927150000", 3, Sites.KodiakTown, session: s2))));
    }

    [Fact] // #20
    public void AllNoGps_TimeOnly()
        => Assert.Equal(2, Plan(R50, Clip.Vid("20260725232655", 1), Clip.Vid("20260726235645", 2), Clip.Vid("20260729120000", 3)).Groups.Length);

    [Fact] // #21 (Changed: miles; G case added)
    public void R_And_G_AreParameters()
    {
        RawItem[] card = [Clip.Vid("20260725232655", 117, Sites.Council), Clip.Vid("20260726235645", 1, Sites.Anvil)];
        Assert.Single(Plan(new Tuning(40, 1), card).Groups);
        var r25 = Plan(R25, card);
        Assert.Equal((2, BoundaryCause.Distance), (r25.Groups.Length, r25.Boundaries[0].Cause));
        var g0 = Plan(new Tuning(50, 0), card);
        Assert.Equal((2, BoundaryCause.DayGap), (g0.Groups.Length, g0.Boundaries[0].Cause));
    }
}
