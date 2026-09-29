// tests/UasSort.Review.Tests/GroupCardVmTests.cs
namespace UasSort.Review.Tests;

public class GroupCardVmTests
{
    private static Plan Derive(IReadOnlyList<PlanClip> clips, double r = 50, IReadOnlyList<PlanEdit>? edits = null,
                               ImmutableArray<Suggestion> suggestions = default, LibraryListings? listings = null)
        => new ScriptedDeriver(suggestions).Derive(TestPlans.Base(clips, listings: listings), new Tuning(r, 1), edits ?? [],
                                                   new SessionFlags(false), 1, TestContext.Current.CancellationToken);

    private static GroupCardVm Card(Plan plan, int index, RecordingActions actions)
    {
        var card = new GroupCardVm(plan.Groups[index].Id, actions);
        card.Update(plan.Groups[index], new PlanIndex(plan));
        return card;
    }

    [Fact]
    public async Task GroupCard_DistanceChip_MergesThroughActions()
    {
        var actions = new RecordingActions();
        var plan = Derive(TestPlans.CouncilAnvil(), r: 25);
        var second = Card(plan, 1, actions);

        Assert.NotNull(second.Chip);
        Assert.Equal("── 34 mi jump · 24 h ──", second.Chip!.Text);
        Assert.Equal("Merge", second.Chip.ButtonText);
        Assert.True(second.Chip.CanMerge);
        await second.Chip.MergeCommand.ExecuteAsync(null);
        Assert.Equal($"merge {plan.Groups[0].Id.Anchor.CardRelPath} {plan.Groups[1].Id.Anchor.CardRelPath}", Assert.Single(actions.Calls));
        Assert.Null(Card(plan, 0, actions).Chip);
    }

    [Fact]
    public void GroupCard_UserSplitChip_OffersUndoSplit()
    {
        var anvil = TestPlans.Id(TestPlans.CouncilAnvil()[2].Name);
        var plan = Derive(TestPlans.CouncilAnvil(), edits: [new SplitBefore(anvil)]);
        var card = Card(plan, 1, new RecordingActions());

        Assert.Equal("── split by you ──", card.Chip!.Text);
        Assert.Equal("Undo split", card.Chip.ButtonText);
    }

    [Fact]
    public void GroupCard_LibraryFolderChip_CannotMerge()
    {
        var b = new Boundary(new GroupId(new ItemId("a")), new GroupId(new ItemId("b")), BoundaryCause.LibraryFolder, null, TimeSpan.FromDays(3), 3);
        var chip = new BoundaryChipVm(b, new RecordingActions());
        Assert.Equal("── different library folder ──", chip.Text);
        Assert.False(chip.CanMerge);
        Assert.False(chip.MergeCommand.CanExecute(null));
        Assert.Equal("These clips are already in two different folders", chip.MergeTooltip);
        var dayGap = new BoundaryChipVm(b with { Cause = BoundaryCause.DayGap, DayGap = 62 }, new RecordingActions());
        Assert.Equal("── 62 days ──", dayGap.Text);
    }

    [Fact]
    public async Task GroupCard_EmphasisedDaySplit_ChipSplitsWithOneQuickFix()
    {
        var actions = new RecordingActions();
        var plan = Derive(TestPlans.CouncilAnvil());
        var card = Card(plan, 0, actions);

        var chip = Assert.Single(card.Chips, c => c.Kind == ChipKind.EmphasisedSplit);
        Assert.Equal("2 days · 34 mi apart", chip.Text);
        await Assert.Single(chip.Actions).Command.ExecuteAsync(null);
        var fix = Assert.Single(actions.Fixes);
        Assert.Equal(new SplitBefore(TestPlans.Id(TestPlans.CouncilAnvil()[2].Name)), Assert.Single(fix.Edits));
    }

    [Fact]
    public void GroupCard_ClockChip_OnlyOnGroupsWithAMismatchMember()
    {
        var clips = TestPlans.CouncilAnvil().Concat(TestPlans.Zachar()).ToList();
        var plan = Derive(clips);
        var cards = Enumerable.Range(0, plan.Groups.Length).Select(i => Card(plan, i, new RecordingActions())).ToList();

        var withChip = cards.Where(c => c.Chips.Any(ch => ch.Kind == ChipKind.ClockMismatch)).ToList();
        var zachar = Assert.Single(withChip);
        Assert.Equal(TestPlans.Id(TestPlans.Zachar()[0].Name), zachar.Anchor);
        var chip = zachar.Chips.Single(ch => ch.Kind == ChipKind.ClockMismatch);
        Assert.Equal("clock ≠ local", chip.Text);
        Assert.Equal("The drone clock (UTC−4) doesn't match local time here (UTC−8). Dates use local time.", chip.Tooltip);
        Assert.True(zachar.HasClockMismatch);
    }

    [Fact]
    public void GroupCard_NewFolderFields()
    {
        var plan = Derive(TestPlans.Zachar());
        var card = Card(plan, 0, new RecordingActions());

        Assert.Equal("NEW FOLDER", card.Badge);
        Assert.Equal(@"2026\2026-09\2026-09-27", card.TargetPath);
        Assert.False(card.IsDescriptionReadOnly);
        Assert.Equal("Sep 27 10:01–10:24 AKDT", card.DateRangeText);
        Assert.Equal("Videos 3 new / 3 · 3.6 GB", card.VideoCountsText);
        Assert.Equal("#1F77B4", card.Swatch);
        Assert.Equal(3, card.Thumbs.Count);
        Assert.Null(card.MoreThumbsText);
        Assert.Contains(card.Chips, c => c.Kind == ChipKind.EmptyName && c.Text == "Name this folder");
    }

