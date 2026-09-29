// tests/UasSort.Core.Tests/Naming/DescriptionSuggesterTests.cs
using UasSort.Core.Naming;
using UasSort.Core.Planning;
using UasSort.Testing.Planning;

namespace UasSort.Core.Tests.Naming;

public sealed class DescriptionSuggesterTests
{
    private static PlaceHit P(string name, double lat, double lon, PlaceClass cls, int pop = 0) =>
        new(name, new GeoPoint(lat, lon), cls, cls == PlaceClass.Feature ? "MT" : "", pop, "America/Anchorage", default);

    private static GroupDraft OneGroup(ScanResult scan)
    {
        var items = scan.Raw.Select(r => ItemFactory.Of(r, NewnessRules.Video((VideoUnit)r.Unit, scan.Library, scan.Ledger))).ToList();
        return Assert.Single(Clusterer.Cluster(items, new Tuning(50, 1), [], out _).Groups);
    }

    [Fact] // suggestions ranked per local day: a merged two-day group offers both names
    public void PerDay_FeatureThenTown_InDayOrder()
    {
        var scan = new PlanScenario().Card(
            Clip.Vid("20260725232655", 117, Sites.Council), Clip.Vid("20260726235645", 1, Sites.Anvil)).Build();
        var places = new FakePlaceIndex(
            P("Council Hill", 64.6945, -164.2657, PlaceClass.Feature),
            P("Anvil Mountain", 64.5650, -165.3700, PlaceClass.Feature),
            P("Nome", 64.5011, -165.4064, PlaceClass.Populated, 3699));
        var s = DescriptionSuggester.Suggest(OneGroup(scan), scan.Library, places);
        Assert.Equal(["Council Hill", "Anvil Mountain", "near Nome"], s.Select(x => x.Text));
        Assert.Equal([DescSource.Feature, DescSource.Feature, DescSource.Town], s.Select(x => x.Source));
        Assert.Equal(new DateOnly(2026, 7, 25), s[0].ForDay);
        Assert.Equal(new DateOnly(2026, 7, 26), s[1].ForDay);
    }

    [Fact]
    public void WallDescriptionFirst_ThenLedgerFolderWithin3Mi()
    {
        const string zrel = @"2026\2026-09\2026-09-27 Zachar Bay";
        var z = Clip.Vid("20260927140127", 123, Sites.Zachar);
        var walled = new PlanScenario().Card(z).Library(zrel, z).Build();
        Assert.Equal((DescSource.ExistingFolder, "Zachar Bay"), (DescriptionSuggester.Suggest(OneGroup(walled), walled.Library, null)[0].Source,
                                                                  DescriptionSuggester.Suggest(OneGroup(walled), walled.Library, null)[0].Text));
        var ledger = new PlanScenario().Library(zrel, z).LedgerFile(z, zrel, Sites.Zachar, "America/Anchorage")
            .Card(Clip.Vid("20260928180000", 170, Sites.Zachar)).Build();
        var s = Assert.Single(DescriptionSuggester.Suggest(OneGroup(ledger), ledger.Library, null));
        Assert.Equal((DescSource.Ledger, "Zachar Bay"), (s.Source, s.Text));
    }

    [Fact] // day 1 alone gives 2 names; days 2-3 add new names, an exact repeat and a case variant; 7 distinct -> first 6
    public void Deduplicated_AndCappedAtSix()
    {
        var scan = new PlanScenario().Card(
            Clip.Vid("20260601100000", 1, new GeoPoint(60.0, -150)),
            Clip.Vid("20260602100000", 2, new GeoPoint(60.1, -150)),
            Clip.Vid("20260603100000", 3, new GeoPoint(60.2, -150))).Build();
        var places = new FakePlaceIndex(
            P("Alpha Peak", 60.001, -150, PlaceClass.Feature), P("Beta Lake", 60.002, -150, PlaceClass.Feature),
            P("alpha peak", 60.101, -150, PlaceClass.Feature), P("Gamma Cove", 60.102, -150, PlaceClass.Feature),
            P("Delta", 60.110, -150, PlaceClass.Populated, 50),
            P("Beta Lake", 60.201, -150, PlaceClass.Feature), P("Epsilon Ridge", 60.202, -150, PlaceClass.Feature),
            P("Zeta", 60.210, -150, PlaceClass.Populated, 60), P("Eta", 60.212, -150, PlaceClass.Populated, 70));
        var s = DescriptionSuggester.Suggest(OneGroup(scan), scan.Library, places);
        Assert.Equal(["Alpha Peak", "Beta Lake", "Gamma Cove", "Delta", "Epsilon Ridge", "Zeta"], s.Select(x => x.Text));
        Assert.Equal([new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 2), new DateOnly(2026, 6, 2),
                      new DateOnly(2026, 6, 3), new DateOnly(2026, 6, 3)], s.Select(x => x.ForDay));
        Assert.True(DescriptionSuggester.Prefills(DescSource.Place));
        Assert.False(DescriptionSuggester.Prefills(DescSource.Town));
    }

    [Fact] // Ref §8.7 rule 5: 60 nearer sub-1,000 places must not hide the nearest town of >= 1,000 within 30 mi
    public void Town_NotHiddenByManyNearerSmallPlaces()
    {
        var scan = new PlanScenario().Card(Clip.Vid("20260601100000", 1, new GeoPoint(61.0, -149))).Build();
        var hits = Enumerable.Range(0, 60)
            .Select(i => P($"Hamlet {i:00}", 61.1 + i * 0.001, -149, PlaceClass.Populated, 100 + i))
            .Append(P("Bigtown", 61.3, -149, PlaceClass.Populated, 1500))
            .ToList();
        var s = DescriptionSuggester.Suggest(OneGroup(scan), scan.Library, new FakePlaceIndex(hits));
        var town = Assert.Single(s);
        Assert.Equal(("near Bigtown", DescSource.Town), (town.Text, town.Source));
    }
}
