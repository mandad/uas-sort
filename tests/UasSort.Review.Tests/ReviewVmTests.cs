// tests/UasSort.Review.Tests/ReviewVmTests.cs
namespace UasSort.Review.Tests;

public class ReviewVmTests
{
    private static readonly LibraryFolderRef CouncilFolder = new(TestPlans.VideoRoot + @"\2026\2026-07\2026-07-25 Council Road", new(2026, 7, 25), "Council Road");
    private static readonly LibraryFolderRef AnvilFolder = new(TestPlans.VideoRoot + @"\2026\2026-07\2026-07-26 Anvil Mountain", new(2026, 7, 26), "Anvil Mountain");
    private static readonly ImmutableArray<Suggestion> ZacharName = [new("Zachar Bay", DescSource.Feature, Distance.FromMiles(0.4), new DateOnly(2026, 9, 27))];

    [Fact]
    public async Task ReviewVm_NothingNewOnCard_EmptyStateFoldedAndOffloadDisabled()
    {
        var ca = TestPlans.CouncilAnvil();
        IReadOnlyList<PlanClip> clips =
        [
            ca[0] with { Newness = new Imported(Evidence.LedgerVerified, CouncilFolder, "in the history") },
            ca[1] with { Newness = new Imported(Evidence.LedgerVerified, CouncilFolder, "in the history") },
            ca[2] with { Newness = new Imported(Evidence.LibraryNameSize, AnvilFolder, "same name and size") },
            ca[3] with { Newness = new Imported(Evidence.LibraryNameSize, AnvilFolder, "same name and size") },
        ];
        var photo = PhotosOtherTabTests.Photo("DJI_20260725200000_0101_D.DNG", TestPlans.Utc(2026, 7, 26, 4, 0),
                                              new Imported(Evidence.LedgerVerified, null, "in the history"));
        using var h = ReviewHarness.Create(clips, extraItems: [photo],
            photoDays: [new PhotoDay(new DateOnly(2026, 7, 25), TestPlans.Anchorage, [photo.Raw.Unit.Id], "already imported")]);
        await h.SettleAsync();

        Assert.True(h.Vm.NothingNew);
        Assert.Equal("Nothing new on this card", h.Vm.EmptyStateText);
        Assert.All(h.Vm.Plan.Groups, g => Assert.IsType<AlreadyImported>(g.Target));
        Assert.All(h.Vm.Videos.Timeline, e => Assert.IsType<FoldedRunVm>(e));   // every AlreadyImported group folded
        Assert.False(h.Vm.CanOffload);
        Assert.False(h.Vm.OffloadCommand.CanExecute(null));
        Assert.Equal("Nothing new on this card", h.Vm.OffloadDisabledReason);
        Assert.True(h.Vm.ShowVerdictCommand.CanExecute(null));
        var asked = false;
        h.Vm.VerdictRequested += () => asked = true;
        h.Vm.ShowVerdictCommand.Execute(null);
        Assert.True(asked);
    }

    [Fact]
    public async Task ReviewVm_BlockingIssue_DisablesOffloadUntilNamed()
    {
        using var h = ReviewHarness.Create(TestPlans.Zachar());
        await h.SettleAsync();
        Assert.False(h.Vm.CanOffload);
        Assert.Equal("Fix before offloading:\n• Name this folder", h.Vm.OffloadDisabledReason);

        await h.Vm.RenameAsync(h.Card(0), "Zachar Bay");
        await h.SettleAsync();
        Assert.True(h.Vm.CanOffload);
        Assert.Null(h.Vm.OffloadDisabledReason);
        Assert.Equal("Zachar Bay", h.Card(0).Description);
        Assert.Equal("3 videos · 3.6 GB → C: (317 GB free)", h.Vm.FooterText);
    }

