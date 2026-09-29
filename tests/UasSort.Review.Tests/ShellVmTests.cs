// tests/UasSort.Review.Tests/ShellVmTests.cs
using UasSort.Testing.Offload;
using static UasSort.Testing.Offload.OffloadPlanBuilder;

namespace UasSort.Review.Tests;

public class ShellVmTests
{
    private sealed class Volumes(IReadOnlyList<VolumeInfo> v) : IVolumeProvider
    {
        public IReadOnlyList<VolumeInfo> GetVolumes() => v;
    }

    private sealed class DeleteFails : IDraftStore
    {
        public Draft? Load(string cardKey) => null;
        public void Save(string cardKey, Draft d) { }
        public void Delete(string cardKey) => throw new IOException("drafts folder is locked");
    }

    private sealed class Rig
    {
        public Rig()
        {
            var b = new OffloadPlanBuilder();
            var v = b.Video("DJI_20260927140000_0123_D.MP4", 5_000, T0);
            b.Group(new NewFolder(@"2026\2026-09\2026-09-27 Zachar Bay"), Zachar, v);
            Offload = new OffloadRig(b).Build();
            Commit = new CommitEnvironment(Offload.Reader, Offload.Fs, Offload.Ledger, Offload.Lock, Offload.Power, Offload.Thumbnails,
                                           Offload.Reports, new FakeVolumeProvider(Offload.Volumes), dirs => new FakeFileOps(Offload.Fs, dirs),
                                           new FakeTimeProvider(new DateTimeOffset(T0.AddHours(1))), FakeLayout.Machine, "0.1.0");
        }

        public FakeUiDispatcher Ui { get; } = new();
        public FakeLedgerStore Ledger { get; } = Fake.Ledger();
        /// <summary>The Commit runs over Part 07's offload rig; the shell only needs a real session to hold and release the lock.</summary>
        public OffloadRig Offload { get; }
        public CommitEnvironment Commit { get; }
        public List<VolumeInfo> Cards { get; } = [CleanupFixture.Volume];
        public bool WriteProtected { get; set; }
        public int Scans { get; private set; }
        public List<Settings> Saved { get; } = [];
        public PlanBase Base { get; } = TestPlans.Base(TestPlans.Zachar());
        public IDraftStore CommitDrafts { get; set; } = new FakeDraftStore();
        public List<(CleanupOrigin Origin, OffloadResult? Offload)> CleanupOpens { get; } = [];

        public ShellVm Shell(bool rootsConfirmed = true)
        {
            var services = Fake.Services(Ui);
            var load = new SettingsLoad(TestPlans.Settings(rootsConfirmed), false, null, null);
            var store = new FakeSettingsStore(load);
            var verdict = new FormatVerdict(VerdictLevel.Safe, TestPlans.Card, "E: · DJI Air 3S · serial 1A2B-3C4D: Safe to format: 3 verified",
                                            ImmutableDictionary<AuditCategory, int>.Empty, 0, 0, [], [], null);
            ShellVm? shell = null;
            shell = new ShellVm(new ShellDeps(
                load,
                l => new SetupVm(l, store, _ => Ledger, new FakeFreeSpace()),
                () => new CardStageVm(new Volumes(Cards), vs => [.. vs.Select(v => new CardCandidate(v, true, null, 214))],
                                      (path, v) => new SourceOk(new CardSource(path, v?.Identity, v is null, WriteProtected))),
                s => new ScanStageVm((src, p, ct) => { Scans++; return Task.FromResult(Base.Scan); }, _ => Base, s, Ui),
                (src, b) => new ReviewVm(new PlanSession(b, new ScriptedDeriver([new Suggestion("Zachar Bay", DescSource.Feature, null, null)]), new Tuning(), services.Time),
                                         services, new LedgerDecisionService(Ledger, services.Time, "PC1")),
                r => new PreflightVm(r.Plan, shell!.Source!, _ => CommitSession.Begin(Offload.Plan, Commit, "run-1"),
                                     new CommitPorts(CommitDrafts, new FakeDialogService(), Ui)),
                p => new CopyVm(p, new FakeDialogService(), Ui),
                (r, res) => new VerdictVm(res?.Verdict ?? verdict, r.Plan, res?.Offload, res?.ReportPath,
                                          new VerdictPorts(Ledger, "PC1", services.Time, new FakeDialogService(), new FakeShellLauncher(), new FakeEjectOk(),
                                                           () => res?.Verdict ?? verdict)),
                (origin, res) => Record(origin, res, new CleanupVm(new CleanupEngine(() => new CleanupPreparation(null, "A different card is in E:; rescan", true),
                                                                 (c, p, ct) => throw new InvalidOperationException("not reached"),
                                                                 () => shell!.RescanForCleanupAsync(), r => "r.json", new FakeEjectOk()),
                                               new FakeDialogService(), Ui, services.Time)),
                s => new SettingsPageVm(s, store, _ => Ledger, FakeLayout.NewFileSystem(), new FakeShellLauncher(), new FakeDialogService(), new FakeFreeSpace(),
                                        new FakeTimeProvider(), Ui, () => TestPlans.Ledger(), r => @"C:\AppData\backup"),
                r => verdict,
                _ => true,
                _ => null,
                Saved.Add));
            return shell;
        }

