// tests/UasSort.Review.Tests/KeyboardTests.cs
namespace UasSort.Review.Tests;

public class KeyboardTests
{
    private static readonly ItemId AnvilFirst = TestPlans.Id(TestPlans.CouncilAnvil()[2].Name);

    [Fact]
    public async Task Keys_CtrlZ_UndoesExceptInATextBox()
    {
        using var h = ReviewHarness.Create(TestPlans.CouncilAnvil());
        await h.SettleAsync();
        await h.Vm.SplitBeforeAsync(AnvilFirst);
        await h.SettleAsync();

        Assert.False(h.Vm.HandleKey(ReviewKey.Z, KeyMods.Ctrl, KeyFocus.TextBox));
        await h.SettleAsync();
        Assert.Equal(2, h.Vm.Plan.Groups.Length);

        Assert.True(h.Vm.HandleKey(ReviewKey.Z, KeyMods.Ctrl, KeyFocus.Other));
        await Eventually.TrueAsync(() => h.Vm.Plan.Groups.Length == 1, h.Ui);
        Assert.True(h.Vm.HandleKey(ReviewKey.Z, KeyMods.Ctrl | KeyMods.Shift, KeyFocus.Other));
        await Eventually.TrueAsync(() => h.Vm.Plan.Groups.Length == 2, h.Ui);
        Assert.False(h.Vm.HandleKey(ReviewKey.Y, KeyMods.Ctrl, KeyFocus.TextBox));
    }

    [Fact]
    public async Task Keys_SpaceInRenameBox_LeavesIncludedUnchanged()
    {
        using var h = ReviewHarness.Create(TestPlans.CouncilAnvil());
        await h.SettleAsync();
        h.Vm.Videos.SelectedEntry = h.Card(0);
        h.Vm.Videos.SetSelectedClips([AnvilFirst]);
        var included = h.Vm.Plan.Included;

        Assert.False(h.Vm.HandleKey(ReviewKey.Space, KeyMods.None, KeyFocus.TextBox));
        await h.SettleAsync();
        Assert.Equal(included, h.Vm.Plan.Included);

        Assert.True(h.Vm.HandleKey(ReviewKey.Space, KeyMods.None, KeyFocus.ClipItem));
        await Eventually.TrueAsync(() => !h.Vm.Plan.Included.Contains(AnvilFirst), h.Ui);
    }

    [Fact]
    public async Task Keys_TabsMergeSplitOffloadRescanRename()
    {
        using var h = ReviewHarness.Create(TestPlans.CouncilAnvil(), tuning: new Tuning(25, 1),
            suggestions: [new Suggestion("Council Road", DescSource.Feature, Distance.FromMiles(0.2), null)]);
        await h.SettleAsync();
        var offload = 0;
        var rescan = 0;
        ItemId? renameFocus = null;
        h.Vm.OffloadRequested += () => offload++;
        h.Vm.RescanRequested += () => rescan++;
        h.Vm.FocusRenameRequested += a => renameFocus = a;

        Assert.True(h.Vm.HandleKey(ReviewKey.D2, KeyMods.Ctrl, KeyFocus.Other));
        Assert.Equal(1, h.Vm.SelectedTab);

        h.Vm.Videos.SelectedEntry = h.Card(0);
        Assert.True(h.Vm.HandleKey(ReviewKey.F2, KeyMods.None, KeyFocus.TimelineItem));
        Assert.Equal(h.Card(0).Anchor, renameFocus);

        Assert.True(h.Vm.HandleKey(ReviewKey.M, KeyMods.Ctrl, KeyFocus.TimelineItem));
        await Eventually.TrueAsync(() => h.Vm.Plan.Groups.Length == 1, h.Ui);

        h.Vm.Videos.SelectedEntry = h.Card(0);
        h.Vm.FocusedClip = h.Vm.Videos.Clips.Single(c => c.Id == AnvilFirst);
        Assert.True(h.Vm.HandleKey(ReviewKey.S, KeyMods.Ctrl | KeyMods.Shift, KeyFocus.ClipItem));
        await Eventually.TrueAsync(() => h.Vm.Plan.Groups.Length == 2, h.Ui);

        Assert.True(h.Vm.HandleKey(ReviewKey.Enter, KeyMods.Ctrl, KeyFocus.Other));
        Assert.Equal(1, offload);
        Assert.True(h.Vm.HandleKey(ReviewKey.F5, KeyMods.None, KeyFocus.Other));
        Assert.Equal(1, rescan);
    }
}
