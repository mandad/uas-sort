// tests/UasSort.Review.Tests/VerdictVmTests.cs
namespace UasSort.Review.Tests;

public class VerdictVmTests
{
    private sealed class FakeEject : IDeviceEject
    {
        public EjectResult Eject(string volumeRoot) => new Ejected(volumeRoot);
    }

    private sealed class Rig
    {
        public FakeLedgerStore Ledger { get; } = Fake.Ledger();
        public FakeDialogService Dialogs { get; } = new();
        public FakeShellLauncher Shell { get; } = new();
        public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 9, 28, 2, 10, 0, TimeSpan.Zero));
        public int Reaudits { get; private set; }
        public Plan Plan { get; }
        public FormatVerdict Verdict { get; }
        public ItemId Video { get; }
        public ItemId Photo25a { get; }
        public ItemId Photo25b { get; }
        public ItemId Photo26 { get; }
        public ItemId Unknown { get; } = new("DCIM/DJI_A001/x.MP4");
        /// <summary>A panorama set on Jul 26 (local), present only when the rig is built with a set.</summary>
        public ItemId Set26 { get; } = new("DCIM/PANORAMA/001_0104");

        public Rig(bool withSet = false)
        {
            var p1 = PhotosOtherTabTests.Photo("DJI_20260725200000_0101_D.DNG", TestPlans.Utc(2026, 7, 26, 4, 0), new ProbablyImported("x"), 200_000_000);
            var p2 = PhotosOtherTabTests.Photo("DJI_20260725200100_0102_D.DNG", TestPlans.Utc(2026, 7, 26, 4, 1), new ProbablyImported("x"), 200_000_000);
            var p3 = PhotosOtherTabTests.Photo("DJI_20260726200000_0103_D.DNG", TestPlans.Utc(2026, 7, 27, 4, 0), new ProbablyImported("x"), 200_000_000);
            List<Item> extra = [p1, p2, p3];
            if (withSet) extra.Add(SetItem(Set26, TestPlans.Utc(2026, 7, 27, 4, 30)));
            var t = TestPlans.Utc(2026, 7, 26, 3, 0);
            var b = TestPlans.Base(TestPlans.CouncilAnvil(), extraItems: extra,
                                   extraEntries: [new CardEntry(Unknown.CardRelPath, 5_000_000, t, t, t, 0x20, EntryClass.Unknown, null)]);
            Plan = new ScriptedDeriver().Derive(b, new Tuning(), [], new SessionFlags(false), 1, TestContext.Current.CancellationToken);
            Video = TestPlans.Id(TestPlans.CouncilAnvil()[3].Name);
            (Photo25a, Photo25b, Photo26) = (p1.Raw.Unit.Id, p2.Raw.Unit.Id, p3.Raw.Unit.Id);
            Verdict = new FormatVerdict(VerdictLevel.NotSafe, TestPlans.Card, "E: · DJI Air 3S · serial 1A2B-3C4D: Don't format yet: 1 file failed",
                ImmutableDictionary<AuditCategory, int>.Empty.Add(AuditCategory.VerifiedThisRun, 3).Add(AuditCategory.AssumedByRule, 3).Add(AuditCategory.Unaccounted, 2),
                0, 0,
                [
                    U(TestPlans.Id(TestPlans.CouncilAnvil()[0].Name), AuditCategory.VerifiedThisRun, 1_200_000_000, "copied"),
                    U(Video, AuditCategory.Unaccounted, 1_200_000_000, "failed: verify"),
                    U(Photo25a, AuditCategory.AssumedByRule, 200_000_000, "probably imported"),
                    U(Photo25b, AuditCategory.AssumedByRule, 200_000_000, "probably imported"),
                    U(Photo26, AuditCategory.AssumedByRule, 200_000_000, "probably imported"),
                    U(Unknown, AuditCategory.Unaccounted, 5_000_000, "unknown file"),
                    .. withSet ? [U(Set26, AuditCategory.AssumedByRule, 100_000_000, "probably imported")] : Array.Empty<UnitAudit>(),
                ], [], null);
        }

        /// <summary>A two-frame panorama set (50 MB per frame) taken at <paramref name="utc"/>, Anchorage time.</summary>
        private static Item SetItem(ItemId id, DateTime utc)
        {
            ImmutableArray<CardEntry> members =
            [
                new($"{id.CardRelPath}/DJI_0001.JPG", 50_000_000, utc, utc, utc, 0x20, EntryClass.SetMember, null),
                new($"{id.CardRelPath}/DJI_0002.JPG", 50_000_000, utc, utc, utc, 0x20, EntryClass.SetMember, null),
            ];
            var raw = new RawItem(new SetUnit(id, SetKind.Panorama, "001_0104", members), ItemKind.Set, "001_0104", 100_000_000, utc,
                                  null, null, null, null);
            var local = TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById(TestPlans.Anchorage));
            return new Item(raw, new ItemTime(utc, TimeSource.DroneClockZone, TestPlans.Anchorage, TzSource.Gps, DateOnly.FromDateTime(local), local),
                            null, null, ItemFlags.NoGps, new ProbablyImported("x"));
        }

        public static UnitAudit U(ItemId id, AuditCategory c, long size, string detail) => new(id, c, [new AuditLine(id.CardRelPath, size, c, detail)]);

        /// <summary>The verdict with the audit of <paramref name="id"/> replaced (or added when absent).</summary>
        public FormatVerdict With(ItemId id, AuditCategory c, long size, string detail)
            => Verdict with { Units = [.. Verdict.Units.Where(u => u.Unit != id), U(id, c, size, detail)] };

        public VerdictVm Vm(OffloadResult? result = null, FormatVerdict? verdict = null, ILedgerStore? store = null)
        {
            var v = verdict ?? Verdict;
            return new(v, Plan, result, @"C:\AppData\uas-sort\reports\20260928-020500-run12345.json",
                       new VerdictPorts(store ?? Ledger, "PC1", Time, Dialogs, Shell, new FakeEject(), () => { Reaudits++; return v; }));
        }
    }

    [Fact]
    public void Verdict_WordingAndNothingPreselected()
    {
        var vm = new Rig().Vm();
        Assert.Equal("Don't format yet", vm.LevelText);
        Assert.Equal("E: · DJI Air 3S · serial 1A2B-3C4D: Don't format yet: 1 file failed", vm.Headline);
        Assert.Equal<string>(["Verified this run: 3", "Assumed imported: 3", "Not accounted for: 2"], vm.CategoryLines);
        Assert.Equal(5, vm.NotCopied.Count);
        Assert.All(vm.NotCopied, r => Assert.False(r.IsSelected));
        Assert.False(vm.RecordImportedCommand.CanExecute(null));
        Assert.False(vm.MarkNotNeededCommand.CanExecute(null));
        Assert.Equal(2, vm.NotCopiedDays.Count);
        Assert.Null(vm.SelectionText);
        Assert.True(vm.SelectAllPhotosAndSetsCommand.CanExecute(null));
        Assert.False(vm.ClearSelectionCommand.CanExecute(null));
    }

    [Fact]
    public void Verdict_SelectAll_SelectsEveryDecidablePhotoAndSetAcrossDays_NeverVideosUnknownOrUndecidable()
    {
        var rig = new Rig(withSet: true);
        var verdict = rig.With(rig.Photo25b, AuditCategory.Unaccounted, 200_000_000, CardDiffResult.ChangedDetail);
        var vm = rig.Vm(verdict: verdict);
        Assert.All(vm.NotCopied, r => Assert.False(r.IsSelected));               // nothing preselected
        Row(vm, rig.Video).ToggleCommand.Execute(null);                          // a selected video is cleared, as a day click does

        vm.SelectAllPhotosAndSetsCommand.Execute(null);

        Assert.Equal<ItemId>([rig.Photo25a, rig.Photo26, rig.Set26], vm.NotCopied.Where(r => r.IsSelected).Select(r => r.Unit));
        Assert.Equal(NotCopiedKind.Set, Row(vm, rig.Set26).Kind);
        Assert.False(Row(vm, rig.Photo25b).IsSelected);                          // can't be decided
        Assert.False(Row(vm, rig.Video).IsSelected);
        Assert.False(Row(vm, rig.Unknown).IsSelected);
        Assert.Equal("2 photos, 1 set · 0.5 GB", vm.SelectionText);
        Assert.True(vm.RecordImportedCommand.CanExecute(null));
        Assert.False(vm.SelectAllPhotosAndSetsCommand.CanExecute(null));         // nothing left to select
        Assert.True(vm.ClearSelectionCommand.CanExecute(null));

        // exactly the union of clicking every day button
        var byDays = rig.Vm(verdict: verdict);
        foreach (var day in byDays.NotCopiedDays) day.SelectDayCommand.Execute(null);
        Assert.Equal(byDays.NotCopied.Where(r => r.IsSelected).Select(r => r.Unit), vm.NotCopied.Where(r => r.IsSelected).Select(r => r.Unit));
        Assert.Equal(byDays.SelectionText, vm.SelectionText);
    }

    [Fact]
    public void Verdict_ClearSelection_DeselectsEverything()
    {
        var rig = new Rig(withSet: true);
        var vm = rig.Vm();
        vm.SelectAllPhotosAndSetsCommand.Execute(null);
        Assert.Equal("3 photos, 1 set · 0.7 GB", vm.SelectionText);

        vm.ClearSelectionCommand.Execute(null);

        Assert.All(vm.NotCopied, r => Assert.False(r.IsSelected));
        Assert.Null(vm.SelectionText);
        Assert.False(vm.ClearSelectionCommand.CanExecute(null));
        Assert.True(vm.SelectAllPhotosAndSetsCommand.CanExecute(null));
        Assert.False(vm.RecordImportedCommand.CanExecute(null));
        Assert.False(vm.MarkNotNeededCommand.CanExecute(null));

        Row(vm, rig.Video).ToggleCommand.Execute(null);                          // a single video selection is cleared too
        Assert.True(vm.ClearSelectionCommand.CanExecute(null));
        vm.ClearSelectionCommand.Execute(null);
        Assert.False(Row(vm, rig.Video).IsSelected);
        Assert.Null(vm.SelectionText);
    }

    [Fact]
    public void Verdict_SelectAll_DisabledWhenNoPhotoOrSetCanBeDecided()
    {
        var rig = new Rig();
        var stuck = rig.Verdict with
        {
            Units = [.. rig.Verdict.Units.Select(u => u.Unit == rig.Photo25a || u.Unit == rig.Photo25b || u.Unit == rig.Photo26
                                                     ? Rig.U(u.Unit, AuditCategory.Unaccounted, 200_000_000, CardDiffResult.ChangedDetail) : u)],
        };
        var vm = rig.Vm(verdict: stuck);

        Assert.Empty(vm.NotCopiedDays);
        Assert.False(vm.SelectAllPhotosAndSetsCommand.CanExecute(null));
        vm.SelectAllPhotosAndSetsCommand.Execute(null);
        Assert.All(vm.NotCopied, r => Assert.False(r.IsSelected));
        Assert.Null(vm.SelectionText);
    }

    [Fact]
    public void Verdict_PerDaySelectionCoversPhotosOnly_VideosAndUnknownOneAtATime()
    {
        var rig = new Rig();
        var vm = rig.Vm();
        Row(vm, rig.Video).ToggleCommand.Execute(null);

        vm.NotCopiedDays.Single(d => d.Date == new DateOnly(2026, 7, 25)).SelectDayCommand.Execute(null);
        Assert.Equal<ItemId>([rig.Photo25a, rig.Photo25b], vm.NotCopied.Where(r => r.IsSelected).Select(r => r.Unit));
        Assert.True(vm.RecordImportedCommand.CanExecute(null));

        Row(vm, rig.Unknown).ToggleCommand.Execute(null);
        Assert.Equal<ItemId>([rig.Unknown], vm.NotCopied.Where(r => r.IsSelected).Select(r => r.Unit));
        Assert.Equal(NotCopiedKind.Unknown, Row(vm, rig.Unknown).Kind);
        Assert.False(vm.RecordImportedCommand.CanExecute(null));
        Assert.True(vm.MarkNotNeededCommand.CanExecute(null));

        Row(vm, rig.Video).ToggleCommand.Execute(null);
        Assert.Equal<ItemId>([rig.Video], vm.NotCopied.Where(r => r.IsSelected).Select(r => r.Unit));
        Assert.Equal("1 video · 1.2 GB", vm.SelectionText);
    }

    [Fact]
    public async Task Verdict_RecordAsImported_ConfirmsWithCoreText_WritesDecisions_UndoRevokes()
    {
        var rig = new Rig();
        var vm = rig.Vm();
        vm.NotCopiedDays.Single(d => d.Date == new DateOnly(2026, 7, 25)).SelectDayCommand.Execute(null);
        var selected = vm.NotCopied.Where(r => r.IsSelected).Select(r => r.Row).ToList();

        await vm.RecordImportedCommand.ExecuteAsync(null);

        var dialog = Assert.Single(rig.Dialogs.Shown);
        Assert.Equal("Record as already imported?", dialog.Title);
        Assert.StartsWith(VerdictDecisions.Confirmation(DecisionKind.AssumedImported, selected), dialog.Body, StringComparison.Ordinal);
        var made = rig.Ledger.Writer.Records.OfType<DecisionRecord>().ToList();
        Assert.Equal(2, made.Count(d => d.Kind == "assumedImported"));
        Assert.All(made, d => Assert.Equal(("PC1", rig.Time.GetUtcNow().UtcDateTime), (d.Machine, d.At)));
        Assert.Equal(1, rig.Reaudits);
        Assert.True(vm.UndoCommand.CanExecute(null));

        await vm.UndoCommand.ExecuteAsync(null);
        var revokes = rig.Ledger.Writer.Records.OfType<RevokeRecord>().ToList();
        Assert.Equal(made.Select(d => d.Id).Order(StringComparer.Ordinal), revokes.Select(r => r.Decision).Order(StringComparer.Ordinal));
        Assert.Equal(2, rig.Reaudits);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public async Task Verdict_MarkOneVideoNotNeeded_WritesDismissed()
    {
        var rig = new Rig();
        var vm = rig.Vm();
        Row(vm, rig.Video).ToggleCommand.Execute(null);
        rig.Dialogs.Answers.Enqueue(DialogResult.Primary);

        await vm.MarkNotNeededCommand.ExecuteAsync(null);

        Assert.Equal("Mark as not needed?", rig.Dialogs.Shown[0].Title);
        var d = Assert.Single(rig.Ledger.Writer.Records.OfType<DecisionRecord>());
        Assert.Equal("dismissed", d.Kind);
        Assert.Equal(rig.Video.CardRelPath, d.Src);
    }

    [Fact]
    public async Task Verdict_CancelledConfirmation_WritesNothing()
    {
        var rig = new Rig();
        var vm = rig.Vm();
        Row(vm, rig.Video).ToggleCommand.Execute(null);
        rig.Dialogs.Answers.Enqueue(DialogResult.Close);

        await vm.MarkNotNeededCommand.ExecuteAsync(null);

        Assert.Empty(rig.Ledger.Writer.Records);
        Assert.Equal(0, rig.Reaudits);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void Verdict_SafeRemovalAndEject_GroupsAndCleanupAvailability()
    {
        var rig = new Rig();
        var result = new OffloadResult("run12345", [], null, TestPlans.Utc(2026, 9, 28, 2, 0), TestPlans.Utc(2026, 9, 28, 2, 5), [@"D:\"]);
        var vm = rig.Vm(result, rig.Verdict with { SafeRemovalNote = "Safely remove D: before formatting the card" });

        Assert.Equal("Safely remove D: before formatting the card", vm.SafeRemovalNote);
        var eject = Assert.Single(vm.Ejects);
        eject.EjectCommand.Execute(null);
        Assert.Equal("Ejected D:", eject.ResultText);

        vm.OpenReportCommand.Execute(null);
        Assert.Contains(rig.Shell.Opened, o => o.EndsWith("run12345.json", StringComparison.Ordinal));
        Assert.False(vm.CleanupCommand.CanExecute(null));
        vm.SetCleanupAvailability(true, null);
        Assert.True(vm.CleanupCommand.CanExecute(null));
    }

    [Fact]
    public void Verdict_ChangedSinceScanPhoto_CannotBeSelected_AndSaysRescanFirst()
    {
        var rig = new Rig();
        var vm = rig.Vm(verdict: rig.With(rig.Photo25b, AuditCategory.Unaccounted, 200_000_000, CardDiffResult.ChangedDetail));
        var stuck = Row(vm, rig.Photo25b);

        Assert.False(stuck.CanDecide);
        Assert.Equal("Rescan the card first", stuck.CannotDecideReason);
        Assert.False(stuck.ToggleCommand.CanExecute(null));
        Assert.True(Row(vm, rig.Photo25a).CanDecide);
        Assert.Null(Row(vm, rig.Photo25a).CannotDecideReason);

        stuck.ToggleCommand.Execute(null);
        Assert.False(stuck.IsSelected);

        vm.NotCopiedDays.Single(d => d.Date == new DateOnly(2026, 7, 25)).SelectDayCommand.Execute(null);
        Assert.Equal<ItemId>([rig.Photo25a], vm.NotCopied.Where(r => r.IsSelected).Select(r => r.Unit));
        Assert.True(vm.RecordImportedCommand.CanExecute(null));
    }

    [Fact]
    public void Verdict_CardSwappedVideo_CannotBeSelected()
    {
        var rig = new Rig();
        var vm = rig.Vm(verdict: rig.With(rig.Video, AuditCategory.Unaccounted, 1_200_000_000, "not copied: the card was swapped"));
        var stuck = Row(vm, rig.Video);

        Assert.False(stuck.CanDecide);
        Assert.Equal("Rescan the card first", stuck.CannotDecideReason);

        stuck.ToggleCommand.Execute(null);
        Assert.All(vm.NotCopied, r => Assert.False(r.IsSelected));
        Assert.Null(vm.SelectionText);
        Assert.False(vm.MarkNotNeededCommand.CanExecute(null));
    }

    [Fact]
    public async Task Verdict_UnitMatchingNoScannedFile_ShowsErrorDialog_WritesNothing()
    {
        var rig = new Rig();
        var gone = new ItemId("DCIM/DJI_A001/gone.MP4");
        var vm = rig.Vm(verdict: rig.With(gone, AuditCategory.Unaccounted, 7_000_000, "unknown file"));
        Row(vm, gone).ToggleCommand.Execute(null);
        Assert.True(vm.MarkNotNeededCommand.CanExecute(null));

        await vm.MarkNotNeededCommand.ExecuteAsync(null);

        Assert.Equal(2, rig.Dialogs.Shown.Count);
        var error = rig.Dialogs.Shown[1];
        Assert.Equal("Nothing was recorded", error.Title);
        Assert.Equal("DCIM/DJI_A001/gone.MP4 matches no file in the scanned card; rescan the card first", error.Body);
        Assert.Empty(rig.Ledger.Writer.Records);
        Assert.Equal(0, rig.Reaudits);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    /// <summary>A ledger store whose own file can't be opened (the guard refuses it).</summary>
    private sealed class RefusingLedger(FakeLedgerStore inner) : ILedgerStore
    {
        public LedgerFolderStatus Check() => inner.Check();
        public LedgerSnapshot Load() => inner.Load();
        public void EnsureFolder() => inner.EnsureFolder();
        public ILedgerWriter OpenOwn() => throw new UnsafeIoException("the history file is cloud-only");
        public void SnapshotToBackup(string runId) => inner.SnapshotToBackup(runId);
        public void KeepOnDevice() => inner.KeepOnDevice();
        public void CopyInto(string newVideoRoot, LedgerSnapshot current) => inner.CopyInto(newVideoRoot, current);
    }

    [Fact]
    public async Task Verdict_DecisionAppendFailsMidBatch_ShowsError_UndoRevokesOnlyWhatWasWritten()
    {
        var rig = new Rig();
        var vm = rig.Vm();
        vm.NotCopiedDays.Single(d => d.Date == new DateOnly(2026, 7, 25)).SelectDayCommand.Execute(null);
        rig.Ledger.Writer.ThrowWhen = r => r is DecisionRecord && rig.Ledger.Writer.Records.OfType<DecisionRecord>().Any();

        await vm.RecordImportedCommand.ExecuteAsync(null);

        Assert.Equal(2, rig.Dialogs.Shown.Count);
        var error = rig.Dialogs.Shown[1];
        Assert.Equal("Only part of the selection was recorded", error.Title);
        Assert.Equal("The history file couldn't be written: The ledger file couldn't be written. You can undo what was recorded.", error.Body);
        var written = Assert.Single(rig.Ledger.Writer.Records.OfType<DecisionRecord>());
        Assert.Equal(1, rig.Reaudits);
        Assert.True(vm.UndoCommand.CanExecute(null));

        rig.Ledger.Writer.ThrowWhen = null;
        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(written.Id, Assert.Single(rig.Ledger.Writer.Records.OfType<RevokeRecord>()).Decision);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public async Task Verdict_UndoAppendFails_ShowsError_KeepsUndoForWhatIsLeft()
    {
        var rig = new Rig();
        var vm = rig.Vm();
        vm.NotCopiedDays.Single(d => d.Date == new DateOnly(2026, 7, 25)).SelectDayCommand.Execute(null);
        await vm.RecordImportedCommand.ExecuteAsync(null);
        var made = rig.Ledger.Writer.Records.OfType<DecisionRecord>().Select(d => d.Id).ToList();
        rig.Ledger.Writer.ThrowWhen = r => r is RevokeRecord && rig.Ledger.Writer.Records.OfType<RevokeRecord>().Any();

        await vm.UndoCommand.ExecuteAsync(null);

        var error = rig.Dialogs.Shown[^1];
        Assert.Equal("Couldn't undo", error.Title);
        Assert.Equal("The history file couldn't be written: The ledger file couldn't be written.", error.Body);
        var first = Assert.Single(rig.Ledger.Writer.Records.OfType<RevokeRecord>()).Decision;
        Assert.True(vm.UndoCommand.CanExecute(null));
        Assert.Equal(2, rig.Reaudits);

        rig.Ledger.Writer.ThrowWhen = null;
        await vm.UndoCommand.ExecuteAsync(null);
        var revoked = rig.Ledger.Writer.Records.OfType<RevokeRecord>().Select(r => r.Decision).ToList();
        Assert.Equal(made.Order(StringComparer.Ordinal), revoked.Order(StringComparer.Ordinal));   // each decision revoked exactly once
        Assert.Equal(first, revoked[0]);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [Fact]
    public async Task Verdict_OwnLedgerRefused_ShowsSafetyStop_NothingRecorded()
    {
        var rig = new Rig();
        var vm = rig.Vm(store: new RefusingLedger(rig.Ledger));
        Row(vm, rig.Video).ToggleCommand.Execute(null);

        await vm.MarkNotNeededCommand.ExecuteAsync(null);

        var error = rig.Dialogs.Shown[^1];
        Assert.Equal("Nothing was recorded", error.Title);
        Assert.Equal("Internal safety stop: the history file is cloud-only", error.Body);
        Assert.Empty(rig.Ledger.Writer.Records);
        Assert.Equal(0, rig.Reaudits);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    private static NotCopiedRowVm Row(VerdictVm vm, ItemId id) => vm.NotCopied.Single(r => r.Unit == id);
}