    [Fact]
    public async Task ReviewVm_ClockInfoBars_MismatchDismissedForSessionOnlyAndChipOnGroup()
    {
        var summary = TestPlans.Summary(ClockMode.Zone, 3, ["America/Anchorage"]);
        var b = TestPlans.Base(TestPlans.Zachar(), summary: summary);
        using (var h = ReviewHarness.Create([], planBase: b))
        {
            await h.SettleAsync();
            Assert.Contains(h.Vm.InfoBars, i => i.Key == "clock");
            var mm = Assert.Single(h.Vm.InfoBars, i => i.Key == "clockMismatch");
            Assert.Equal(InfoSeverity.Warning, mm.Severity);
            Assert.True(mm.IsClosable);
            Assert.StartsWith("Drone clock is set to UTC−4 (America/New_York), but footage on this card was shot in Alaska (UTC−8).", mm.Message, StringComparison.Ordinal);

            h.Vm.CloseInfoBar("clockMismatch");
            Assert.DoesNotContain(h.Vm.InfoBars, i => i.Key == "clockMismatch");
            await h.Vm.RenameAsync(h.Card(0), "Zachar Bay");
            await h.SettleAsync();
            Assert.DoesNotContain(h.Vm.InfoBars, i => i.Key == "clockMismatch");
        }

        using var rescan = ReviewHarness.Create([], planBase: b);
        await rescan.SettleAsync();
        Assert.Contains(rescan.Vm.InfoBars, i => i.Key == "clockMismatch");
    }

    [Fact]
    public async Task ReviewVm_AcceptAndContinue_TurnsLedgerIssueIntoAckWarningForThisSessionOnly()
    {
        var ledger = TestPlans.Ledger(parseIssues: [new LedgerParseIssue("ledger-PC2.jsonl", 17, "unexpected token")]);
        using var h = ReviewHarness.Create(TestPlans.Zachar(), ledger: ledger, suggestions: ZacharName);
        await h.SettleAsync();

        var bar = Assert.Single(h.Vm.InfoBars, i => i.Key.StartsWith("issue:LedgerParseIssue", StringComparison.Ordinal));
        Assert.Equal(InfoSeverity.Error, bar.Severity);
        Assert.False(h.Vm.CanOffload);
        await Assert.Single(bar.Actions, a => a.Label == "Accept and continue").Command.ExecuteAsync(null);
        await Eventually.TrueAsync(() => h.Vm.Plan.Issues.Any(i => i.Code == IssueCode.LedgerParseIssue && i.Severity == IssueSeverity.Warning), h.Ui);

        Assert.True(h.Vm.Plan.Issues.Single(i => i.Code == IssueCode.LedgerParseIssue).RequiresAckAtPreflight);
        Assert.True(h.Vm.CanOffload);
        Assert.Empty(h.Session.ToDraft().Edits);

        using var rescan = ReviewHarness.Create(TestPlans.Zachar(), ledger: ledger, suggestions: ZacharName);
        await rescan.SettleAsync();
        Assert.Equal(IssueSeverity.Blocking, rescan.Vm.Plan.Issues.Single(i => i.Code == IssueCode.LedgerParseIssue).Severity);
    }

    [Fact]
    public async Task ReviewVm_ChipMerge_UndoRedo()
    {
        using var h = ReviewHarness.Create(TestPlans.CouncilAnvil(), tuning: new Tuning(25, 1));
        await h.SettleAsync();
        Assert.Equal(2, h.Vm.Plan.Groups.Length);
        Assert.False(h.Vm.CanUndo);

        await h.Card(1).Chip!.MergeCommand.ExecuteAsync(null);
        await h.SettleAsync();
        Assert.Single(h.Vm.Plan.Groups);
        Assert.True(h.Vm.CanUndo);
        Assert.Equal((h.Session.CanUndo, h.Session.CanRedo), (h.Vm.CanUndo, h.Vm.CanRedo));

        await h.Vm.UndoCommand.ExecuteAsync(null);
        await h.SettleAsync();
        Assert.Equal(2, h.Vm.Plan.Groups.Length);
        Assert.True(h.Vm.CanRedo);
        Assert.Equal((h.Session.CanUndo, h.Session.CanRedo), (h.Vm.CanUndo, h.Vm.CanRedo));

        await h.Vm.RedoCommand.ExecuteAsync(null);
        await h.SettleAsync();
        Assert.Single(h.Vm.Plan.Groups);
    }

