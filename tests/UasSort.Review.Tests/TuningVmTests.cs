// tests/UasSort.Review.Tests/TuningVmTests.cs
namespace UasSort.Review.Tests;

public class TuningVmTests
{
    private sealed class Host : ITuningHost
    {
        public List<Tuning> Previews { get; } = [];
        public List<Tuning> Commits { get; } = [];
        public void Preview(Tuning t) => Previews.Add(t);
        public Task CommitTuningAsync(Tuning t) { Commits.Add(t); return Task.CompletedTask; }
    }

    private static (TuningVm Vm, Host Host, FakeTimeProvider Time, FakeUiDispatcher Ui) Make()
    {
        var host = new Host();
        var time = new FakeTimeProvider();
        var ui = new FakeUiDispatcher();
        return (new TuningVm(host, time, ui), host, time, ui);
    }

    [Fact]
    public async Task Tuning_Drag_PreviewsLiveAndCommitsOnceOnRelease()
    {
        var (vm, host, time, ui) = Make();
        vm.BeginDrag();
        vm.RadiusMiles = 40;
        vm.RadiusMiles = 35;
        vm.RadiusMiles = 30;
        time.Advance(TimeSpan.FromSeconds(2));
        ui.RunAll();

        Assert.Equal(3, host.Previews.Count);
        Assert.Empty(host.Commits);

        await vm.EndDragAsync();
        Assert.Equal(new Tuning(30, 1), Assert.Single(host.Commits));
        Assert.Equal(new Tuning(30, 1), vm.Committed);
    }

    [Fact]
    public void Tuning_KeyboardChange_CommitsAfter400MsIdle()
    {
        var (vm, host, time, ui) = Make();
        vm.RadiusMiles = 45;
        time.Advance(TimeSpan.FromMilliseconds(300));
        vm.RadiusMiles = 44;
        time.Advance(TimeSpan.FromMilliseconds(399));
        ui.RunAll();
        Assert.Empty(host.Commits);

        time.Advance(TimeSpan.FromMilliseconds(1));
        ui.RunAll();
        Assert.Equal(new Tuning(44, 1), Assert.Single(host.Commits));
        Assert.Equal(2, host.Previews.Count);
    }

    [Fact]
    public void Tuning_GapSliderUsesTheSameIdleCommit()
    {
        var (vm, host, time, ui) = Make();
        vm.GapDays = 3;
        time.Advance(TuningVm.IdleCommit);
        ui.RunAll();
        Assert.Equal(new Tuning(50, 3), Assert.Single(host.Commits));
        Assert.Equal("3 days", vm.GapText);
    }

    [Fact]
    public async Task Tuning_Reset_CommitsDefaults()
    {
        var (vm, host, time, ui) = Make();
        vm.RadiusMiles = 30;
        time.Advance(TuningVm.IdleCommit);
        ui.RunAll();
        await vm.ResetCommand.ExecuteAsync(null);

        Assert.Equal(new Tuning(50, 1), host.Commits[^1]);
        Assert.Equal("50 mi", vm.RadiusText);
    }

    [Fact]
    public void Tuning_SyncFromPlan_DoesNotPreviewOrCommit()
    {
        var (vm, host, _, _) = Make();
        vm.Sync(new Tuning(25, 2), 8);
        Assert.Empty(host.Previews);
        Assert.Equal(25, vm.RadiusMiles);
        Assert.Equal("8 groups", vm.GroupCountText);
    }

    [Fact]
    public void Tuning_ValuesAreClampedAndStepped()
    {
        var (vm, _, _, _) = Make();
        vm.RadiusMiles = 3.4;
        Assert.Equal(5, vm.RadiusMiles);
        vm.RadiusMiles = 33.6;
        Assert.Equal(34, vm.RadiusMiles);
        vm.GapDays = 9;
        Assert.Equal(7, vm.GapDays);
    }
}