    [Fact]
    public void GroupCard_SuggestionPrefill_IsItalicAndSuggestionVmToStringIsText()
    {
        var plan = Derive(TestPlans.Zachar(), suggestions: [new Suggestion("Zachar Bay", DescSource.Feature, Distance.FromMiles(0.4), new DateOnly(2026, 9, 27))]);
        var card = Card(plan, 0, new RecordingActions());

        Assert.Equal("Zachar Bay", card.Description);
        Assert.True(card.DescriptionIsSuggestion);
        var s = Assert.Single(card.Suggestions);
        Assert.Equal("Zachar Bay", s.ToString());
        Assert.Equal("feature · 0.4 mi", s.Detail);
        Assert.Equal("near Zachar Bay · spread 0.4 mi", card.LocationText);
    }

    [Fact]
    public void GroupCard_Append_IsReadOnlyWithHint()
    {
        var anchor = TestPlans.Id(TestPlans.Zachar()[0].Name);
        var folder = TestPlans.VideoRoot + @"\2026\2026-09\2026-09-27 Zachar Bay";
        var plan = Derive(TestPlans.Zachar(), edits: [new Retarget(anchor, new AppendTo(folder), false, [anchor])]);
        var card = Card(plan, 0, new RecordingActions());

        Assert.Equal("APPEND", card.Badge);
        Assert.True(card.IsDescriptionReadOnly);
        Assert.Equal("Appending to an existing folder · [New folder instead]", card.ReadOnlyHint);
        Assert.Equal(@"2026\2026-09\2026-09-27 Zachar Bay", card.TargetPath);
    }

    [Fact]
    public void GroupCard_RetargetOptions_AutoNewCandidatesBrowseSkip()
    {
        var folder = TestPlans.VideoRoot + @"\2026\2026-09\2026-09-26 Kodiak";
        var t = TestPlans.Utc(2026, 9, 26, 20, 0);
        ImmutableArray<FsEntry> entries =
        [
            new(TestPlans.VideoRoot + @"\2026", "2026", true, 0, t, t, t, 0x10),
            new(TestPlans.VideoRoot + @"\2026\2026-09", @"2026\2026-09", true, 0, t, t, t, 0x10),
            new(folder, @"2026\2026-09\2026-09-26 Kodiak", true, 0, t, t, t, 0x10),
            new(folder + @"\DJI_20260926120000_0100_D.MP4", @"2026\2026-09\2026-09-26 Kodiak\DJI_20260926120000_0100_D.MP4", false, 5_000_000, t, t, t, 0x20),
        ];
        var plan = Derive(TestPlans.Zachar(), listings: TestPlans.Listings(entries));
        var card = Card(plan, 0, new RecordingActions());

        var options = card.RetargetOptions();
        Assert.Equal<RetargetKind>([RetargetKind.Auto, RetargetKind.NewFolder, RetargetKind.Append, RetargetKind.Browse, RetargetKind.Skip],
                                   options.Select(o => o.Kind));
        var candidate = options[2];
        Assert.Equal("Kodiak", candidate.Label);
        Assert.StartsWith("Sep 26 · ", candidate.Detail, StringComparison.Ordinal);
        Assert.Equal("Browse existing…", options[3].Label);
        Assert.Equal("Skip this group", options[4].ToString());
    }

    [Fact]
    public async Task ClipRow_FieldsBannerAndToggle()
    {
        var actions = new RecordingActions();
        var plan = Derive(TestPlans.CouncilAnvil());
        var ix = new PlanIndex(plan);
        var g = plan.Groups[0];
        var anvil = TestPlans.Id(TestPlans.CouncilAnvil()[2].Name);
        var row = new ClipRowVm(anvil, actions);
        row.Update(ix.Items[anvil], plan.Included.Contains(anvil), g, readOnly: false);

        Assert.Equal("DJI_20260726195645_0001_D.MP4", row.ToString());
        Assert.Equal("19:56 AKDT", row.TimeText);
        Assert.Equal("1.2 GB", row.SizeText);
        Assert.Equal("New", row.StatusText);
        Assert.True(row.IsIncluded);
        Assert.NotNull(row.Banner);
        Assert.Equal("── Jul 25 → Jul 26 · 34 mi apart · 24 h ──", row.Banner!.Text);
        Assert.True(row.Banner.IsEmphasised);
        Assert.Equal("Likely separate outing", row.Banner.EmphasisText);

        await row.ToggleIncludedCommand.ExecuteAsync(null);
        await row.Banner.SplitCommand.ExecuteAsync(null);
        Assert.Equal<string>(["include 1 False", "split " + anvil.CardRelPath], actions.Calls);
    }

    [Fact]
    public void ClipRow_TruncatedClipShowsUnfinishedPill()
    {
        var clip = TestPlans.Zachar()[0] with { Flags = ItemFlags.Truncated };
        var plan = Derive([clip]);
        var row = new ClipRowVm(TestPlans.Id(clip.Name), new RecordingActions());
        row.Update(new PlanIndex(plan).Items[TestPlans.Id(clip.Name)], false, plan.Groups[0], false);

        Assert.Equal("Unfinished", row.StatusText);
        Assert.StartsWith("Unfinished recording. Powering the drone on", row.StatusTooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void FoldedRun_TextCountsGroups()
    {
        var plan = Derive(TestPlans.CouncilAnvil(), r: 25);
        var run = new FoldedRunVm("f:x");
        run.Update(plan.Groups);
        Assert.Equal("2 groups already in library", run.Text);
        Assert.Equal(4, run.ClipCount);
    }
}
