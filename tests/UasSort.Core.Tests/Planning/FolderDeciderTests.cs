// tests/UasSort.Core.Tests/Planning/FolderDeciderTests.cs
using UasSort.Core.Planning;
using UasSort.Testing.Planning;

namespace UasSort.Core.Tests.Planning;

public sealed class FolderDeciderTests
{
    private const string Zrel = @"2026\2026-09\2026-09-27 Zachar Bay";
    private const string CouncilRel = @"2026\2026-07\2026-07-25 Council Road";
    private static readonly Tuning R50 = new(50, 1);
    private static readonly RawItem[] Z =
    [
        Clip.Vid("20260927140127", 123, Sites.Zachar), Clip.Vid("20260927140144", 124, Sites.Zachar), Clip.Vid("20260927142416", 148, Sites.Zachar),
    ];

    private sealed record Setup(ScanResult Scan, ClusterResult Clusters, HashSet<ItemId> Included, FolderDecider Decider)
    {
        public GroupTarget Pass1(int i) => Decider.Decide(Clusters.Groups[i], Included, null);
        public GroupTarget Final(int i)
        {
            var p1 = Clusters.Groups.ToDictionary(g => g.Id, g => Decider.Decide(g, Included, null));
            var g = Clusters.Groups[i];
            return Decider.BordersUserSplit(g) ? Decider.Decide(g, Included, p1) : p1[g.Id];
        }
    }

    private static Setup Make(PlanScenario s, IReadOnlyList<PlanEdit>? edits = null, IEnumerable<ItemId>? alsoInclude = null, Tuning? t = null)
    {
        var scan = s.Build();
        var items = scan.Raw.Where(r => r.Unit is VideoUnit)
            .Select(r => ItemFactory.Of(r, NewnessRules.Video((VideoUnit)r.Unit, scan.Library, scan.Ledger))).ToList();
        var cr = Clusterer.Cluster(items, t ?? R50, edits ?? [], out _);
        var inc = items.Where(i => i.Newness is IsNew).Select(i => i.Raw.Unit.Id).ToHashSet();
        foreach (var id in alsoInclude ?? []) inc.Add(id);
        return new Setup(scan, cr, inc, new FolderDecider(scan.Library, cr.Groups, t ?? R50));
    }

    private static PlanScenario ZWithLedger() =>
        Z.Aggregate(new PlanScenario().Library(Zrel, Z), (s, z) => s.LedgerFile(z, Zrel, Sites.Zachar, "America/Anchorage"));

    [Fact]
    public void Wall_SameDay_AppendHigh()
    {
        var s = Make(new PlanScenario().Card([.. Z, Clip.Vid("20260927160000", 160, Sites.Zachar)]).Library(Zrel, Z));
        var a = Assert.IsType<Append>(s.Pass1(0));
        Assert.Equal((Confidence.High, "same day as clips already in this folder"), (a.Confidence, a.Why));
    }

    [Fact] // Scenario D in miniature
    public void Wall_DifferentDay_AppendMediumWithSplitHint()
    {
        var c = new[] { Clip.Vid("20260725232655", 117, Sites.Council), Clip.Vid("20260726022937", 118, Sites.Council) };
        var a1 = Clip.Vid("20260726235645", 1, Sites.Anvil);
        var s = Make(new PlanScenario().Card([.. c, a1, Clip.Vid("20260727000012", 2, Sites.Anvil)]).Library(CouncilRel, c));
        var a = Assert.IsType<Append>(s.Pass1(0));
        Assert.Equal(Confidence.Medium, a.Confidence);
        Assert.Equal("different day, 34 mi from Council Road", a.Why);
        Assert.Equal(new SplitBefore(a1.Id()), Assert.Single(a.Hint!.Fix));
    }

    [Fact] // a wall folder dated after the New clips
    public void Wall_NewClipsBeforeFolderDate_NewFolderWithSplitAtFirstImported()
    {
        var f = new[] { Clip.Vid("20260726000000", 5, Sites.Anvil), Clip.Vid("20260726010000", 6, Sites.Anvil) };  // Jul 25 AKDT
        var early = Clip.Vid("20260725000000", 1, Sites.Anvil);                                                      // Jul 24 AKDT
        var s = Make(new PlanScenario().Card([early, .. f]).Library(@"2026\2026-07\2026-07-25 Anvil", f));
        Assert.IsType<NewFolder>(s.Pass1(0));
        Assert.Equal(f[0].Id(), s.Decider.NewBeforeWallSplitPoint(s.Clusters.Groups[0], s.Included));
    }

    [Fact]
    public void NoNew_WithWall_IsAlreadyImported_WithoutWall_IsNothingToCopy()
    {
        Assert.IsType<AlreadyImported>(Make(new PlanScenario().Card(Z).Library(Zrel, Z)).Pass1(0));

        var c1 = Clip.Vid("20260927140127", 123, Sites.Zachar, size: 1);
        var c2 = Clip.Vid("20260927140144", 124, Sites.Zachar, size: 2);
        var d = Clip.Vid("20260927142416", 148, Sites.Zachar);
        var s = Make(new PlanScenario().Card(c1, c2, d)
            .LedgerFile(Clip.Vid("20260927140127", 123, Sites.Zachar, size: 9), Zrel)
            .LedgerFile(Clip.Vid("20260927140144", 124, Sites.Zachar, size: 9), Zrel)
            .LedgerDecision(d, DecisionKind.Dismissed));
        Assert.Equal(new NothingToCopy("nothing to copy: 2 conflicts, 1 dismissed"), s.Pass1(0));

        var culled = Make(new PlanScenario().Card(Z[0], Z[1]).LedgerFile(Z[0], Zrel).LedgerFile(Z[1], Zrel));
        Assert.Equal(new NothingToCopy("nothing to copy: 2 already imported"), culled.Pass1(0));
    }

