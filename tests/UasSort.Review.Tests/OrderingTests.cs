// tests/UasSort.Review.Tests/OrderingTests.cs
namespace UasSort.Review.Tests;

public class OrderingTests
{
    private static readonly ItemId AnvilFirst = TestPlans.Id(TestPlans.CouncilAnvil()[2].Name);

    [Fact]
    public async Task Ordering_SlowEarlierPreview_NeverOverwritesALaterOne()
    {
        using var h = ReviewHarness.Create(TestPlans.CouncilAnvil());
        await h.SettleAsync();
        var n = h.Deriver.Calls.Count;

        h.Deriver.Hold = true;
        h.Vm.Tuning.BeginDrag();
        h.Vm.Tuning.RadiusMiles = 25;
        await h.Deriver.CallStartedAsync(n);
        h.Deriver.Hold = false;
        h.Vm.Tuning.RadiusMiles = 40;
        await Eventually.TrueAsync(() => h.Vm.Plan.Tuning.RadiusMiles == 40, h.Ui);

        h.Deriver.Release(n);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        h.Ui.RunAll();

        Assert.Equal(40, h.Vm.Plan.Tuning.RadiusMiles);
        Assert.Single(h.Vm.Plan.Groups);
    }

    [Fact]
    public async Task Ordering_EditQueuedBehindSlowDerive_IsValidatedAgainstThePlanWithTheEarlierEdit()
    {
        using var h = ReviewHarness.Create(TestPlans.CouncilAnvil());
        await h.SettleAsync();
        var n = h.Deriver.Calls.Count;

        h.Deriver.Hold = true;
        var first = h.Vm.SplitBeforeAsync(AnvilFirst);
        await h.Deriver.CallStartedAsync(n);
        var second = h.Vm.SplitBeforeAsync(AnvilFirst);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(second.IsCompleted);

        h.Deriver.ReleaseAll();
        await first;
        await second;
        await h.SettleAsync();

        Assert.Equal(2, h.Vm.Plan.Groups.Length);
        Assert.NotNull(h.Vm.LastError);
        Assert.Single(h.Session.ToDraft().Edits);
    }

    [Fact]
    public async Task Ordering_ChangedArrivesOffTheUiThread_AndIsAppliedOnlyThroughTheDispatcher()
    {
        using var h = ReviewHarness.Create(TestPlans.CouncilAnvil());
        await h.SettleAsync();
        var before = h.Vm.Plan;
        var mark = h.Ui.PostedWithAccess.Count;

        await Task.Run(() => h.Vm.SplitBeforeAsync(AnvilFirst), TestContext.Current.CancellationToken);
        await Eventually.TrueAsync(() => h.Ui.Pending > 0);

        Assert.Same(before, h.Vm.Plan);
        Assert.Contains(false, h.Ui.PostedWithAccess.Skip(mark));
        h.Ui.RunAll();
        Assert.Equal(2, h.Vm.Plan.Groups.Length);
    }

    [Fact]
    public async Task Ordering_PlanWithLowerRevisionPostedLate_IsIgnored()
    {
        using var h = ReviewHarness.Create(TestPlans.CouncilAnvil());
        await h.SettleAsync();
        var older = h.Vm.Plan;
        await h.Vm.SplitBeforeAsync(AnvilFirst);
        await h.SettleAsync();
        var newer = h.Vm.Plan;

        h.Services.Ui.Post(() => h.Vm.OnPlanArrived(h.Vm.Session, older));
        h.Ui.RunAll();
        Assert.Same(newer, h.Vm.Plan);
    }

    [Fact]
    public async Task Ordering_RejectedEdit_ReturnsWithoutWaitingForADerive()
    {
        using var h = ReviewHarness.Create(TestPlans.CouncilAnvil());
        await h.SettleAsync();
        var n = h.Deriver.Calls.Count;
        var before = h.Vm.Plan;

        h.Deriver.Hold = true;
        var groupStart = h.Vm.Plan.Groups[0].Videos[0];
        await h.Vm.SplitBeforeAsync(groupStart).WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Equal(n, h.Deriver.Calls.Count);
        Assert.NotNull(h.Vm.LastError);
        Assert.Same(before, h.Vm.Plan);
        Assert.False(h.Vm.CanUndo);
        h.Deriver.ReleaseAll();
    }
}
