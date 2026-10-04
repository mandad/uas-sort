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
        public PlanBase Base { get; set; } = TestPlans.Base(TestPlans.Zachar());
        /// <summary>Replaces the instant scan (a scan that waits for Cancel, or one that throws).</summary>
        public Func<CancellationToken, Task<ScanResult>>? ScanWith { get; set; }
        public bool ReviewThrows { get; set; }
        public List<Settings> ScannedWith { get; } = [];
        public List<Exception> Faults { get; } = [];
        public FakeDialogService Dialogs { get; } = new();
        public IDraftStore CommitDrafts { get; set; } = new FakeDraftStore();
        public List<(CleanupOrigin Origin, OffloadResult? Offload)> CleanupOpens { get; } = [];
        /// <summary>ShellDeps.VolumeRefusal: the cleanup volume check's refusal and detail (default: the volume passes).</summary>
        public Func<CardSource, (string? Refusal, string? Detail)> Refusal { get; set; } = _ => default;
        public ListLog Log { get; } = new();

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
                s =>
                {
                    ScannedWith.Add(s);
                    return new ScanStageVm((src, p, ct) => { Scans++; return ScanWith?.Invoke(ct) ?? Task.FromResult(Base.Scan); }, _ => Base, s, Ui);
                },
                (src, b) => ReviewThrows ? throw new InvalidOperationException(@"No volume found for \\server\share\card") : new ReviewVm(new PlanSession(b, new ScriptedDeriver([new Suggestion("Zachar Bay", DescSource.Feature, null, null)]), new Tuning(), services.Time),
                                         services, new LedgerDecisionService(Ledger, services.Time, "PC1")),
                r => new PreflightVm(r.Plan, shell!.Source!, _ => CommitSession.Begin(Offload.Plan, Commit, "run-1"),
                                     new CommitPorts(CommitDrafts, new FakeDialogService(), Ui)),
                p => new CopyVm(p, Dialogs, Ui),
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
                s => Refusal(s),
                Saved.Add) { Log = Log });
            shell.Faulted += Faults.Add;
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

    [Fact] // Task U4: a refused volume shows the failing rule as visible text (a disabled button shows no tooltip)
    public async Task Shell_RefusedVolume_ShowsTheRuleAsVisibleText_NullOnceEnabled()
    {
        var rig = new Rig { Refusal = _ => (CleanupVolumeCheck.NotACard, "rule 4: bus Usb, removable media false") };
        var shell = rig.Shell();
        await shell.StartAsync();
        await Eventually.TrueAsync(() => shell.Stage == Stage.Review, rig.Ui);

        const string text = "This doesn't look like a drone card (rule 4: bus Usb, removable media false)";
        Assert.Equal((false, CleanupVolumeCheck.NotACard, text), (shell.CleanupEnabled, shell.CleanupTooltip, shell.CleanupUnavailableText));

        shell.BeginOffload();
        Assert.Equal("Wait until the offload finishes", shell.CleanupUnavailableText);
        await shell.StartCopyAsync();
        Assert.Equal(Stage.Verdict, shell.Stage);
        Assert.Equal((false, CleanupVolumeCheck.NotACard, text),
                     (shell.Verdict!.CanCleanup, shell.Verdict.CleanupTooltip, shell.Verdict.CleanupUnavailableText));

        rig.Refusal = _ => default;
        shell.DeviceChanged();
        Assert.Equal((true, null, null), (shell.CleanupEnabled, shell.CleanupTooltip, shell.CleanupUnavailableText));
        Assert.Equal((true, null), (shell.Verdict.CanCleanup, shell.Verdict.CleanupUnavailableText));
    }

    [Fact] // Task U4: the availability is logged (Info) once per change for a card source, not on every flag update
    public async Task Shell_CleanupAvailability_IsLoggedOncePerChange()
    {
        var rig = new Rig { Refusal = _ => (CleanupVolumeCheck.NotACard, "rule 4: bus Scsi, removable media false") };
        var shell = rig.Shell();
        await shell.StartAsync();
        await Eventually.TrueAsync(() => shell.Stage == Stage.Review, rig.Ui);
        var root = shell.Source!.Root;

        shell.DeviceChanged();
        shell.DeviceChanged();
        rig.Refusal = _ => default;
        shell.DeviceChanged();
        shell.DeviceChanged();

        Assert.Equal([$"Card cleanup on {root} unavailable: Wait until the scan finishes",
                      $"Card cleanup on {root} unavailable: This doesn't look like a drone card (rule 4: bus Scsi, removable media false)",
                      $"Card cleanup on {root} available"],
                     rig.Log.Infos);
        Assert.Empty(rig.Log.Warnings);
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

    private static PlanBase WithClock(ClockModel clock) => TestPlans.Base(TestPlans.Zachar(), clock: clock);

    [Fact] // F1 (Ref §6.1, §9.14): a successful run saves what the drone clock learned
    public async Task Shell_SuccessfulOffload_SavesTheLearnedClockZone()
    {
        var rig = new Rig { Base = WithClock(new ClockModel(ClockMode.Zone, "America/Los_Angeles", [], TimeSpan.FromHours(-7),
                                                             StoredClockMode.Zone, "America/New_York")) };
        var shell = rig.Shell();
        await shell.StartAsync();
        await Eventually.TrueAsync(() => shell.Stage == Stage.Review, rig.Ui);

        shell.BeginOffload();
        await shell.StartCopyAsync();

        Assert.Null(shell.Verdict!.StopText);
        var saved = Assert.Single(rig.Saved);
        Assert.Equal((StoredClockMode.Zone, "America/Los_Angeles"), (saved.DroneClockMode, saved.DroneClockZone));
        Assert.Equal("America/Los_Angeles", shell.Settings.DroneClockZone);
        Assert.Equal(TestPlans.VideoRoot, saved.VideoRoot);
    }

    [Fact] // F1: SiteLocal saves the mode only
    public async Task Shell_SuccessfulOffload_SiteLocalSavesTheModeOnly()
    {
        var rig = new Rig { Base = WithClock(new ClockModel(ClockMode.SiteLocal, null, [], TimeSpan.Zero, StoredClockMode.Zone, "America/New_York")) };
        var shell = rig.Shell();
        await shell.StartAsync();
        await Eventually.TrueAsync(() => shell.Stage == Stage.Review, rig.Ui);

        shell.BeginOffload();
        await shell.StartCopyAsync();

        Assert.Equal((StoredClockMode.SiteLocal, "America/New_York"), (Assert.Single(rig.Saved).DroneClockMode, rig.Saved[0].DroneClockZone));
    }

    [Fact] // F1: a stopped run, or a clock that learned nothing to keep (NearestSample), saves nothing
    public async Task Shell_StoppedRunOrNearestSample_SavesNoClock()
    {
        var zone = new ClockModel(ClockMode.Zone, "America/Los_Angeles", [], TimeSpan.FromHours(-7), StoredClockMode.Zone, "America/New_York");
        var stopped = new Rig { Base = WithClock(zone) };
        stopped.Offload.Ledger.EnsureFolderThrows = true;                     // LedgerWriteFailed: nothing copied
        var shell = stopped.Shell();
        await shell.StartAsync();
        await Eventually.TrueAsync(() => shell.Stage == Stage.Review, stopped.Ui);
        shell.BeginOffload();
        await shell.StartCopyAsync();
        Assert.Empty(stopped.Saved);
        Assert.Equal("America/New_York", shell.Settings.DroneClockZone);

        var nearest = new Rig { Base = WithClock(new ClockModel(ClockMode.NearestSample, null, [], null, StoredClockMode.Zone, "America/New_York")) };
        var shell2 = nearest.Shell();
        await shell2.StartAsync();
        await Eventually.TrueAsync(() => shell2.Stage == Stage.Review, nearest.Ui);
        shell2.BeginOffload();
        await shell2.StartCopyAsync();
        Assert.Empty(nearest.Saved);
    }

    [Fact] // F9 (Ref §9.1 Scan): Cancel with one DJI card inserted shows the Card stage and does not start the scan again
    public async Task Shell_CancelledScan_ShowsTheCardStage_WithoutRescanning()
    {
        var rig = new Rig { ScanWith = async ct => { await Task.Delay(Timeout.Infinite, ct); throw new InvalidOperationException("unreachable"); } };
        var shell = rig.Shell();
        await shell.StartAsync();
        Assert.Equal((Stage.Scan, 1), (shell.Stage, rig.Scans));

        ((ScanStageVm)shell.Current!).CancelCommand.Execute(null);
        await Eventually.TrueAsync(() => shell.Stage == Stage.Card, rig.Ui);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        rig.Ui.RunAll();

        Assert.Equal((Stage.Card, 1), (shell.Stage, rig.Scans));
        Assert.False(shell.IsScanning);
        Assert.True(shell.CanBrowse);
        Assert.True(shell.CanOpenSettings);
        Assert.Single(shell.Card!.Rows);                                       // the card is listed; the user picks it again
    }

    [Fact] // F10: an unexpected scan fault (no volume for a network folder) is logged and shown; the shell is never stuck on Scan
    public async Task Shell_ScanThatThrowsUnexpectedly_ShowsItOnTheCardStage_AndResetsTheFlags()
    {
        var rig = new Rig { ScanWith = _ => throw new InvalidOperationException(@"No volume found for \\server\share\card") };
        var shell = rig.Shell();
        await shell.StartAsync();
        await Eventually.TrueAsync(() => shell.Stage == Stage.Card && shell.Card!.Message is not null, rig.Ui);

        Assert.Equal(1, rig.Scans);
        Assert.False(shell.IsScanning);
        Assert.True(shell.CanRescan);
        Assert.True(shell.CanBrowse);
        Assert.Contains(@"No volume found for \\server\share\card", shell.Card!.Message, StringComparison.Ordinal);
        Assert.IsType<InvalidOperationException>(Assert.Single(rig.Faults));
    }

    [Fact] // F10: CreateReview throwing after the scan is handled the same way
    public async Task Shell_ReviewThatCannotBeCreated_ShowsItOnTheCardStage()
    {
        var rig = new Rig { ReviewThrows = true };
        var shell = rig.Shell();
        await shell.StartAsync();
        await Eventually.TrueAsync(() => shell.Stage == Stage.Card && shell.Card!.Message is not null, rig.Ui);

        Assert.Null(shell.Review);
        Assert.False(shell.IsScanning);
        Assert.True(shell.CanBrowse);
        Assert.Single(rig.Faults);
    }

    [Fact] // F11: new roots (or any plan input) from Settings invalidate the Review: it rescans with the new settings
    public async Task Shell_SettingsChangedFromReview_Rescans_WithTheNewRoots()
    {
        var rig = new Rig();
        var shell = rig.Shell();
        await shell.StartAsync();
        await Eventually.TrueAsync(() => shell.Stage == Stage.Review, rig.Ui);
        var before = shell.Review;

        shell.OpenSettings();
        await ((SettingsPageVm)shell.Current!).ChangeVideoRootAsync(@"D:\UAS Videos");
        shell.CloseSettings();
        await Eventually.TrueAsync(() => shell.Stage == Stage.Review && !ReferenceEquals(before, shell.Review), rig.Ui);

        Assert.Equal(2, rig.Scans);
        Assert.Equal(@"D:\UAS Videos", rig.ScannedWith[^1].VideoRoot);
    }

    [Fact] // F11: a change that is not a plan input (the map base) returns to the same Review without a rescan
    public async Task Shell_SettingsWithoutPlanInputChanges_ReturnToTheSameReview()
    {
        var rig = new Rig();
        var shell = rig.Shell();
        await shell.StartAsync();
        await Eventually.TrueAsync(() => shell.Stage == Stage.Review, rig.Ui);
        var before = shell.Review;

        shell.OpenSettings();
        ((SettingsPageVm)shell.Current!).MapBase = "none";
        shell.CloseSettings();

        Assert.Equal((Stage.Review, 1), (shell.Stage, rig.Scans));
        Assert.Same(before, shell.Review);
    }

    [Fact] // F13: the next card's Verdict-origin cleanup never gets the previous card's OffloadResult
    public async Task Shell_NextCardsVerdictCleanup_GetsNoOffloadFromThePreviousCard()
    {
        var rig = new Rig();
        var shell = rig.Shell();
        await shell.StartAsync();
        await Eventually.TrueAsync(() => shell.Stage == Stage.Review, rig.Ui);
        shell.BeginOffload();
        await shell.StartCopyAsync();

        var folder = new LibraryFolderRef(TestPlans.VideoRoot + @"\2026\2026-09\2026-09-27 Zachar Bay", new DateOnly(2026, 9, 27), "Zachar Bay");
        rig.Base = TestPlans.Base([.. TestPlans.Zachar().Select(c => c with { Newness = new Imported(Evidence.LibraryNameSize, folder, "same name and size") })]);
        shell.Verdict!.DoneCommand.Execute(null);                              // card B (nothing new) is scanned
        await Eventually.TrueAsync(() => shell.Stage == Stage.Review && shell.Review!.NothingNew, rig.Ui);
        shell.Review!.ShowVerdictCommand.Execute(null);
        Assert.Equal(Stage.Verdict, shell.Stage);
        shell.Verdict!.CleanupCommand.Execute(null);

        var (origin, offload) = rig.CleanupOpens[^1];
        Assert.Equal(CleanupOrigin.Verdict, origin);
        Assert.Null(offload);
    }

    [Fact] // F18: a history that couldn't be fully written after the copy is shown on the Verdict page
    public async Task Shell_LedgerTailWriteFails_TheVerdictWarnsToRescan()
    {
        var rig = new Rig();
        rig.Offload.Writer.ThrowWhen = r => r is SeenRecord or RunRecord;
        var shell = rig.Shell();
        await shell.StartAsync();
        await Eventually.TrueAsync(() => shell.Stage == Stage.Review, rig.Ui);

        shell.BeginOffload();
        await shell.StartCopyAsync();

        Assert.Equal(Stage.Verdict, shell.Stage);
        Assert.Equal("The history couldn't be fully written; rescan before formatting the card.", shell.Verdict!.HistoryWarning);
    }

    [Fact] // F16: the unhandled-exception dialog lists the copies of the run in progress, or of the last run shown on the Verdict page
    public async Task Shell_RunId_IsTheRunInProgressThenTheLastRun_UntilTheNextCard()
    {
        var rig = new Rig();
        var shell = rig.Shell();
        await shell.StartAsync();
        await Eventually.TrueAsync(() => shell.Stage == Stage.Review, rig.Ui);
        Assert.Null(shell.RunId);

        shell.BeginOffload();
        Assert.Equal("run-1", shell.RunId);
        await shell.StartCopyAsync();
        Assert.Equal("run-1", shell.RunId);

        rig.Cards.Clear();
        shell.Verdict!.DoneCommand.Execute(null);
        Assert.Null(shell.RunId);
    }
}