    [Fact] // inclusion before Decide: a ticked Conflict turns AlreadyImported into Append
    public void TickedConflict_TurnsAlreadyImportedIntoAppend()
    {
        var conflict = Clip.Vid("20260927150000", 150, Sites.Zachar, size: 7_340_032);
        var libVersion = Clip.Vid("20260927150000", 150, Sites.Zachar, size: 9_999_999);
        var scen = () => new PlanScenario().Card([.. Z, conflict]).Library(Zrel, [.. Z, libVersion]);
        Assert.IsType<AlreadyImported>(Make(scen()).Pass1(0));
        Assert.Equal(Confidence.High, Assert.IsType<Append>(Make(scen(), alsoInclude: [conflict.Id()]).Pass1(0)).Confidence);
    }

    [Fact] // ported #24 and #25
    public void NoWall_SameDates_HighWithLedgerCentroid_MediumWithout()
    {
        var n = Clip.Vid("20260927160000", 160, Sites.Zachar);
        var high = Assert.IsType<Append>(Make(ZWithLedger().Card(n)).Pass1(0));
        Assert.Equal((Confidence.High, "same dates, <0.1 mi"), (high.Confidence, high.Why));
        var med = Assert.IsType<Append>(Make(new PlanScenario().Library(Zrel, Z).Card(n)).Pass1(0));
        Assert.Equal((Confidence.Medium, "same dates, location unknown"), (med.Confidence, med.Why));
    }

    [Fact] // ported #26–#29 and the adjacent-day confidence rows
    public void NoWall_AdjacentDays()
    {
        var near = Assert.IsType<Append>(Make(ZWithLedger().Card(Clip.Vid("20260928180000", 170, Sites.Zachar))).Pass1(0));
        Assert.Equal((Confidence.High, "next day, <0.1 mi"), (near.Confidence, near.Why));

        var twenty = Clip.Vid("20260928180000", 170, new GeoPoint(57.5368 + 0.2894631662875806, -153.7484));
        var s20 = Make(ZWithLedger().Card(twenty));
        var mid = Assert.IsType<Append>(s20.Pass1(0));
        Assert.Equal((Confidence.Medium, "different day, 20 mi"), (mid.Confidence, mid.Why));
        var fix = Assert.IsType<Retarget>(Assert.Single(mid.Hint!.Fix));
        Assert.IsType<NewFolderTarget>(fix.Choice);

        Assert.IsType<NewFolder>(Make(ZWithLedger().Card(Clip.Vid("20260928180000", 170, Sites.KodiakTown))).Pass1(0));

        var unknown = Make(new PlanScenario().Library(Zrel, Z).Card(Clip.Vid("20260928180000", 170, Sites.Zachar)));
        Assert.IsType<NewFolder>(unknown.Pass1(0));
        Assert.Contains(unknown.Decider.AppendCandidates(unknown.Clusters.Groups[0], 5), f => f.Ref.Description == "Zachar Bay");

        Assert.IsType<NewFolder>(Make(ZWithLedger().Card(Clip.Vid("20260926180000", 170, Sites.Zachar))).Pass1(0));
    }

    [Fact] // two-pass UserSplit: the earlier group keeps the shared target
    public void TwoPass_LaterNeighbourBecomesNewFolder()
    {
        var a = Clip.Vid("20260927160000", 160, Sites.Zachar);
        var b = Clip.Vid("20260927161000", 161, Sites.Zachar);
        var s = Make(ZWithLedger().Card(a, b), [new SplitBefore(b.Id())]);
        Assert.IsType<Append>(s.Pass1(0));
        Assert.IsType<Append>(s.Pass1(1));
        Assert.IsType<Append>(s.Final(0));
        Assert.IsType<NewFolder>(s.Final(1));
    }

    [Fact] // two-pass UserSplit: F is the Wall of the later neighbour → the earlier group is NewFolder, the walled one keeps F
    public void TwoPass_WallOfNeighbour_ExcludedForOtherSide()
    {
        var early = Clip.Vid("20260927130000", 120, Sites.Zachar);
        var late = Clip.Vid("20260927160000", 160, Sites.Zachar);
        var s = Make(ZWithLedger().Card([early, .. Z, late]), [new SplitBefore(Z[0].Id())]);
        Assert.Equal(2, s.Clusters.Groups.Length);
        Assert.IsType<Append>(s.Pass1(0));
        Assert.IsType<NewFolder>(s.Final(0));
        Assert.Equal("Zachar Bay", Assert.IsType<Append>(s.Final(1)).Folder.Description);
    }
}
