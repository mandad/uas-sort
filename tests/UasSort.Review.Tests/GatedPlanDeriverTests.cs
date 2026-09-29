// tests/UasSort.Review.Tests/GatedPlanDeriverTests.cs
namespace UasSort.Review.Tests;

public class GatedPlanDeriverTests
{
    [Fact]
    public async Task GatedPlanDeriver_Part06ArmAndCallCount_StillWork()
    {
        var b = TestPlans.Base(TestPlans.CouncilAnvil());
        var gated = new GatedPlanDeriver(new ScriptedDeriver());
        var ct = TestContext.Current.CancellationToken;
        var gate = gated.Arm((t, _) => t.RadiusMiles == 30);

        var derive = Task.Run(() => gated.Derive(b, new Tuning(30, 1), [], new SessionFlags(false), 1, ct), ct);
        await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10), ct);
        Assert.False(derive.IsCompleted);
        gate.Release();
        await derive;

        gated.Derive(b, new Tuning(), [], new SessionFlags(false), 2, ct);
        Assert.Equal(2, gated.CallCount);
        Assert.Equal(2, gated.Calls.Count);
        Assert.All(gated.Calls, c => Assert.True(c.Completed));
    }

    [Fact]
    public async Task GatedPlanDeriver_HeldCall_BlocksUntilReleased()
    {
        var b = TestPlans.Base(TestPlans.CouncilAnvil());
        var gated = new GatedPlanDeriver(new ScriptedDeriver()) { Hold = true };
        var ct = TestContext.Current.CancellationToken;

        var derive = Task.Run(() => gated.Derive(b, new Tuning(), [], new SessionFlags(false), 1, ct), ct);
        var call = await gated.CallStartedAsync(0);

        Assert.True(call.Held);
        Assert.False(derive.IsCompleted);
        gated.Release(0);
        var plan = await derive;
        Assert.Equal(1, plan.Revision);
        Assert.True(gated.Calls[0].Completed);
    }

    [Fact]
    public async Task GatedPlanDeriver_CancelledWhileHeld_ThrowsAndIsMarked()
    {
        var b = TestPlans.Base(TestPlans.CouncilAnvil());
        var gated = new GatedPlanDeriver(new ScriptedDeriver()) { Hold = true };
        using var cts = new CancellationTokenSource();

        var derive = Task.Run(() => gated.Derive(b, new Tuning(), [], new SessionFlags(false), 1, cts.Token), TestContext.Current.CancellationToken);
        await gated.CallStartedAsync(0);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => derive);
        Assert.True(gated.Calls[0].Cancelled);
    }

    [Fact]
    public void ScriptedDeriver_CouncilAnvilAt50Miles_IsOneGroupWithEmphasisedSplit()
    {
        var b = TestPlans.Base(TestPlans.CouncilAnvil());
        var plan = new ScriptedDeriver().Derive(b, new Tuning(50, 1), [], new SessionFlags(false), 1, TestContext.Current.CancellationToken);

        var g = Assert.Single(plan.Groups);
        Assert.Equal(4, g.Videos.Length);
        var split = Assert.Single(g.DaySplits);
        Assert.True(split.Emphasised);
        Assert.Contains(plan.Issues, i => i.Code == IssueCode.EmphasisedDaySplit && i.RequiresAckAtPreflight);
    }

    [Fact]
    public void ScriptedDeriver_At25Miles_SplitsByDistance()
    {
        var b = TestPlans.Base(TestPlans.CouncilAnvil());
        var plan = new ScriptedDeriver().Derive(b, new Tuning(25, 1), [], new SessionFlags(false), 2, TestContext.Current.CancellationToken);

        Assert.Equal(2, plan.Groups.Length);
        Assert.Equal(BoundaryCause.Distance, Assert.Single(plan.Boundaries).Cause);
    }
}
