// tests/UasSort.Core.Tests/Planning/ClustererTests.cs
using UasSort.Core.Planning;
using UasSort.Testing.Planning;

namespace UasSort.Core.Tests.Planning;

#pragma warning disable CA1861 // expected partitions read best inline next to their scenario; each test runs once
public sealed class ClustererTests
{
    private static readonly Tuning R50 = new(50, 1);
    private static readonly Tuning R25 = new(25, 1);

    private static ClusterResult Run(Tuning t, IReadOnlyList<PlanEdit> edits, params Item[] items) => Clusterer.Cluster(items, t, edits, out _);
    private static ClusterResult Run(Tuning t, params Item[] items) => Run(t, [], items);
    private static string[][] Ids(ClusterResult r) =>
        r.Groups.Select(g => g.Videos.Select(v => v.Raw.Name[4..18]).ToArray()).ToArray();

    private static Item C117 => ItemFactory.Of(Clip.Vid("20260725232655", 117, Sites.Council));
    private static Item A1 => ItemFactory.Of(Clip.Vid("20260726235645", 1, Sites.Anvil));

    [Fact]
    public void TwoDayGap_SplitsWithDayGapCause()
    {
        var r = Run(R50, ItemFactory.Of(Clip.Vid("20260722230000", 1, Sites.Anvil)), ItemFactory.Of(Clip.Vid("20260725230000", 2, Sites.Anvil)));
        Assert.Equal(2, r.Groups.Length);
        var b = Assert.Single(r.Boundaries);
        Assert.Equal((BoundaryCause.DayGap, 3), (b.Cause, b.DayGap));
    }

    [Fact]
    public void HoursGuard_KeepsMidnightFlightTogetherAtG0()
        => Assert.Single(Run(new Tuning(50, 0), ItemFactory.Of(Clip.Vid("20260726035000", 1, Sites.Anvil)),
                                                 ItemFactory.Of(Clip.Vid("20260726041000", 2, Sites.Anvil))).Groups);

    [Fact]
    public void Distance_DependsOnR()
    {
        Assert.Single(Run(new Tuning(40, 1), C117, A1).Groups);
        var r = Run(R25, C117, A1);
        Assert.Equal(BoundaryCause.Distance, Assert.Single(r.Boundaries).Cause);
        Assert.Equal(33.9, r.Boundaries[0].Jump!.Value.Miles, 1);
    }

    [Fact]
    public void NoGps_GoesToNearerNeighbourInTime()
    {
        var nogps = ItemFactory.Of(Clip.Vid("20260726234000", 99));
        Assert.Equal(new[] { new[] { "20260725232655" }, new[] { "20260726234000", "20260726235645" } }, Ids(Run(R25, C117, nogps, A1)));
    }

    [Fact]
    public void NoGps_SessionOfLastGpsItemBeatsTime()
    {
        var s = new SessionKey(Clip.Serial, new DateTime(2026, 7, 26, 3, 20, 0, DateTimeKind.Utc));
        var c = ItemFactory.Of(Clip.Vid("20260725232655", 117, Sites.Council), session: s);
        var nogps = ItemFactory.Of(Clip.Vid("20260726234000", 99), session: s);
        Assert.Equal(new[] { new[] { "20260725232655", "20260726234000" }, new[] { "20260726235645" } }, Ids(Run(R25, c, nogps, A1)));
    }

    [Theory] // SessionKey tolerance (Ref §13): 0.8 s apart = same session, 3 s = different; different serials never
    [InlineData("TESTSERIAL", 0.8, 1)]
    [InlineData("TESTSERIAL", 3.0, 2)]
    [InlineData("OTHERSERIAL", 0.0, 2)]
    public void SessionTolerance_BlocksDistanceSplitOnlyWithinTwoSeconds(string serial2, double deltaS, int groups)
    {
        var t0 = new DateTime(2026, 9, 27, 17, 55, 0, DateTimeKind.Utc);
        var a = ItemFactory.Of(Clip.Vid("20260927140000", 1, Sites.Zachar), session: new SessionKey(Clip.Serial, t0));
        var b = ItemFactory.Of(Clip.Vid("20260927142000", 2, Sites.KodiakTown), session: new SessionKey(serial2, t0.AddSeconds(deltaS)));
        Assert.Equal(groups, Run(R50, a, b).Groups.Length);
    }

    [Fact]
    public void LibraryFolderWall_SplitsWithCause_AndSetsWall()
    {
        var am = ItemFactory.Of(Clip.Vid("20260801200000", 1, Sites.Anvil), ItemFactory.ImportedInto(@"2026\2026-08\2026-08-01 Anvil AM"));
        var nw = ItemFactory.Of(Clip.Vid("20260801202000", 9, Sites.Anvil));
        var pm = ItemFactory.Of(Clip.Vid("20260802200000", 3, Sites.Anvil), ItemFactory.ImportedInto(@"2026\2026-08\2026-08-02 Anvil PM"));
        var r = Run(R50, am, nw, pm);
        Assert.Equal(2, r.Groups.Length);
        Assert.Equal(BoundaryCause.LibraryFolder, r.Boundaries[0].Cause);
        Assert.Equal("Anvil AM", r.Groups[0].Wall!.Description);
        Assert.Equal("Anvil PM", r.Groups[1].Wall!.Description);
    }

