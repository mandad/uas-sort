// tests/UasSort.Review.Tests/CleanupVmTests.cs
using System.Reflection;

namespace UasSort.Review.Tests;

public class CleanupVmTests
{
    private sealed class Rig
    {
        public FakeDialogService Dialogs { get; } = new();
        public FakeUiDispatcher Ui { get; } = new();
        public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 9, 28, 3, 0, 0, TimeSpan.Zero));
        public CleanupPreparation Preparation { get; set; } = new(CleanupFixture.Inputs(), null, false);
        public ConfirmedCleanupPlan? Confirmed { get; private set; }
        public CleanupReport? Report { get; private set; }
        public bool RescanThrows { get; set; }
        public bool RescanCancelled { get; set; }
        public Exception? RunFault { get; set; }
        public Exception? SaveFault { get; set; }
        public FakeEjectOk Eject { get; } = new();

        public Rig() => Time.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/Anchorage"));

        public CleanupVm Vm()
        {
            var engine = new CleanupEngine(
                () => Preparation,
                (confirmed, progress, ct) =>
                {
                    if (RunFault is { } fault) return Task.FromException<CleanupResult>(fault);
                    Confirmed = confirmed;
                    progress.Report(new CleanupProgress(1, confirmed.Plan.FileCount, CleanupFixture.S, confirmed.Plan.AllocatedBytes, "DJI_x.MP4", null));
                    ImmutableArray<CleanupOutcome> outcomes = [.. confirmed.Plan.Delete.Select(c => (CleanupOutcome)new Deleted(c.Unit, c.Files.Length, c.AllocatedBytes, false))];
                    return Task.FromResult(new CleanupResult("cleanup01", confirmed, outcomes, null,
                        new CardSpace(CleanupFixture.Space.FreeBytes + confirmed.Plan.AllocatedBytes, CleanupFixture.Space.TotalBytes, 131_072), [],
                        TestPlans.Utc(2026, 9, 28, 3, 0), TestPlans.Utc(2026, 9, 28, 3, 1)));
                },
                () => RescanCancelled ? Task.FromCanceled<VerdictLevel>(new CancellationToken(true))
                    : RescanThrows ? Task.FromException<VerdictLevel>(new IOException("card removed")) : Task.FromResult(VerdictLevel.Safe),
                r =>
                {
                    if (SaveFault is { } fault) throw fault;
                    Report = r;
                    return @"C:\AppData\uas-sort\reports\20260928-030100-cleanup0-cleanup.json";
                },
                Eject);
            var vm = new CleanupVm(engine, Dialogs, Ui, Time);
            vm.Open();
            return vm;
        }
    }

    private static readonly DateTimeOffset Jul26LateEveningAlaska = new(2026, 7, 26, 23, 30, 0, TimeSpan.FromHours(-8));

    [Fact]
    public void Cleanup_ContinueDisabledUntilADateIsPicked_PickerDayIsTheCutoffDay()
    {
        var vm = new Rig().Vm();
        Assert.Equal(CleanupMode.BeforeDate, vm.Mode);
        Assert.False(vm.ContinueCommand.CanExecute(null));
        Assert.Equal("E: · SD card · exFAT · 256.1 GB · serial 1A2B-3C4D", vm.CardSummary);

        vm.PickDate(Jul26LateEveningAlaska);
        Assert.Equal(new DateOnly(2026, 7, 26), vm.Plan!.Request.Before);
        Assert.True(vm.ContinueCommand.CanExecute(null));

        vm.PickDate(new DateTimeOffset(2026, 7, 26, 0, 30, 0, TimeSpan.Zero));
        Assert.Equal(new DateOnly(2026, 7, 26), vm.Plan!.Request.Before);
        Assert.Equal(4, vm.Plan.Delete.Length);
        Assert.Equal(CleanupTexts.CutoffLine(vm.Plan), vm.CutoffText);          // Part 08 owns (and tests) the wording
        Assert.Equal(CleanupTexts.EvidenceSplit(vm.Plan), vm.EvidenceText);
        Assert.Equal(CleanupTexts.NeverCopiesLine(vm.Plan), vm.NeverCopiedText);
        Assert.Equal("Delete 4 files (4.8 GB)", vm.DeleteButtonText);
        Assert.Equal("4 videos", vm.CountsText);
        Assert.Single(vm.Kept);
    }

    [Fact]
    public void Cleanup_SwitchOn_ReviewListRequired_TickedRowsStartOnKeep_DeleteAllLeavesThem()
    {
        var vm = new Rig().Vm();
        vm.PickDate(Jul26LateEveningAlaska);
        vm.IncludeNotInLibrary = true;
        vm.ContinueCommand.Execute(null);
        Assert.Equal(CleanupStep.Review, vm.Step);

        Assert.Equal(2, vm.Rows.Count);
        var a = vm.Rows.Single(r => r.Unit == TestPlans.Id(CleanupFixture.NewA));
        var b = vm.Rows.Single(r => r.Unit == TestPlans.Id(CleanupFixture.NewB));
        Assert.Equal((RowDecision.Keep, "ticked for offload"), (a.Decision, a.Badge));
        Assert.Equal(RowDecision.Delete, b.Decision);

        vm.DeleteAllCommand.Execute(null);
        Assert.Equal(RowDecision.Keep, vm.Rows.Single(r => r.Unit == a.Unit).Decision);
        vm.ContinueCommand.Execute(null);
        Assert.Equal(CleanupStep.Confirm, vm.Step);

        Assert.True(vm.ShowNotInLibraryAck);
        Assert.Equal("Includes 1 file not proven to be in your library.", vm.NotInLibraryAckText);
        Assert.Equal(5, vm.Plan!.FileCount);
        Assert.Equal(CleanupTexts.CutoffLine(vm.Plan), vm.CutoffText);
        Assert.Equal("Delete 5 files (6 GB)", vm.DeleteButtonText);

        vm.Rows.Single(r => r.Unit == a.Unit).DeleteCommand.Execute(null);
        Assert.Equal("Delete 6 files (7.2 GB)", vm.DeleteButtonText);
        Assert.Contains(vm.Plan!.Delete, c => c.Unit == a.Unit);
        Assert.Equal(CleanupTexts.CutoffLine(vm.Plan), vm.CutoffText);
    }

    [Fact]
    public void Cleanup_RowsThatJoinLater_AreUndecidedAndBlockDelete()
    {
        var vm = new Rig().Vm();
        vm.PickDate(Jul26LateEveningAlaska);
        vm.IncludeNotInLibrary = true;
        vm.ContinueCommand.Execute(null);

        vm.PickDate(new DateTimeOffset(2026, 7, 29, 12, 0, 0, TimeSpan.FromHours(-8)));
        var c = vm.Rows.Single(r => r.Unit == TestPlans.Id(CleanupFixture.NewC));
        Assert.Equal((RowDecision.Undecided, "new in range"), (c.Decision, c.Badge));
        Assert.Same(c, vm.FirstUndecided);
        Assert.Equal("Decide 1 new row", vm.DeleteButtonText);
        Assert.False(vm.ContinueCommand.CanExecute(null));

        vm.DeleteAllCommand.Execute(null);
        Assert.Equal(RowDecision.Delete, vm.Rows.Single(r => r.Unit == c.Unit).Decision);
        Assert.Equal("Delete 8 files (9.6 GB)", vm.DeleteButtonText);
        Assert.True(vm.ContinueCommand.CanExecute(null));
    }

    [Fact]
    public void Cleanup_AcknowledgementsGateDelete_AndClearOnAnyFingerprintChange()
    {
        var vm = new Rig().Vm();
        vm.PickDate(Jul26LateEveningAlaska);
        vm.ContinueCommand.Execute(null);
        Assert.Equal(CleanupStep.Confirm, vm.Step);
        Assert.False(vm.CanDelete);

        vm.AckCantBeRecovered = true;
        Assert.True(vm.CanDelete);

        vm.PickDate(new DateTimeOffset(2026, 7, 29, 12, 0, 0, TimeSpan.FromHours(-8)));
        Assert.False(vm.AckCantBeRecovered);
        Assert.False(vm.CanDelete);
    }

    [Fact]
    public void Cleanup_FreeSpaceModes_TargetsTextsAndShortfall()
    {
        var vm = new Rig().Vm();
        vm.Mode = CleanupMode.FreeSpace;
        Assert.Equal(FreeSpaceKind.HaveFree, vm.FreeKind);
        Assert.Equal("E: 12.4 GB free of 256.1 GB", vm.FreeNowText);
        Assert.False(vm.ContinueCommand.CanExecute(null));

        vm.GbValue = 15;
        Assert.Equal("will delete ≈ 3.6 GB = free up ≈ 3.6 GB", vm.WillDeleteText);
        Assert.Equal(3, vm.Plan!.FileCount);
        Assert.Equal(CleanupTexts.CutoffLine(vm.Plan), vm.CutoffText);

        vm.FreeKind = FreeSpaceKind.FreeUp;
        vm.GbValue = 2;
        Assert.Equal("will delete ≈ 2.4 GB", vm.WillDeleteText);

        vm.FreeKind = FreeSpaceKind.HaveFree;
        vm.GbValue = 10;
        Assert.Empty(vm.Plan!.Delete);
        Assert.NotNull(CleanupTexts.NothingToDelete(vm.Plan));
        Assert.Equal(CleanupTexts.NothingToDelete(vm.Plan), vm.CutoffText);
        Assert.False(vm.CanDelete);

        vm.GbValue = 20;
        Assert.NotNull(vm.Plan!.Shortfall);
        Assert.Equal(CleanupTexts.ShortfallLine(vm.Plan), vm.ShortfallText);
        Assert.True(vm.CanIncludeNotInLibraryFix);
        vm.IncludeNotInLibraryCommand.Execute(null);
        Assert.True(vm.IncludeNotInLibrary);
    }

    [Fact]
    public void Cleanup_PreparationBlocking_DisablesEverythingAndOffersRescan()
    {
        var rig = new Rig { Preparation = new CleanupPreparation(null, "A different card is in E:; rescan", true) };
        var vm = rig.Vm();
        var rescan = false;
        vm.RescanRequested += () => rescan = true;

        Assert.Equal("A different card is in E:; rescan", vm.BlockingText);
        vm.PickDate(Jul26LateEveningAlaska);
        Assert.False(vm.ContinueCommand.CanExecute(null));
        Assert.False(vm.CanDelete);
        Assert.True(vm.CanRescan);
        vm.RescanCommand.Execute(null);
        Assert.True(rescan);
    }

    [Fact]
    public async Task Cleanup_Delete_ConfirmsRunsRescansAndSavesTheReport()
    {
        var rig = new Rig();
        var vm = rig.Vm();
        vm.PickDate(Jul26LateEveningAlaska);
        vm.ContinueCommand.Execute(null);
        vm.AckCantBeRecovered = true;

        await vm.DeleteCommand.ExecuteAsync(null);
        rig.Ui.RunAll();

        Assert.Equal(4, rig.Confirmed!.FilePaths.Count);
        Assert.Equal(CleanupStep.Result, vm.Step);
        var report = rig.Report!;
        Assert.Equal(4, report.Files.Length);
        Assert.All(report.Files, l => Assert.True(l.LedgerRecorded));
        Assert.All(report.Files, l => Assert.Equal("deleted", l.Outcome));
        Assert.Equal(12_400_000_000, report.FreeBefore);
        Assert.Equal(VerdictLevel.Safe, report.VerdictAfter);
        Assert.Equal("Deleted 4 files (4.8 GB) · E: now has 17.2 GB free", vm.Result!.HeadlineText);
    }

    [Fact]
    public async Task Cleanup_RescanFails_ReportStillWrittenWithNotSafe()
    {
        var rig = new Rig { RescanThrows = true };
        var vm = rig.Vm();
        vm.PickDate(Jul26LateEveningAlaska);
        vm.ContinueCommand.Execute(null);
        vm.AckCantBeRecovered = true;

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(VerdictLevel.NotSafe, rig.Report!.VerdictAfter);
        Assert.Equal("Don't format yet", vm.Result!.VerdictText);
    }

    private static CleanupVm ReadyToDelete(Rig rig)
    {
        var vm = rig.Vm();
        vm.PickDate(Jul26LateEveningAlaska);
        vm.ContinueCommand.Execute(null);
        vm.AckCantBeRecovered = true;
        return vm;
    }

    [Fact]
    public async Task Cleanup_RescanCancelled_ReportStillWrittenWithNotSafe()
    {
        var rig = new Rig { RescanCancelled = true };
        var vm = ReadyToDelete(rig);

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(CleanupStep.Result, vm.Step);
        Assert.Equal(VerdictLevel.NotSafe, rig.Report!.VerdictAfter);
        Assert.Equal("Don't format yet", vm.Result!.VerdictText);
    }

    [Fact]
    public async Task Cleanup_SaveReportFails_StillReachesResult_ErrorShown()
    {
        var rig = new Rig { SaveFault = new IOException("disk full") };
        var vm = ReadyToDelete(rig);

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(CleanupStep.Result, vm.Step);
        Assert.NotNull(vm.Result);
        Assert.Equal("The cleanup report couldn't be saved: disk full", vm.BlockingText);
        Assert.True(vm.DoneCommand.CanExecute(null));
    }

    [Fact]
    public async Task Cleanup_RunFails_LeavesDeleting_OffersRescanAndBack_NoReport()
    {
        var rig = new Rig { RunFault = new IOException("card gone") };
        var vm = ReadyToDelete(rig);

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(CleanupStep.Choose, vm.Step);
        Assert.Equal("The cleanup stopped unexpectedly: card gone. Some files may already be deleted; Rescan to see what is on the card.",
                     vm.BlockingText);
        Assert.True(vm.CanRescan);
        Assert.True(vm.RescanCommand.CanExecute(null));
        Assert.True(vm.BackCommand.CanExecute(null));
        Assert.False(vm.CanDelete);
        Assert.False(vm.CanContinue);
        Assert.Null(rig.Report);
        Assert.Null(vm.Result);
    }

    [Fact]
    public void Cleanup_PlanTypes_HaveNoPublicConstructorAndNoWithClone()
    {
        foreach (var t in new[] { typeof(CleanupPlan), typeof(ConfirmedCleanupPlan) })
        {
            Assert.Empty(t.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
            Assert.Null(t.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.Instance));
            Assert.True(t.IsSealed);
        }
    }
}
