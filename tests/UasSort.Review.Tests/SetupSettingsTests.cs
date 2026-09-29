// tests/UasSort.Review.Tests/SetupSettingsTests.cs
namespace UasSort.Review.Tests;

public class SetupSettingsTests
{
    private const string NewRoot = @"D:\UAS Videos";

    private static LedgerFolderStatus Status(LedgerFolderState state, params string[] files)
        => new(TestPlans.VideoRoot + @"\.uas-sort", state, state != LedgerFolderState.Missing, true, state != LedgerFolderState.NotPinned, true,
               [.. files], state == LedgerFolderState.CloudOnly ? [.. files] : [], []);

    [Fact]
    public void LedgerStatusText_CoversEveryState()
    {
        Assert.Equal("History: 2 PCs' ledgers found", LedgerStatusText.For(Status(LedgerFolderState.Ok, "ledger-PC1.jsonl", "ledger-PC2.jsonl")).Text);
        Assert.Equal("History: 1 PC's ledger found", LedgerStatusText.For(Status(LedgerFolderState.Ok, "ledger-PC1.jsonl", "ledger-PC1-DESKTOP.jsonl")).Text);
        Assert.StartsWith("No history yet. It will be created at the first offload.", LedgerStatusText.For(Status(LedgerFolderState.Missing)).Text, StringComparison.Ordinal);
        var cloud = LedgerStatusText.For(Status(LedgerFolderState.CloudOnly, "ledger-PC2.jsonl"));
        Assert.Equal(("Set UAS Videos\\.uas-sort to Always keep on this device", InfoSeverity.Error, true), cloud);
        Assert.Equal(InfoSeverity.Warning, LedgerStatusText.For(Status(LedgerFolderState.NotPinned, "ledger-PC1.jsonl")).Severity);
        Assert.Equal(($@"Can't list {TestPlans.VideoRoot}\.uas-sort", InfoSeverity.Error, false),
                     LedgerStatusText.For(Status(LedgerFolderState.Unlistable, "ledger-PC1.jsonl")));      // F2: never "History: …" or "No history yet"
        foreach (var state in Enum.GetValues<LedgerFolderState>())
            Assert.False(string.IsNullOrWhiteSpace(LedgerStatusText.For(Status(state, "ledger-PC1.jsonl")).Text), state.ToString());
    }

    [Fact]
    public void LedgerStatusText_CountsHyphenatedMachineNames()
    {
        static string History(params string[] files) => LedgerStatusText.For(Status(LedgerFolderState.Ok, files)).Text;

        Assert.Equal("History: 2 PCs' ledgers found",
                     History("ledger-DESKTOP-A.jsonl", "ledger-DESKTOP-B.jsonl", "ledger-DESKTOP-A-DESKTOP-B.jsonl"));
        Assert.Equal("History: 2 PCs' ledgers found", History("ledger-DESKTOP-7H2K9QF.jsonl", "ledger-DESKTOP-M3P1XZ8.jsonl"));
        Assert.Equal("History: 1 PC's ledger found", History("ledger-DESKTOP-A.jsonl", "ledger-DESKTOP-A-LAPTOP-B.jsonl"));
        Assert.Equal("History: 1 PC's ledger found", History("ledger-desktop-a.jsonl", "LEDGER-DESKTOP-A.JSONL"));
        Assert.Equal("History: 1 PC's ledger found", History(@"C:\Lib\UAS Videos\.uas-sort\ledger-DESKTOP-A.jsonl", "notes.txt"));
    }

    [Fact]
    public void Setup_ConfirmSavesRootsConfirmed_AndPhotoRootFollowsVideoRoot()
    {
        var store = new FakeSettingsStore(new SettingsLoad(TestPlans.Settings(rootsConfirmed: false), false, null, null));
        var ledger = Fake.Ledger(Status(LedgerFolderState.Ok, "ledger-PC1.jsonl", "ledger-PC2.jsonl"));
        var vm = new SetupVm(store.Load(), store, _ => ledger, new FakeFreeSpace());
        Settings? confirmed = null;
        vm.Confirmed += s => confirmed = s;

        Assert.Equal("History: 2 PCs' ledgers found", vm.LedgerStatus);
        Assert.Equal("317 GB free", vm.VideoFreeText);
        vm.SetVideoRoot(NewRoot);
        Assert.Equal(NewRoot + @"\Picture Offload", vm.PhotoRoot);
        Assert.True(vm.PhotoInsideVideoNote);
        vm.ConfirmCommand.Execute(null);

        Assert.True(confirmed!.RootsConfirmed);
        Assert.Equal(NewRoot, store.Saved.Single().VideoRoot);
    }