        private CleanupVm Record(CleanupOrigin origin, OffloadResult? offload, CleanupVm vm)
        {
            CleanupOpens.Add((origin, offload));
            return vm;
        }
    }

    [Fact]
    public async Task Shell_RootsUnconfirmed_SetupThenCardAutoScanToReview()
    {
        var rig = new Rig();
        var shell = rig.Shell(rootsConfirmed: false);
        await shell.StartAsync();
        Assert.Equal(Stage.Setup, shell.Stage);
        Assert.False(shell.CanOpenSettings);

        ((SetupVm)shell.Current!).ConfirmCommand.Execute(null);
        await Eventually.TrueAsync(() => shell.Stage == Stage.Review, rig.Ui);

        Assert.True(shell.Settings.RootsConfirmed);
        Assert.Equal(1, rig.Scans);
        Assert.StartsWith(@"E:\ · DJI Air 3S · serial 1A2B-3C4D", shell.CardChipText, StringComparison.Ordinal);
        Assert.True(shell.CanRescan);
        Assert.True(shell.CanUndoRedo);
        Assert.True(shell.CleanupEnabled);
    }

    [Fact]
    public async Task Shell_TwoDjiVolumes_StaysOnCardStage()
    {
        var rig = new Rig();
        rig.Cards.Add(CleanupFixture.Volume with { Root = @"F:\", Identity = new CardIdentity(0x5E6F7A8B, "DJI Internal", "exFAT", 64_000_000_000) });
        var shell = rig.Shell();
        await shell.StartAsync();

        Assert.Equal(Stage.Card, shell.Stage);
        Assert.Equal(2, shell.Card!.Rows.Count);
        Assert.True(shell.CanBrowse);
        Assert.Equal(0, rig.Scans);
    }

    [Fact]
    public async Task Shell_CommitDisablesRescanSettingsBrowseAndCleanup_BackRestores()
    {
        var rig = new Rig();
        var shell = rig.Shell();
        await shell.StartAsync();
        await Eventually.TrueAsync(() => shell.Stage == Stage.Review, rig.Ui);

        shell.BeginOffload();
        Assert.Equal(Stage.Preflight, shell.Stage);
        Assert.Equal((1, 1), (rig.Offload.Lock.Holds, rig.Offload.Thumbnails.Paused));
        Assert.False(shell.CanRescan);
        Assert.False(shell.CanOpenSettings);
        Assert.False(shell.CanBrowse);
        Assert.False(shell.CanUndoRedo);
        Assert.Equal((false, "Wait until the offload finishes"), (shell.CleanupEnabled, shell.CleanupTooltip));
        await shell.RescanAsync();
        Assert.Equal(1, rig.Scans);

        shell.Preflight!.BackCommand.Execute(null);
        Assert.Equal(Stage.Review, shell.Stage);
        Assert.Equal((0, 0), (rig.Offload.Lock.Holds, rig.Offload.Thumbnails.Paused));
        Assert.True(shell.CanRescan);
    }

    [Fact]
    public async Task Shell_OffloadToVerdict_ReleasesLock_DoneReturnsToCard()
    {
        var rig = new Rig();
        var shell = rig.Shell();
        await shell.StartAsync();
        await Eventually.TrueAsync(() => shell.Stage == Stage.Review, rig.Ui);

        shell.BeginOffload();
        await shell.StartCopyAsync();

        Assert.Equal(Stage.Verdict, shell.Stage);
        Assert.Equal((0, 0), (rig.Offload.Lock.Holds, rig.Offload.Thumbnails.Paused));   // the shell disposed the session
        Assert.Single(rig.Offload.Reports.Offload);
        Assert.True(shell.Verdict!.CleanupCommand.CanExecute(null));
        Assert.Null(shell.Verdict.StopText);                        // a finished run has nothing to explain
        rig.Cards.Clear();
        shell.Verdict.DoneCommand.Execute(null);
        Assert.Equal(Stage.Card, shell.Stage);
    }

    [Fact]
    public async Task Shell_WriteProtectedCard_DisablesCleanupWithTooltip()
    {
        var rig = new Rig { WriteProtected = true };
        var shell = rig.Shell();
        await shell.StartAsync();
        await Eventually.TrueAsync(() => shell.Stage == Stage.Review, rig.Ui);

        Assert.Equal((false, "The card is write-protected (lock switch)"), (shell.CleanupEnabled, shell.CleanupTooltip));
        Assert.False(shell.CleanupCommand.CanExecute(null));
    }

    [Fact]
    public async Task Shell_CleanupRescansAndDoneReturnsToReviewOfTheRescannedCard()
    {
        var rig = new Rig();
        var shell = rig.Shell();
        await shell.StartAsync();
        await Eventually.TrueAsync(() => shell.Stage == Stage.Review, rig.Ui);
        var before = shell.Review;

        shell.OpenCleanup(CleanupOrigin.Review);
        Assert.Equal(Stage.Cleanup, shell.Stage);
        Assert.False(shell.CanRescan);
        Assert.False(shell.CanUndoRedo);

        Assert.Equal(VerdictLevel.Safe, await shell.RescanForCleanupAsync());
        Assert.Equal(Stage.Cleanup, shell.Stage);
        Assert.NotSame(before, shell.Review);
        Assert.Equal(2, rig.Scans);

        shell.Cleanup!.BackCommand.Execute(null);
        Assert.Equal(Stage.Review, shell.Stage);
        Assert.Same(shell.Review, shell.Current);
    }

    [Fact]
    public async Task Shell_SettingsOpensFromReviewAndReturns()
    {
        var rig = new Rig();
        var shell = rig.Shell();
        await shell.StartAsync();
        await Eventually.TrueAsync(() => shell.Stage == Stage.Review, rig.Ui);

        shell.OpenSettings();
        Assert.Equal(Stage.Settings, shell.Stage);
        shell.CloseSettings();
        Assert.Equal(Stage.Review, shell.Stage);
    }

    [Fact]
    public async Task Shell_UpdateLayout_SavesTheLayoutAndKeepsTheSettings()
    {
        var rig = new Rig();
        var shell = rig.Shell();
        await shell.StartAsync();

        shell.UpdateLayout(512, 0.55);

        Assert.Equal(new LayoutSettings(512, 0.55), Assert.Single(rig.Saved).Layout);
        Assert.Equal(new LayoutSettings(512, 0.55), shell.Settings.Layout);
        Assert.Equal(TestPlans.VideoRoot, shell.Settings.VideoRoot);
    }

    [Fact]
    public async Task Shell_CopyFailsWithoutAResult_StillShowsTheVerdictAndReleasesTheLock()
    {
        var rig = new Rig { CommitDrafts = new DeleteFails() };
        var shell = rig.Shell();
        await shell.StartAsync();
        await Eventually.TrueAsync(() => shell.Stage == Stage.Review, rig.Ui);

        shell.BeginOffload();
        await shell.StartCopyAsync();

        Assert.StartsWith("The offload stopped before it could finish", shell.Copy!.ErrorText, StringComparison.Ordinal);
        Assert.Equal(Stage.Verdict, shell.Stage);
        Assert.NotNull(shell.Verdict);
        Assert.Equal(shell.Copy.ErrorText, shell.Verdict.StopText);   // the Verdict page says why the run stopped
        Assert.Null(shell.Preflight);
        Assert.Equal((0, 0), (rig.Offload.Lock.Holds, rig.Offload.Thumbnails.Paused));
        Assert.True(shell.CleanupEnabled);
    }

    [Fact]
    public async Task Shell_ShowPlanFromVerdict_IsReadOnly_KeepsVerdictCleanup_AndReturnsToTheSameVerdict()
    {
        var rig = new Rig();
        var shell = rig.Shell();
        await shell.StartAsync();
        await Eventually.TrueAsync(() => shell.Stage == Stage.Review, rig.Ui);
        shell.BeginOffload();
        await shell.StartCopyAsync();
        var verdict = shell.Verdict!;
        var review = shell.Review!;

        verdict.ShowPlanCommand.Execute(null);
        Assert.Equal(Stage.Review, shell.Stage);
        Assert.True(review.IsReadOnly);
        Assert.False(shell.CanRescan);
        Assert.False(shell.CanOpenSettings);
        Assert.False(shell.CanUndoRedo);
        Assert.True(review.ShowVerdictCommand.CanExecute(null));
        await shell.RescanAsync();
        Assert.Equal((Stage.Review, 1), (shell.Stage, rig.Scans));

        shell.CleanupCommand.Execute(null);
        var (origin, offload) = Assert.Single(rig.CleanupOpens);
        Assert.Equal(CleanupOrigin.Verdict, origin);
        Assert.NotNull(offload);
        shell.Cleanup!.BackCommand.Execute(null);
        Assert.Equal(Stage.Verdict, shell.Stage);
        Assert.Same(verdict, shell.Current);

        verdict.ShowPlanCommand.Execute(null);
        review.ShowVerdictCommand.Execute(null);
        Assert.Equal(Stage.Verdict, shell.Stage);
        Assert.Same(verdict, shell.Current);
        Assert.Same(verdict, shell.Verdict);

        rig.Cards.Clear();
        verdict.DoneCommand.Execute(null);
        Assert.Equal(Stage.Card, shell.Stage);
    }

    [Fact]
    public async Task Shell_HistoryFileCannotBeOpened_TheVerdictCarriesTheStopText()
    {
        var rig = new Rig();
        rig.Offload.Ledger.EnsureFolderThrows = true;
        var shell = rig.Shell();
        await shell.StartAsync();
        await Eventually.TrueAsync(() => shell.Stage == Stage.Review, rig.Ui);

        shell.BeginOffload();
        await shell.StartCopyAsync();

        Assert.Equal(Stage.Verdict, shell.Stage);
        Assert.StartsWith("Couldn't create or open the history file", shell.Verdict!.StopText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shell_StaleCardVmChoosingAfterTheStageMoved_DoesNotStartASecondScan()
    {
        var rig = new Rig();
        var shell = rig.Shell();
        await shell.StartAsync();
        var stale = shell.Card!;                                    // the Card VM that auto-chose the single DJI card
        await Eventually.TrueAsync(() => shell.Stage == Stage.Review, rig.Ui);
        var review = shell.Review;
        Assert.Equal(1, rig.Scans);

        stale.Browse(@"E:\");                                       // a folder picked after a device change moved the stage on
        await Task.Delay(50, TestContext.Current.CancellationToken);
        rig.Ui.RunAll();

        Assert.Equal((Stage.Review, 1), (shell.Stage, rig.Scans));
        Assert.Same(review, shell.Review);
    }
}