    [Fact]
    public async Task ReviewVm_SplitHereAndMoveToNewGroup()
    {
        using var h = ReviewHarness.Create(TestPlans.CouncilAnvil());
        await h.SettleAsync();
        h.Vm.Videos.SelectedEntry = h.Card(0);
        var banner = h.Vm.Videos.Clips.Single(c => c.Banner is not null).Banner!;

        await banner.SplitCommand.ExecuteAsync(null);
        await h.SettleAsync();
        Assert.Equal(2, h.Vm.Plan.Groups.Length);
        Assert.Equal("── split by you ──", h.Card(1).Chip!.Text);

        h.Vm.Videos.SelectedEntry = h.Card(0);
        h.Vm.Videos.SetSelectedClips([TestPlans.Id(TestPlans.CouncilAnvil()[1].Name)]);
        await h.Vm.MoveSelectedToNewGroupAsync();
        await h.SettleAsync();
        Assert.Equal(3, h.Vm.Plan.Groups.Length);
    }

    [Fact]
    public async Task ReviewVm_QuickFixWithTwoEdits_IsOneUndoEntry()
    {
        using var h = ReviewHarness.Create(TestPlans.CouncilAnvil());
        await h.SettleAsync();
        var anvil = TestPlans.Id(TestPlans.CouncilAnvil()[2].Name);
        var anvil2 = TestPlans.Id(TestPlans.CouncilAnvil()[3].Name);

        await h.Vm.ApplyQuickFixAsync(new QuickFix("Split and name", [new SplitBefore(anvil), new Rename(anvil, "Anvil Mountain", [anvil, anvil2])]));
        await h.SettleAsync();
        Assert.Equal(2, h.Vm.Plan.Groups.Length);
        Assert.Equal("Anvil Mountain", h.Vm.Plan.Groups[1].Description);

        await h.Vm.UndoCommand.ExecuteAsync(null);
        await h.SettleAsync();
        Assert.Single(h.Vm.Plan.Groups);
        Assert.False(h.Vm.CanUndo);
    }

    [Fact]
    public async Task ReviewVm_Rename_ValidationUnchangedAndRejectedOnAppend()
    {
        using var h = ReviewHarness.Create(TestPlans.Zachar(), suggestions: ZacharName);
        await h.SettleAsync();

        await h.Vm.RenameAsync(h.Card(0), "???");
        Assert.Equal("Use letters or numbers in the folder name", h.Vm.LastError);
        await h.Vm.RenameAsync(h.Card(0), "Zachar Bay");
        Assert.False(h.Vm.CanUndo);

        var folder = TestPlans.VideoRoot + @"\2026\2026-09\2026-09-27 Zachar Bay";
        await h.Vm.RetargetAsync(h.Card(0), new RetargetOptionVm(RetargetKind.Append, "Zachar Bay", null, folder, new DateOnly(2026, 9, 27)));
        await h.SettleAsync();
        Assert.Equal("APPEND", h.Card(0).Badge);

        await h.Vm.RenameAsync(h.Card(0), "Something else");
        await h.SettleAsync();
        Assert.Contains("Appending to an existing folder", h.Vm.LastError, StringComparison.Ordinal);
        Assert.Equal("APPEND", h.Card(0).Badge);
    }

    [Fact]
    public async Task ReviewVm_RetargetToLaterDatedFolder_NeedsConfirmation()
    {
        using var h = ReviewHarness.Create(TestPlans.Zachar(), suggestions: ZacharName);
        await h.SettleAsync();
        var later = new RetargetOptionVm(RetargetKind.Append, "Kodiak", "Sep 28 · 50 mi", TestPlans.VideoRoot + @"\2026\2026-09\2026-09-28 Kodiak", new DateOnly(2026, 9, 28));

        h.Dialogs.Answers.Enqueue(DialogResult.Close);
        await h.Vm.RetargetAsync(h.Card(0), later);
        Assert.Equal("Folder is dated Sep 28; these clips start Sep 27", Assert.Single(h.Dialogs.Shown).Body);
        Assert.False(h.Vm.CanUndo);

        h.Dialogs.Answers.Enqueue(DialogResult.Primary);
        await h.Vm.RetargetAsync(h.Card(0), later);
        await h.SettleAsync();
        var edit = Assert.IsType<Retarget>(Assert.Single(h.Session.ToDraft().Edits));
        Assert.True(edit.ConfirmedBeforeFolderDate);
        Assert.Equal("APPEND", h.Card(0).Badge);
    }