    [Fact]
    public void Setup_CloudOnlyLedger_BlocksConfirmAndOffersKeepOnDevice()
    {
        var store = new FakeSettingsStore(new SettingsLoad(TestPlans.Settings(false), true, @"C:\x\settings.json.corrupt-1", new RunRoots(NewRoot, NewRoot + @"\Picture Offload")));
        var ledger = Fake.Ledger(Status(LedgerFolderState.CloudOnly, "ledger-PC2.jsonl"));
        var vm = new SetupVm(store.Load(), store, _ => ledger, new FakeFreeSpace());

        Assert.Equal(NewRoot, vm.VideoRoot);
        Assert.NotNull(vm.RecoveryText);
        Assert.False(vm.CanConfirm);
        Assert.False(vm.ConfirmCommand.CanExecute(null));
        Assert.True(vm.CanKeepOnDevice);
        vm.KeepOnDeviceCommand.Execute(null);
        Assert.Contains("KeepOnDevice", ledger.Calls);
    }

    /// <summary>Records CopyInto on top of the shared in-memory FakeLedgerStore (the offload fakes never copy a ledger).</summary>
    private sealed class CopyTarget(FakeLedgerStore inner) : ILedgerStore
    {
        public List<string> CopiedInto { get; } = [];
        public Exception? CopyFault { get; set; }
        public LedgerFolderStatus Check() => inner.Check();
        public LedgerSnapshot Load() => inner.Load();
        public void EnsureFolder() => inner.EnsureFolder();
        public ILedgerWriter OpenOwn() => inner.OpenOwn();
        public void SnapshotToBackup(string runId) => inner.SnapshotToBackup(runId);
        public void KeepOnDevice() => inner.KeepOnDevice();
        public void CopyInto(string newVideoRoot, LedgerSnapshot current)
        {
            if (CopyFault is { } fault) throw fault;
            CopiedInto.Add(newVideoRoot);
        }
    }

    private static readonly DateTime T = TestPlans.Utc(2026, 9, 27, 20, 0);
    private static readonly FileKey CopiedKey = new("dji_20260927140627_0128_d.mp4", 1_200_000_000);

    /// <summary>The current ledger: one app-copied video (so VideoRootChange.HasRecords is true).</summary>
    private static LedgerSnapshot CurrentLedger()
        => TestPlans.Ledger() with
        {
            Files = ImmutableDictionary<FileKey, LedgerFile>.Empty.Add(CopiedKey,
                new LedgerFile(CopiedKey, "DCIM/DJI_001/DJI_20260927140627_0128_D.MP4", DestRoot.Video, NewRoot + @"\x.MP4", null,
                               VerifyKind.Unbuffered, T, null, null, null, null, null, null, "PC1", "run1")),
        };

