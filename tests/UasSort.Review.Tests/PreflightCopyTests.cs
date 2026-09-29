// tests/UasSort.Review.Tests/PreflightCopyTests.cs
using UasSort.Testing.Offload;
using static UasSort.Testing.Offload.OffloadPlanBuilder;

namespace UasSort.Review.Tests;

public class PreflightCopyTests
{
    private const string ZRel = @"2026\2026-09\2026-09-27 Zachar Bay";
    private const string AckText = "Appending to 'Zachar Bay': different day, 34 mi from Council Road";

    /// <summary>A real CommitSession over Part 07's offload rig: an Append group with a stale temp from an earlier run and one new
    /// video (a second one with <c>twoVideos</c>), plus one plan issue that needs an acknowledgement.</summary>
    private sealed class Rig
    {
        public Rig(bool twoVideos = false)
        {
            var b = new OffloadPlanBuilder();
            var folder = new LibraryFolderRef(b.NewFolderPath(ZRel), new DateOnly(2026, 9, 27), "Zachar Bay");
            var v = b.Video("DJI_20260927160000_0160_D.MP4", 3_000, T0.AddHours(2));
            ItemId[] videos = twoVideos ? [v, b.Video("DJI_20260927160500_0161_D.MP4", 3_000, T0.AddHours(2).AddMinutes(5))] : [v];
            b.Group(new Append(folder, Confidence.High, "same day as clips already in this folder", null), Zachar, videos);
            b.Issue(new Issue(IssueSeverity.Warning, IssueCode.MediumAppend, AckText, v, [], true));
            Offload = new OffloadRig(b).Build();
            StaleTemp = folder.FullPath + @"\DJI_20260927150000_0150_D.MP4.uas-sort.tmp";
            Offload.Fs.AddFile(StaleTemp, 10, T0, 0x22);
            Env = new CommitEnvironment(Offload.Reader, Offload.Fs, Offload.Ledger, Offload.Lock, Offload.Power, Offload.Thumbnails,
                                        Offload.Reports, new FakeVolumeProvider(Offload.Volumes), dirs => Files = new FakeFileOps(Offload.Fs, dirs),
                                        Time, FakeLayout.Machine, "0.1.0");
        }

        public OffloadRig Offload { get; }
        public string StaleTemp { get; }
        public CommitEnvironment Env { get; }
        public FakeFileOps Files { get; private set; } = null!;
        public FakeTimeProvider Time { get; } = new(new DateTimeOffset(T0.AddHours(3)));
        public FakeDraftStore Drafts { get; } = new();
        public FakeDialogService Dialogs { get; } = new();
        public FakeUiDispatcher Ui { get; } = new();
        public CardSource Source => Offload.Plan.Base.Scan.Inventory.Source;

        public PreflightVm Preflight()
            => new(Offload.Plan, Source, plan => CommitSession.Begin(plan, Env, "run-1"), new CommitPorts(Drafts, Dialogs, Ui));
    }

    [Fact]
    public void Preflight_OpenBeginsTheSession_AcknowledgementsGateStart_WritesNothing()
    {
        var rig = new Rig();
        using var vm = rig.Preflight();
        vm.Open();

        Assert.Equal((1, 1), (rig.Offload.Lock.Holds, rig.Offload.Thumbnails.Paused));
        Assert.Same(vm.Session!.Preflight, vm.Report);
        Assert.Same(vm.Session.Batch, vm.Batch);
        var ack = Assert.Single(vm.Acks);
        Assert.Equal(new AckKey(IssueCode.MediumAppend, ack.Key.Anchor, AckText), ack.Key);
        Assert.Equal(AckText, ack.Text);
        Assert.Equal(vm.Report!.Volumes.Length, vm.VolumeLines.Count);
        Assert.False(vm.CanStart);

        ack.IsChecked = true;
        Assert.True(vm.CanStart);
        ack.IsChecked = false;
        Assert.False(vm.CanStart);
        Assert.Equal<string>(["Check"], rig.Offload.Ledger.Calls);
        Assert.True(rig.Offload.Fs.Exists(rig.StaleTemp));
    }

    [Fact]
    public void Preflight_VolumeLine_ShowsFilesSizeAndFreeAfter()
        => Assert.Equal("C: · 3 files · 3.6 GB · 317 GB free → 313.4 GB after",
                        PreflightVm.VolumeLine(new VolumeNeed("C:", 3, 3_600_000_000, 317_000_000_000, 4_673_741_824)));

    [Fact]
    public void Preflight_Back_DisposesTheSession_DeletesNothingAndReleasesTheLockAndPause()
    {
        var rig = new Rig();
        var vm = rig.Preflight();
        vm.Open();
        var back = false;
        vm.BackRequested += () => back = true;

        vm.BackCommand.Execute(null);

        Assert.True(back);
        Assert.Equal((0, 0), (rig.Offload.Lock.Holds, rig.Offload.Thumbnails.Paused));
        Assert.True(rig.Offload.Fs.Exists(rig.StaleTemp));
        Assert.DoesNotContain("EnsureFolder", rig.Offload.Ledger.Calls);
    }