    [Theory]
    [InlineData(@"D:\Elsewhere\2026-09-27 X", "Pick a folder inside UAS Videos")]
    [InlineData(@"C:\Lib\UAS Videos\.uas-sort", "That folder is reserved for uas-sort history or photos")]
    [InlineData(@"C:\Lib\UAS Videos\.uas-sort\sub", "That folder is reserved for uas-sort history or photos")]
    [InlineData(@"C:\Lib\UAS Videos\Picture Offload", "That folder is reserved for uas-sort history or photos")]
    [InlineData(@"C:\Lib\UAS Videos", "Pick a folder inside UAS Videos")]
    public async Task ReviewVm_BrowseExisting_RefusesOutsideAndReservedFolders(string picked, string message)
    {
        using var h = ReviewHarness.Create(TestPlans.Zachar(), suggestions: ZacharName);
        await h.SettleAsync();
        await h.Vm.BrowseRetargetAsync(h.Card(0), picked);
        Assert.Equal(message, h.Vm.LastError);
        Assert.False(h.Vm.CanUndo);
    }

    [Fact]
    public async Task ReviewVm_BrowseExisting_ValidFolderAppends()
    {
        using var h = ReviewHarness.Create(TestPlans.Zachar(), suggestions: ZacharName);
        await h.SettleAsync();
        await h.Vm.BrowseRetargetAsync(h.Card(0), TestPlans.VideoRoot + @"\2026\2026-09\2026-09-27 Zachar Bay\");
        await h.SettleAsync();
        Assert.Equal("APPEND", h.Card(0).Badge);
    }

    [Fact]
    public async Task ReviewVm_MapClickSelectsGroupAndSendsSelect()
    {
        using var h = ReviewHarness.Create(TestPlans.CouncilAnvil(), tuning: new Tuning(25, 1));
        await h.SettleAsync();
        var sent = new List<string>();
        using var bridge = new MapBridge(sent.Add, h.Log, h.Time);
        h.Vm.Map = bridge;
        Assert.IsType<MapSetData>(MapBridge.ParseHostMessage(sent[0]));

        var anvil = h.Vm.Plan.Groups[1];
        bridge.Dispatch($$"""{"v":1,"type":"click","itemIds":["{{anvil.Videos[0].CardRelPath}}"],"groupId":"{{anvil.Id.Anchor.CardRelPath}}","ctrl":false,"shift":false}""");

        Assert.Same(h.Card(1), h.Vm.Videos.SelectedEntry);
        var select = sent.Select(MapBridge.ParseHostMessage).OfType<MapSelect>().Last();
        Assert.Equal(anvil.Id.Anchor.CardRelPath, select.GroupId);
    }

    [Fact]
    public async Task ReviewVm_LowerRevisionIsIgnored()
    {
        using var h = ReviewHarness.Create(TestPlans.CouncilAnvil());
        await h.SettleAsync();
        var current = h.Vm.Plan;
        var stale = current with { Revision = current.Revision - 1, Groups = [] };

        h.Vm.OnPlanArrived(stale);
        Assert.Same(current, h.Vm.Plan);
    }

    [Fact]
    public async Task ReviewVm_MapBase_StartsFromSettingsAndSendsSetBase()
    {
        using var h = ReviewHarness.Create(TestPlans.Zachar());
        await h.SettleAsync();
        var sent = new List<string>();
        using var bridge = new MapBridge(sent.Add, h.Log, h.Time);
        h.Vm.Map = bridge;
        Assert.Equal("streets", h.Vm.MapBase);

        h.Vm.MapBase = "satellite";

        Assert.Equal("satellite", Assert.IsType<MapSetBase>(MapBridge.ParseHostMessage(sent[^1])).Base);
    }

    [Fact]
    public async Task ReviewVm_GoTo_SelectsTheCardAndClip_OrThePhotoDay()
    {
        var photo = PhotosOtherTabTests.Photo("DJI_20260725200000_0101_D.DNG", TestPlans.Utc(2026, 7, 26, 4, 0), new IsNew(NewReason.DayHasNewVideos, null));
        using var h = ReviewHarness.Create(TestPlans.CouncilAnvil(), tuning: new Tuning(25, 1), extraItems: [photo],
            photoDays: [new PhotoDay(new DateOnly(2026, 7, 25), TestPlans.Anchorage, [photo.Raw.Unit.Id], "day has new videos")]);
        await h.SettleAsync();
        var anvil2 = TestPlans.Id(TestPlans.CouncilAnvil()[3].Name);
        var picked = new List<IReadOnlyList<ItemId>>();
        h.Vm.Videos.ClipSelectionChanged += picked.Add;

        h.Vm.GoTo(anvil2);
        Assert.Equal(0, h.Vm.SelectedTab);
        Assert.Same(h.Card(1), h.Vm.Videos.SelectedEntry);
        Assert.Equal<ItemId>([anvil2], h.Vm.Videos.SelectedClipIds);
        Assert.Equal<ItemId>([anvil2], picked[^1]);

        h.Vm.GoTo(photo.Raw.Unit.Id);
        Assert.Equal(1, h.Vm.SelectedTab);
        Assert.Equal(new DateOnly(2026, 7, 25), h.Vm.Photos.SelectedDay!.Date);
    }

    [Fact]
    public async Task ReviewVm_MoveTargetsAndMoveSelectedToGroup()
    {
        using var h = ReviewHarness.Create(TestPlans.CouncilAnvil(), tuning: new Tuning(25, 1));
        await h.SettleAsync();
        var c118 = TestPlans.Id(TestPlans.CouncilAnvil()[1].Name);
        h.Vm.Videos.SelectedEntry = h.Card(0);
        h.Vm.Videos.SetSelectedClips([c118]);

        var target = Assert.Single(h.Vm.MoveTargets());
        Assert.Same(h.Card(1), target);

        await h.Vm.MoveSelectedToGroupAsync(target);
        await h.SettleAsync();

        var move = Assert.IsType<MoveToGroup>(Assert.Single(h.Session.ToDraft().Edits));
        Assert.Equal<ItemId>([c118], move.Items);
        Assert.Equal(target.Anchor, move.InTarget);
        Assert.Contains(h.Vm.Plan.Groups, g => g.Videos.Contains(c118) && g.Videos.Length == 3);
        Assert.True(h.Vm.CanUndo);
    }

    /// <summary>Derives the committed tuning; any other tuning (a slider preview) throws.</summary>
    private sealed class FaultingDeriver(Tuning ok) : IPlanDeriver
    {
        private readonly ScriptedDeriver _inner = new();

        public Plan Derive(PlanBase b, Tuning t, IReadOnlyList<PlanEdit> edits, SessionFlags flags, int revision, CancellationToken ct)
            => t == ok ? _inner.Derive(b, t, edits, flags, revision, ct) : throw new InvalidOperationException("derive exploded");
    }

    [Fact]
    public async Task ReviewVm_SessionFaulted_ShowsErrorInfoBarAndLogs_UntilDisposed()
    {
        var ct = TestContext.Current.CancellationToken;
        var ui = new FakeUiDispatcher();
        var log = new ListLog();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 28, 2, 0, 0, TimeSpan.Zero));
        var session = new PlanSession(TestPlans.Base(TestPlans.Zachar()), new FaultingDeriver(new Tuning()), new Tuning(), time);
        var vm = new ReviewVm(session, Fake.Services(ui, time, log: log), new LedgerDecisionService(Fake.Ledger(), time, "PC1"));

        ((ITuningHost)vm).Preview(new Tuning(30, 1));
        await Eventually.TrueAsync(() => vm.InfoBars.Any(i => i.Key == "sessionFault"), ui);

        var bar = Assert.Single(vm.InfoBars, i => i.Key == "sessionFault");
        Assert.Equal(InfoSeverity.Error, bar.Severity);
        Assert.True(bar.IsClosable);
        Assert.Equal("Couldn't update the plan: derive exploded", bar.Message);
        Assert.Contains(log.Warnings, w => w.Contains("derive exploded", StringComparison.Ordinal));
        vm.CloseInfoBar("sessionFault");
        Assert.DoesNotContain(vm.InfoBars, i => i.Key == "sessionFault");

        vm.Dispose();
        var warned = log.Warnings.Count;
        var seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Faulted += _ => seen.TrySetResult();   // raised after any handler the VM still had
        session.Preview(new Tuning(40, 1));
        await seen.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);

        Assert.Equal(0, ui.Pending);
        Assert.Equal(warned, log.Warnings.Count);
    }
}