    [Fact]
    public void SplitBefore_MidGroup_IsUserSplitWithNeighbour()
    {
        var c118 = ItemFactory.Of(Clip.Vid("20260726022937", 118, Sites.Council));
        var r = Run(R50, [new SplitBefore(A1.Raw.Unit.Id)], C117, c118, A1);
        Assert.Equal(2, r.Groups.Length);
        Assert.Equal(BoundaryCause.UserSplit, r.Boundaries[0].Cause);
        Assert.Equal(r.Groups[0].Id, r.Groups[1].UserSplitNeighbour);
        Assert.Null(r.Groups[0].UserSplitNeighbour);
    }

    [Fact] // §8.9 worked example: the chip still reads "split by you" where R 25 would split anyway
    public void SplitBefore_AtAutoBoundary_OverridesCause()
    {
        IReadOnlyList<PlanEdit> edits = [new SplitBefore(A1.Raw.Unit.Id)];
        foreach (var t in new[] { R50, R25, R50 })
        {
            var r = Run(t, edits, C117, A1);
            Assert.Equal(BoundaryCause.UserSplit, Assert.Single(r.Boundaries).Cause);
        }
    }

    [Fact] // §8.9 worked example: sites A, B, C 8 mi apart in a line
    public void Merge_AbsorbsGroupsBetween()
    {
        var a = ItemFactory.Of(Clip.Vid("20260601100000", 1, new GeoPoint(60, -150)));
        var b = ItemFactory.Of(Clip.Vid("20260601110000", 2, new GeoPoint(60.11578526651503, -150)));
        var c = ItemFactory.Of(Clip.Vid("20260601120000", 3, new GeoPoint(60.23157053303006, -150)));
        Assert.Equal(new[] { new[] { "20260601100000", "20260601110000" }, new[] { "20260601120000" } }, Ids(Run(new Tuning(10, 1), a, b, c)));
        Assert.Equal(3, Run(new Tuning(5, 1), a, b, c).Groups.Length);
        IReadOnlyList<PlanEdit> merge = [new Merge(a.Raw.Unit.Id, c.Raw.Unit.Id)];
        Assert.Single(Run(new Tuning(10, 1), merge, a, b, c).Groups);
        Assert.Single(Run(new Tuning(5, 1), merge, a, b, c).Groups);
    }

    [Fact]
    public void MergeAcrossWalls_IsInactive_NotDropped()
    {
        var am = ItemFactory.Of(Clip.Vid("20260801200000", 1, Sites.Anvil), ItemFactory.ImportedInto(@"2026\2026-08\2026-08-01 Anvil AM"));
        var pm = ItemFactory.Of(Clip.Vid("20260802200000", 3, Sites.Anvil), ItemFactory.ImportedInto(@"2026\2026-08\2026-08-02 Anvil PM"));
        var r = Clusterer.Cluster([am, pm], R50, [new Merge(am.Raw.Unit.Id, pm.Raw.Unit.Id)], out var missing);
        Assert.Equal(2, r.Groups.Length);
        Assert.Equal(0, missing);
    }

    [Fact] // §8.9 worked example: MoveToNewGroup leaves a non-contiguous group ordered by its anchor
    public void MoveToNewGroup_MiddleClip()
    {
        var v = Enumerable.Range(0, 3).Select(i => ItemFactory.Of(Clip.Vid($"2026092714{i:00}00", i + 1, Sites.Zachar))).ToArray();
        var r = Run(R50, [new MoveToNewGroup([v[1].Raw.Unit.Id])], v);
        Assert.Equal(2, r.Groups.Length);
        Assert.Equal(v[0].Raw.Unit.Id, r.Groups[0].Id.Anchor);
        Assert.Equal(new[] { v[0].Raw.Unit.Id, v[2].Raw.Unit.Id }, r.Groups[0].Videos.Select(x => x.Raw.Unit.Id));
        Assert.Equal(v[1].Raw.Unit.Id, r.Groups[1].Id.Anchor);
        Assert.Equal(BoundaryCause.UserSplit, r.Boundaries[0].Cause);
    }

    [Fact]
    public void MoveToGroup_TargetAmongItems_IsInactive_MissingItemsAreCounted()
    {
        var v = Enumerable.Range(0, 2).Select(i => ItemFactory.Of(Clip.Vid($"2026092714{i:00}00", i + 1, Sites.Zachar))).ToArray();
        var gone = new ItemId("DCIM/DJI_001/DJI_20260101000000_0999_D.MP4");
        var r = Clusterer.Cluster(v, R50,
            [new MoveToGroup([v[0].Raw.Unit.Id], v[0].Raw.Unit.Id), new SplitBefore(gone), new Merge(gone, v[0].Raw.Unit.Id)], out var missing);
        Assert.Single(r.Groups);
        Assert.Equal(2, missing);
    }
}
