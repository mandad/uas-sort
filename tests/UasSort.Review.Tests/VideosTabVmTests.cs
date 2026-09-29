// tests/UasSort.Review.Tests/VideosTabVmTests.cs
namespace UasSort.Review.Tests;

public class VideosTabVmTests
{
    private static readonly LibraryFolderRef Council = new(TestPlans.VideoRoot + @"\2026\2026-07\2026-07-25 Council Road", new(2026, 7, 25), "Council Road");
    private static readonly LibraryFolderRef Anvil = new(TestPlans.VideoRoot + @"\2026\2026-07\2026-07-26 Anvil Mountain", new(2026, 7, 26), "Anvil Mountain");

    private static Plan Derive(IReadOnlyList<PlanClip> clips, double r = 50, IReadOnlyList<PlanEdit>? edits = null, int rev = 1)
        => new ScriptedDeriver().Derive(TestPlans.Base(clips), new Tuning(r, 1), edits ?? [], new SessionFlags(false), rev, TestContext.Current.CancellationToken);

    private static IReadOnlyList<PlanClip> ImportedCouncilAnvilPlusZachar()
    {
        var ca = TestPlans.CouncilAnvil();
        return
        [
            ca[0] with { Newness = new Imported(Evidence.LibraryNameSize, Council, "same name and size") },
            ca[1] with { Newness = new Imported(Evidence.LibraryNameSize, Council, "same name and size") },
            ca[2] with { Newness = new Imported(Evidence.LibraryNameSize, Anvil, "same name and size") },
            ca[3] with { Newness = new Imported(Evidence.LibraryNameSize, Anvil, "same name and size") },
            .. TestPlans.Zachar(),
        ];
    }

    [Fact]
    public void VideosTab_ImportedRunsFoldIntoOneRow_AndExpand()
    {
        var tab = new VideosTabVm(new RecordingActions());
        tab.Update(new PlanIndex(Derive(ImportedCouncilAnvilPlusZachar())));

        Assert.Equal(2, tab.Timeline.Count);
        var fold = Assert.IsType<FoldedRunVm>(tab.Timeline[0]);
        Assert.Equal("2 groups already in library", fold.Text);
        Assert.IsType<GroupCardVm>(tab.Timeline[1]);
        Assert.Equal("Videos · 1 to offload", tab.Header);

        tab.ToggleFold(fold);
        Assert.Equal(3, tab.Timeline.Count);
        Assert.All(tab.Timeline, e => Assert.IsType<GroupCardVm>(e));
    }

    [Fact]
    public void VideosTab_SelectingAFold_ShowsItsClipsReadOnly()
    {
        var tab = new VideosTabVm(new RecordingActions());
        tab.Update(new PlanIndex(Derive(ImportedCouncilAnvilPlusZachar())));
        tab.SelectedEntry = tab.Timeline[0];

        Assert.Equal(4, tab.Clips.Count);
        Assert.All(tab.Clips, c => Assert.True(c.IsReadOnly));
    }

    [Fact]
    public void VideosTab_SelectionSurvivesRederive_SameInstanceOrContainingGroup()
    {
        var tab = new VideosTabVm(new RecordingActions());
        tab.Update(new PlanIndex(Derive(TestPlans.CouncilAnvil(), r: 25, rev: 1)));
        var anvilCard = (GroupCardVm)tab.Timeline[1];
        tab.SelectedEntry = anvilCard;

        tab.Update(new PlanIndex(Derive(TestPlans.CouncilAnvil(), r: 20, rev: 2)));
        Assert.Same(anvilCard, tab.SelectedEntry);

        tab.Update(new PlanIndex(Derive(TestPlans.CouncilAnvil(), r: 50, rev: 3)));
        var merged = Assert.IsType<GroupCardVm>(Assert.Single(tab.Timeline));
        Assert.Same(merged, tab.SelectedEntry);
        Assert.Equal(4, tab.Clips.Count);
    }

    [Fact]
    public void VideosTab_MapClick_SelectsGroupAndClip_CtrlAdds_GridSelectionHighlights()
    {
        var tab = new VideosTabVm(new RecordingActions());
        var plan = Derive(TestPlans.CouncilAnvil(), r: 25);
        tab.Update(new PlanIndex(plan));
        var selects = new List<(GroupId G, IReadOnlyList<ItemId> Ids, bool Fit)>();
        var clipEvents = new List<IReadOnlyList<ItemId>>();
        tab.MapSelectRequested += (g, ids, fit) => selects.Add((g, ids, fit));
        tab.ClipSelectionChanged += clipEvents.Add;
        var a1 = TestPlans.Id(TestPlans.CouncilAnvil()[2].Name);
        var a2 = TestPlans.Id(TestPlans.CouncilAnvil()[3].Name);
        var anvilGroup = plan.Groups[1].Id.Anchor.CardRelPath;

        tab.OnMapClick(new MapClick([a1.CardRelPath], anvilGroup, false, false));
        Assert.Same(tab.Timeline[1], tab.SelectedEntry);
        Assert.Equal<ItemId>([a1], tab.SelectedClipIds);
        Assert.True(selects[0].Fit);

        tab.OnMapClick(new MapClick([a2.CardRelPath], anvilGroup, true, false));
        Assert.Equal<ItemId>([a1, a2], tab.SelectedClipIds);
        Assert.Equal(2, clipEvents[^1].Count);

        tab.SetSelectedClips([a2]);
        var last = selects[^1];
        Assert.False(last.Fit);
        Assert.Equal<ItemId>([a2], last.Ids);

        tab.OnMapClickEmpty();
        Assert.Empty(tab.SelectedClipIds);
    }

    [Fact]
    public void VideosTab_NextCard_SkipsFolds()
    {
        var tab = new VideosTabVm(new RecordingActions());
        tab.Update(new PlanIndex(Derive(TestPlans.CouncilAnvil(), r: 25)));
        var first = (GroupCardVm)tab.Timeline[0];
        Assert.Same(tab.Timeline[1], tab.NextCard(first));
        Assert.Null(tab.NextCard((GroupCardVm)tab.Timeline[1]));
    }
}