    [Fact]
    public void Preflight_LockHeldElsewhere_BlocksWithMessage()
    {
        var rig = new Rig();
        rig.Offload.Lock.HeldElsewhere = true;
        using var vm = rig.Preflight();
        vm.Open();

        var held = Assert.Single(vm.Report!.Issues, i => i.Code == IssueCode.OffloadLockHeld);
        Assert.Equal(held.Message, vm.LockMessage);
        Assert.Contains(held.Message, vm.Blocking);
        vm.Acks[0].IsChecked = true;
        Assert.False(vm.CanStart);
    }

    [Fact]
    public async Task Copy_Start_RunsTheSession_ReportsProgress_DeletesDraftWhenFailureFree()
    {
        var rig = new Rig();
        using var preflight = rig.Preflight();
        preflight.Open();
        preflight.Acks[0].IsChecked = true;
        rig.Drafts.Save(rig.Source.DraftKey, new Draft(1, rig.Source.DraftKey, "h", DateTime.UnixEpoch, new Tuning(), []));
        using var copy = new CopyVm(preflight, rig.Dialogs, rig.Ui);

        var result = await copy.RunAsync();
        rig.Ui.RunAll();

        Assert.NotNull(result);
        Assert.True(result.FailureFree);
        Assert.Equal<string>(["Check", "EnsureFolder", "SnapshotToBackup run-1", "OpenOwn"], rig.Offload.Ledger.Calls);
        Assert.False(rig.Offload.Fs.Exists(rig.StaleTemp));
        Assert.Equal<string>(["Offloading drone media"], rig.Offload.Power.Reasons);
        Assert.Equal(1, rig.Offload.Thumbnails.Paused);                 // the session keeps the pause until the verdict is shown
        Assert.Same(rig.Offload.Reports.Offload.Single(), result.Report);
        Assert.Contains(rig.Source.DraftKey, rig.Drafts.Deleted);
        Assert.NotEqual("", copy.PhaseText);
        Assert.False(copy.IsRunning);
        Assert.Null(copy.ErrorText);

        preflight.Dispose();
        Assert.Equal((0, 0), (rig.Offload.Lock.Holds, rig.Offload.Thumbnails.Paused));
    }

    [Fact]
    public void Copy_Apply_FormatsTheProgress()
    {
        var rig = new Rig();
        using var preflight = rig.Preflight();
        using var copy = new CopyVm(preflight, rig.Dialogs, rig.Ui);

        copy.Apply(new OffloadProgress(1, 3, 1_200_000_000, 3_600_000_000, 85.4, TimeSpan.FromMinutes(3), "DJI_20260927140627_0128_D.MP4", CopyPhase.Verify, null));

        Assert.Equal("1 / 3 files", copy.FilesText);
        Assert.Equal("1.2 GB of 3.6 GB", copy.BytesText);
        Assert.Equal("85 MB/s", copy.SpeedText);
        Assert.Equal("3 min left", copy.EtaText);
        Assert.Equal("Verifying", copy.PhaseText);
        Assert.Equal("DJI_20260927140627_0128_D.MP4", copy.CurrentText);
        Assert.Equal(1.0 / 3, copy.Fraction, 6);
    }

    [Fact]
    public async Task Copy_LedgerFolderCannotBeCreated_CopiesNothingAndSaysSo()
    {
        var rig = new Rig();
        rig.Offload.Ledger.EnsureFolderThrows = true;
        rig.Drafts.Save(rig.Source.DraftKey, new Draft(1, rig.Source.DraftKey, "h", DateTime.UnixEpoch, new Tuning(), []));
        using var preflight = rig.Preflight();
        preflight.Open();
        preflight.Acks[0].IsChecked = true;
        using var copy = new CopyVm(preflight, rig.Dialogs, rig.Ui);

        var result = await copy.RunAsync();

        Assert.Equal(StopReason.LedgerWriteFailed, result!.Offload.Stop);
        Assert.All(result.Offload.Outcomes, o => Assert.IsType<NotStarted>(o));
        Assert.True(rig.Offload.Fs.Exists(rig.StaleTemp));
        Assert.DoesNotContain(rig.Source.DraftKey, rig.Drafts.Deleted);
        Assert.StartsWith("Couldn't create or open the history file", copy.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Copy_Cancel_AsksThenStopsTheRun()
    {
        var rig = new Rig(twoVideos: true);
        using var preflight = rig.Preflight();
        preflight.Open();
        preflight.Acks[0].IsChecked = true;
        var ct = TestContext.Current.CancellationToken;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim(false);
        rig.Files.OnTempWrite = (_, _) => { started.TrySetResult(); release.Wait(ct); };
        using var copy = new CopyVm(preflight, rig.Dialogs, rig.Ui);

        var run = Task.Run(copy.RunAsync, ct);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        rig.Dialogs.Answers.Enqueue(DialogResult.Close);
        await copy.CancelCommand.ExecuteAsync(null);
        Assert.False(run.IsCompleted);
        rig.Dialogs.Answers.Enqueue(DialogResult.Primary);
        await copy.CancelCommand.ExecuteAsync(null);
        release.Set();

        var result = await run;
        Assert.Equal(StopReason.Cancelled, result!.Offload.Stop);
        Assert.False(result.FailureFree);
        Assert.Equal("Stop the offload?", rig.Dialogs.Shown[0].Title);
        Assert.DoesNotContain(rig.Source.DraftKey, rig.Drafts.Deleted);
    }
}
