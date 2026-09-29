// tests/UasSort.Review.Tests/MapProjectionTests.cs
namespace UasSort.Review.Tests;

public class MapProjectionTests
{
    private static Plan Derive(IReadOnlyList<PlanClip> clips, double r = 50)
        => new ScriptedDeriver().Derive(TestPlans.Base(clips), new Tuning(r, 1), [], new SessionFlags(false), 1, TestContext.Current.CancellationToken);

    [Fact]
    public void MapProjection_SetData_ColoursLabelsAndItems()
    {
        var plan = Derive(TestPlans.Zachar());
        var data = MapProjection.SetData(plan, 7);

        Assert.Equal(7, data.Rev);
        Assert.Equal(3, data.Items.Length);
        Assert.All(data.Items, i => Assert.Equal("video", i.Kind));
        var g = Assert.Single(data.Groups);
        Assert.Equal("#1F77B4", g.Color);
        Assert.Equal("Sep 27 · 3 clips", g.Label);
        Assert.Empty(data.Jumps);
    }

    [Fact]
    public void MapProjection_NoGpsClip_UsesGroupCentreAsHollowDot()
    {
        var clips = TestPlans.Zachar().Append(new PlanClip("DJI_20260927143000_0149_D.MP4", TestPlans.Utc(2026, 9, 27, 18, 30), TestPlans.Anchorage, null, null)).ToList();
        var plan = Derive(clips);
        var data = MapProjection.SetData(plan, 1);

        var hollow = Assert.Single(data.Items, i => i.Kind == "videoNoGps");
        Assert.Equal(plan.Groups[0].Centroid!.Value.Lon, hollow.Lon);
    }

    [Fact]
    public void MapProjection_DistanceBoundary_BecomesJumpLineWithMilesLabel()
    {
        var plan = Derive(TestPlans.CouncilAnvil(), r: 25);
        var jump = Assert.Single(MapProjection.SetData(plan, 1).Jumps);
        Assert.Equal("34 mi · 24 h", jump.Label);
        Assert.Equal("#FF7F0E", MapProjection.SetData(plan, 1).Groups[1].Color);
    }

    [Fact]
    public void MapProjection_Select_FitsTheGroupsPoints()
    {
        var plan = Derive(TestPlans.Zachar());
        var sel = MapProjection.Select(plan, plan.Groups[0].Id, [TestPlans.Id(TestPlans.Zachar()[1].Name)], fit: true);

        Assert.Equal(plan.Groups[0].Id.Anchor.CardRelPath, sel.GroupId);
        Assert.Equal(TestPlans.Id(TestPlans.Zachar()[1].Name).CardRelPath, Assert.Single(sel.ItemIds));
        Assert.Equal(4, sel.Bbox.Length);
        Assert.True(sel.Bbox[0] <= -153.738973 && sel.Bbox[2] >= -153.738973);
        Assert.True(sel.Bbox[1] <= 57.5415 && sel.Bbox[3] >= 57.550442);
    }

    [Fact]
    public void MapBridge_SendData_ThrottlesToTenPerSecondWithTrailingLatest()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero));
        var sent = new List<string>();
        using var bridge = new MapBridge(sent.Add, new ListLog(), time);
        var p1 = Derive(TestPlans.CouncilAnvil(), 50);
        var p2 = Derive(TestPlans.CouncilAnvil(), 40);
        var p3 = Derive(TestPlans.CouncilAnvil(), 25);

        bridge.SendData(p1);
        bridge.SendData(p2);
        bridge.SendData(p3);
        Assert.Single(sent);

        time.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(2, sent.Count);
        var last = Assert.IsType<MapSetData>(MapBridge.ParseHostMessage(sent[1]));
        Assert.Equal(2, last.Groups.Length);
        Assert.Equal(2, last.Rev);
    }
}