    private static (SettingsPageVm Vm, FakeSettingsStore Store, Dictionary<string, CopyTarget> Ledgers, FakeFileSystem Fs, FakeDialogService Dialogs, FakeTimeProvider Time,
                   FakeUiDispatcher Ui)
        Page()
    {
        var store = new FakeSettingsStore(new SettingsLoad(TestPlans.Settings(), false, null, null));
        var ledgers = new Dictionary<string, CopyTarget>(StringComparer.OrdinalIgnoreCase)
        {
            [TestPlans.VideoRoot] = new(Fake.Ledger(Status(LedgerFolderState.Ok, "ledger-PC1.jsonl"))),
            [NewRoot] = new(Fake.Ledger(Status(LedgerFolderState.Missing), videoRoot: NewRoot)),
        };
        var fs = FakeLayout.NewFileSystem();
        var dialogs = new FakeDialogService();
        var time = new FakeTimeProvider();
        var ui = new FakeUiDispatcher();
        var vm = new SettingsPageVm(TestPlans.Settings(), store, r => ledgers[r], fs, new FakeShellLauncher(), dialogs, new FakeFreeSpace(),
                                    time, ui, CurrentLedger, r => @"C:\AppData\uas-sort\ledger-backup\" + r.Length);
        return (vm, store, ledgers, fs, dialogs, time, ui);
    }

    [Fact] // F8: a [Copy] that fails says why and keeps the prompt; it never reaches the unhandled-error dialog
    public async Task Settings_CopyLedgerFails_ShowsADialog_AndKeepsThePrompt()
    {
        var (vm, _, ledgers, _, dialogs, _, _) = Page();
        await vm.ChangeVideoRootAsync(NewRoot);
        ledgers[NewRoot].CopyFault = new UnsafeIoException(@"Refused ReadData of C:\old\.uas-sort\ledger-B.jsonl: outside every configured root");

        await vm.CopyLedgerCommand.ExecuteAsync(null);

        var d = Assert.Single(dialogs.Shown);
        Assert.Equal("Couldn't copy the history", d.Title);
        Assert.Contains("outside every configured root", d.Body, StringComparison.Ordinal);
        Assert.NotNull(vm.NoHistoryPrompt);
    }

    [Fact]
    public async Task Settings_VideoRootChange_SavesFirstThenPromptsAndCopies()
    {
        var (vm, store, ledgers, _, _, _, _) = Page();
        await vm.ChangeVideoRootAsync(NewRoot);

        Assert.Equal(NewRoot, store.Saved[^1].VideoRoot);
        Assert.Equal(@"No history found in D:\UAS Videos\.uas-sort; copy current ledger there?", vm.NoHistoryPrompt);
        Assert.Equal(NewRoot + @"\.uas-sort", vm.LedgerFolder);

        await vm.CopyLedgerCommand.ExecuteAsync(null);
        Assert.Equal<string>([NewRoot], ledgers[NewRoot].CopiedInto);
        Assert.Null(vm.NoHistoryPrompt);
    }

    [Fact]
    public async Task Settings_StartEmpty_ConfirmsWhenNewRootHoldsAppCopiedVideos()
    {
        var (vm, _, _, fs, dialogs, _, _) = Page();
        fs.AddFile(NewRoot + @"\2026\2026-09\2026-09-27 Zachar Bay\DJI_20260927140627_0128_D.MP4", 1_200_000_000L, T);
        await vm.ChangeVideoRootAsync(NewRoot);

        dialogs.Answers.Enqueue(DialogResult.Close);
        await vm.StartEmptyCommand.ExecuteAsync(null);
        Assert.StartsWith("1 video here was copied by uas-sort; starting empty treats them as manual imports", dialogs.Shown.Single().Body, StringComparison.Ordinal);
        Assert.NotNull(vm.NoHistoryPrompt);

        dialogs.Answers.Enqueue(DialogResult.Primary);
        await vm.StartEmptyCommand.ExecuteAsync(null);
        Assert.Null(vm.NoHistoryPrompt);
    }

    [Fact]
    public void Settings_PhotoRootChange_AppendsPreviousAndSavesAfter500Ms()
    {
        var (vm, store, _, _, _, time, ui) = Page();
        vm.ChangePhotoRoot(@"D:\Photos");
        Assert.Empty(store.Saved);
        time.Advance(SettingsPageVm.SaveDelay);
        ui.RunAll();

        var saved = store.Saved.Single();
        Assert.Equal(@"D:\Photos", saved.PhotoRoot);
        Assert.Equal<string>([TestPlans.PhotoRoot], saved.PreviousPhotoRoots);
        Assert.Equal(TestPlans.PhotoRoot, Assert.Single(vm.PreviousPhotoRoots).Path);

        vm.PreviousPhotoRoots[0].ForgetCommand.Execute(null);
        time.Advance(SettingsPageVm.SaveDelay);
        ui.RunAll();
        Assert.Empty(store.Saved[^1].PreviousPhotoRoots);
    }

    [Fact]
    public void Settings_DefaultsClockAndMapFieldsSave()
    {
        var (vm, store, _, _, _, time, ui) = Page();
        vm.RadiusMiles = 30;
        vm.IsSiteLocal = true;
        vm.CopyJpgTwin = false;
        vm.MapBase = "satellite";
        time.Advance(SettingsPageVm.SaveDelay);
        ui.RunAll();

        var s = store.Saved.Single();
        Assert.Equal((30.0, StoredClockMode.SiteLocal, false, "satellite"), (s.RadiusMiles, s.DroneClockMode, s.CopyJpgTwin, s.Map.Base));
        Assert.Contains("GeoNames CC-BY 4.0", vm.AboutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Settings_DebouncedSave_RunsOnTheUiThread_AndYieldsToTheVideoRootSave()
    {
        var (vm, store, _, _, _, time, ui) = Page();
        vm.RadiusMiles = 30;
        time.Advance(SettingsPageVm.SaveDelay);

        Assert.Empty(store.Saved);                 // the timer only posts; the save runs on the UI thread
        Assert.Equal(1, ui.Pending);

        await vm.ChangeVideoRootAsync(NewRoot);
        ui.RunAll();

        var saved = Assert.Single(store.Saved);    // the stale posted save is skipped: nothing lands after the Ref 9.14 save
        Assert.Equal((NewRoot, 30.0), (saved.VideoRoot, saved.RadiusMiles));
    }

    [Fact]
    public void Settings_ClearedRadius_IsIgnored()
    {
        var (vm, store, _, _, _, time, ui) = Page();
        var before = vm.Current.RadiusMiles;
        vm.RadiusMiles = double.NaN;                 // an emptied NumberBox writes NaN
        time.Advance(SettingsPageVm.SaveDelay);
        ui.RunAll();
        Assert.Equal(before, vm.Current.RadiusMiles);
        Assert.Empty(store.Saved);

        vm.RadiusMiles = 30;                         // later edits still save
        time.Advance(SettingsPageVm.SaveDelay);
        ui.RunAll();
        Assert.Equal(30.0, store.Saved.Single().RadiusMiles);
    }

    [Fact]
    public void Settings_Dispose_FlushesAPendingDebouncedSave()
    {
        var (vm, store, _, _, _, time, ui) = Page();
        vm.RadiusMiles = 30;                         // closed before the 500 ms debounce ran
        vm.Dispose();

        Assert.Equal(30.0, Assert.Single(store.Saved).RadiusMiles);
        time.Advance(SettingsPageVm.SaveDelay);
        ui.RunAll();
        Assert.Single(store.Saved);                  // the timer was stopped: no second save
    }

    [Fact]
    public void Settings_Dispose_WithNothingPending_DoesNotSave()
    {
        var (vm, store, _, _, _, time, ui) = Page();
        vm.Dispose();
        time.Advance(SettingsPageVm.SaveDelay);
        ui.RunAll();
        Assert.Empty(store.Saved);
    }
}
