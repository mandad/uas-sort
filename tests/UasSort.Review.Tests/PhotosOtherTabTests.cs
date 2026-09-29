// tests/UasSort.Review.Tests/PhotosOtherTabTests.cs
namespace UasSort.Review.Tests;

public class PhotosOtherTabTests
{
    private static readonly DateOnly Jul25 = new(2026, 7, 25);

    internal static Item Photo(string name, DateTime utc, Newness newness, long bytes = 30_000_000)
    {
        var id = new ItemId("DCIM/DJI_001/" + name);
        var entry = new CardEntry(id.CardRelPath, bytes, utc, utc, utc, 0x20, EntryClass.Photo, null);
        var raw = new RawItem(new PhotoUnit(id, entry, null), ItemKind.Photo, name, bytes, utc, null, null, null, null);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById(TestPlans.Anchorage));
        return new Item(raw, new ItemTime(utc, TimeSource.DroneClockZone, TestPlans.Anchorage, TzSource.Gps, DateOnly.FromDateTime(local), local),
                        null, null, ItemFlags.NoGps, newness);
    }

    private static (PlanIndex Ix, List<Item> Photos) PhotoPlan(IReadOnlyList<PlanEdit>? edits = null) => DayPlan(
        [
            Photo("DJI_20260725200000_0101_D.DNG", TestPlans.Utc(2026, 7, 26, 4, 0), new IsNew(NewReason.DayHasNewVideos, null)),
            Photo("DJI_20260725200100_0102_D.DNG", TestPlans.Utc(2026, 7, 26, 4, 1), new ProbablyImported("videos from this day are already in the library")),
            Photo("DJI_20260725200200_0103_D.DNG", TestPlans.Utc(2026, 7, 26, 4, 2), new Decided(DecisionKind.AssumedImported, TestPlans.Utc(2026, 10, 4, 18, 0), "PC1")),
        ], edits);

    private static (PlanIndex Ix, List<Item> Photos) DayPlan(List<Item> photos, IReadOnlyList<PlanEdit>? edits = null)
    {
        var day = new PhotoDay(Jul25, TestPlans.Anchorage, [.. photos.Select(p => p.Raw.Unit.Id)], "probably imported: videos from this day already in library");
        var b = TestPlans.Base(TestPlans.CouncilAnvil(), extraItems: photos, photoDays: [day]);
        var plan = new ScriptedDeriver().Derive(b, new Tuning(), edits ?? [], new SessionFlags(false), 1, TestContext.Current.CancellationToken);
        return (new PlanIndex(plan), photos);
    }

    [Fact]
    public async Task PhotosTab_DayRowTriStateAndToggle()
    {
        var applied = new List<PlanEdit>();
        var tab = new PhotosTabVm(e => { applied.Add(e); return Task.CompletedTask; }, _ => Task.CompletedTask);
        tab.Update(PhotoPlan().Ix);

        var day = Assert.Single(tab.Days);
        Assert.Equal("Jul 25 (Sat) · AKDT", day.DayText);
        Assert.Equal("3 photos · 90 MB", day.CountsText);
        Assert.Equal("1 new · 1 probably imported · 1 confirmed by you", day.StatusText);
        Assert.Null(day.IsIncluded);
        Assert.Equal("Photos · 1 day", tab.Header);

        await day.ToggleCommand.ExecuteAsync(null);
        Assert.Equal(new SetDayIncluded(Jul25, true), Assert.Single(applied));
    }

    [Fact]
    public void PhotosTab_AllIncludedDay_IsChecked()
    {
        var tab = new PhotosTabVm(_ => Task.CompletedTask, _ => Task.CompletedTask);
        tab.Update(PhotoPlan([new SetDayIncluded(Jul25, true)]).Ix);
        Assert.True(tab.Days[0].IsIncluded);
    }

    /// <summary>A day of one new photo and one confirmed-by-you photo: only the new one can be included (Planner.Derive's
    /// Includable), so with it included the box reads checked and the next click excludes the day.</summary>
    [Fact]
    public async Task PhotosTab_MixedDay_AllIncludableIncluded_IsChecked_AndTheToggleExcludes()
    {
        var applied = new List<PlanEdit>();
        var tab = new PhotosTabVm(e => { applied.Add(e); return Task.CompletedTask; }, _ => Task.CompletedTask);
        tab.Update(DayPlan(
        [
            Photo("DJI_20260725200000_0101_D.DNG", TestPlans.Utc(2026, 7, 26, 4, 0), new IsNew(NewReason.DayHasNewVideos, null)),
            Photo("DJI_20260725200200_0103_D.DNG", TestPlans.Utc(2026, 7, 26, 4, 2), new Decided(DecisionKind.AssumedImported, TestPlans.Utc(2026, 10, 4, 18, 0), "PC1")),
        ]).Ix);

        var day = Assert.Single(tab.Days);
        Assert.True(day.IsIncluded);
        await day.ToggleCommand.ExecuteAsync(null);
        Assert.Equal(new SetDayIncluded(Jul25, false), Assert.Single(applied));
    }

    [Fact]
    public void PhotosTab_DayOfOnlyConfirmedPhotos_IsUnchecked()
    {
        var tab = new PhotosTabVm(_ => Task.CompletedTask, _ => Task.CompletedTask) { ShowImportedDays = true };
        tab.Update(DayPlan(
        [
            Photo("DJI_20260725200200_0103_D.DNG", TestPlans.Utc(2026, 7, 26, 4, 2), new Decided(DecisionKind.AssumedImported, TestPlans.Utc(2026, 10, 4, 18, 0), "PC1")),
        ]).Ix);
        Assert.False(Assert.Single(tab.Days).IsIncluded);
    }

    /// <summary>A click the plan ignores (a tile that can't be included, a read-only session) still re-raises IsIncluded, so the
    /// box the click flipped goes back to the plan's value.</summary>
    [Fact]
    public async Task PhotosTab_ToggleThatChangesNothing_ReRaisesIsIncluded()
    {
        var tab = new PhotosTabVm(_ => Task.CompletedTask, _ => Task.CompletedTask);
        tab.Update(PhotoPlan().Ix);
        tab.SelectedDay = tab.Days[0];
        var tile = Assert.Single(tab.Tiles, t => t.CanUndo);         // the confirmed-by-you photo: not includable
        var day = tab.Days[0];
        var raised = new List<string>();
        tile.PropertyChanged += (_, e) => raised.Add("tile." + e.PropertyName);
        day.PropertyChanged += (_, e) => raised.Add("day." + e.PropertyName);

        await tile.ToggleCommand.ExecuteAsync(null);
        await day.ToggleCommand.ExecuteAsync(null);

        Assert.Equal<string>(["tile.IsIncluded", "day.IsIncluded"], raised);
        Assert.False(tile.IsIncluded);
    }

    [Fact]
    public async Task PhotosTab_ConfirmedTileOffersUndo()
    {
        var undone = new List<Item>();
        var tab = new PhotosTabVm(_ => Task.CompletedTask, items => { undone.AddRange(items); return Task.CompletedTask; });
        var (ix, photos) = PhotoPlan();
        tab.Update(ix);
        tab.SelectedDay = tab.Days[0];

        Assert.Equal(3, tab.Tiles.Count);
        var confirmed = Assert.Single(tab.Tiles, t => t.CanUndo);
        Assert.Equal("Confirmed by you", confirmed.StatusText);
        await confirmed.UndoCommand.ExecuteAsync(null);
        Assert.Equal(photos[2], Assert.Single(undone));
    }

    [Fact]
    public async Task OtherTab_SectionsAndUndismiss()
    {
        var undismissed = new List<Item>();
        var tab = new OtherTabVm(items => { undismissed.AddRange(items); return Task.CompletedTask; }, _ => Task.CompletedTask);
        var t = TestPlans.Utc(2026, 7, 26, 3, 0);
        ImmutableArray<CardEntry> extra =
        [
            new("DCIM/DJI_A001/x.MP4", 5_000_000, t, t, t, 0x20, EntryClass.Unknown, null),
            new("DCIM/DJI_001/DJI_20260725192655_0117_D.LRF", 1_000, t, t, t, 0x20, EntryClass.Skip, "LRF"),
            new("DCIM/DJI_001/DJI_20260725194000_0118_D.LRF", 1_000, t, t, t, 0x20, EntryClass.Skip, "LRF"),
        ];
        var clips = TestPlans.CouncilAnvil().ToList();
        clips[3] = clips[3] with { Newness = new Decided(DecisionKind.Dismissed, TestPlans.Utc(2026, 10, 4, 18, 0), "PC1") };
        var b = TestPlans.Base(clips, extraEntries: extra,
                               warnings: [new ScanWarning("EnumerationError", "Can't list DCIM\\DJI_002", "DCIM/DJI_002", true)]);
        var plan = new ScriptedDeriver().Derive(b, new Tuning(), [], new SessionFlags(false), 1, TestContext.Current.CancellationToken);
        tab.Update(new PlanIndex(plan));

        Assert.Equal<string>(["Unknown files", "Skipped by rule", "Scan warnings", "Dismissed by you"], tab.Sections.Select(s => s.Title));
        Assert.Equal("Not copied by uas-sort; copy manually if needed", tab.Sections[0].Note);
        Assert.Equal("LRF · 2 files", Assert.Single(tab.Sections[1].Rows).Text);
        Assert.True(tab.Sections[1].IsCollapsed);
        Assert.Equal("Other · 5", tab.Header);

        await tab.Sections[3].Rows[0].UndismissCommand!.ExecuteAsync(null);
        Assert.Equal(TestPlans.Id(clips[3].Name), Assert.Single(undismissed).Raw.Unit.Id);
    }

    [Fact]
    public void Decisions_RecordAndRevokeWriteLedgerRecords()
    {
        var store = Fake.Ledger();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 4, 18, 0, 0, TimeSpan.Zero));
        var svc = new LedgerDecisionService(store, time, "PC1");
        var photo = Photo("DJI_20260725200000_0101_D (2).DNG", TestPlans.Utc(2026, 7, 26, 4, 0), new ProbablyImported("x"));

        var ids = svc.Record(DecisionKind.AssumedImported, DecisionTargets.For(photo), "recorded on the Verdict page");
        svc.Revoke(ids);

        var records = store.Writer.Records;
        var d = Assert.IsType<DecisionRecord>(records[0]);
        Assert.Equal("assumedImported", d.Kind);
        Assert.Equal("DJI_20260725200000_0101_D (2).DNG", d.Name);
        Assert.Equal("PC1", d.Machine);
        Assert.Equal(time.GetUtcNow().UtcDateTime, d.At);
        var r = Assert.IsType<RevokeRecord>(records[1]);
        Assert.Equal(d.Id, r.Decision);
        Assert.Equal("PC1", r.Machine);
        Assert.Equal(new FileKey("dji_20260725200000_0101_d.dng", 30_000_000), FileKey.OfPath(d.Src, d.Size));

        var ledger = TestPlans.Ledger() with
        {
            Decisions = ImmutableDictionary<FileKey, LedgerDecision>.Empty.Add(FileKey.OfPath(d.Src, d.Size),
                new LedgerDecision(d.Id, FileKey.OfPath(d.Src, d.Size), DecisionKind.AssumedImported, d.At, "PC1", null, d.Why)),
        };
        Assert.Equal<string>([d.Id], svc.DecisionIdsFor(DecisionTargets.For(photo), ledger));
    }

    private static LedgerDecision Decision(string id, CardEntry file, DecisionKind kind)
        => new(id, FileKey.OfPath(file.RelPath, file.Size), kind, TestPlans.Utc(2026, 10, 4, 18, 0), "PC1", null, "recorded on the Verdict page");

    [Fact]
    public void Decisions_PairedPhoto_TargetsAndRevokesTheDngAndItsJpgTwin()
    {
        var utc = TestPlans.Utc(2026, 7, 26, 4, 0);
        var lone = Photo("DJI_20260725200000_0101_D.DNG", utc, new Decided(DecisionKind.AssumedImported, utc, "PC1"));
        var unit = (PhotoUnit)lone.Raw.Unit;
        var twin = new CardEntry("DCIM/DJI_001/DJI_20260725200000_0101_D.JPG", 8_000_000, utc, utc, utc, 0x20, EntryClass.Photo, null);
        var paired = lone with { Raw = lone.Raw with { Unit = unit with { JpgTwin = twin } } };

        var targets = DecisionTargets.For(paired);
        Assert.Equal<CardEntry>([unit.Primary, twin], targets.Select(t => t.File));

        var store = Fake.Ledger();
        var svc = new LedgerDecisionService(store, new FakeTimeProvider(new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero)), "PC1");
        var ledger = TestPlans.Ledger() with
        {
            Decisions = ImmutableDictionary<FileKey, LedgerDecision>.Empty
                .Add(FileKey.OfPath(unit.Primary.RelPath, unit.Primary.Size), Decision("d1", unit.Primary, DecisionKind.AssumedImported))
                .Add(FileKey.OfPath(twin.RelPath, twin.Size), Decision("d2", twin, DecisionKind.AssumedImported)),
        };
        var ids = svc.DecisionIdsFor(targets, ledger);
        Assert.Equal<string>(["d1", "d2"], ids.Order(StringComparer.Ordinal));

        svc.Revoke(ids);
        Assert.Equal<string>(["d1", "d2"], store.Writer.Records.OfType<RevokeRecord>().Select(r => r.Decision).Order(StringComparer.Ordinal));
        Assert.Single(DecisionTargets.For(lone));
    }

    [Fact]
    public async Task OtherTab_UnknownFileDismissedOnTheVerdictPage_ListedUnderDismissedWithUndismiss()
    {
        var undismissedEntries = new List<CardEntry>();
        var tab = new OtherTabVm(_ => Task.CompletedTask, entries => { undismissedEntries.AddRange(entries); return Task.CompletedTask; });
        var t = TestPlans.Utc(2026, 7, 26, 3, 0);
        var dismissed = new CardEntry("DCIM/DJI_A001/x.MP4", 5_000_000, t, t, t, 0x20, EntryClass.Unknown, null);
        var other = new CardEntry("DCIM/DJI_A001/y.BIN", 7_000_000, t, t, t, 0x20, EntryClass.Unknown, null);
        var ledger = TestPlans.Ledger() with
        {
            Decisions = ImmutableDictionary<FileKey, LedgerDecision>.Empty
                .Add(FileKey.OfPath(dismissed.RelPath, dismissed.Size), Decision("d1", dismissed, DecisionKind.Dismissed)),
        };
        var b = TestPlans.Base(TestPlans.CouncilAnvil(), ledger: ledger, extraEntries: [dismissed, other]);
        var plan = new ScriptedDeriver().Derive(b, new Tuning(), [], new SessionFlags(false), 1, TestContext.Current.CancellationToken);
        tab.Update(new PlanIndex(plan));

        Assert.Equal<string>(["Unknown files", "Dismissed by you"], tab.Sections.Select(s => s.Title));
        Assert.Equal("DCIM/DJI_A001/y.BIN", Assert.Single(tab.Sections[0].Rows).Text);
        var row = Assert.Single(tab.Sections[1].Rows);
        Assert.Equal("DCIM/DJI_A001/x.MP4", row.Text);
        Assert.Equal("5 MB · marked not needed", row.Detail);
        Assert.Equal("Other · 2", tab.Header);

        await row.UndismissCommand!.ExecuteAsync(null);
        Assert.Equal(dismissed, Assert.Single(undismissedEntries));
    }

    [Fact]
    public async Task ReviewVm_UndismissUnknownFile_RevokesItsDecisionAndRescans()
    {
        var t = TestPlans.Utc(2026, 7, 26, 3, 0);
        var dismissed = new CardEntry("DCIM/DJI_A001/x.MP4", 5_000_000, t, t, t, 0x20, EntryClass.Unknown, null);
        var ledger = TestPlans.Ledger() with
        {
            Decisions = ImmutableDictionary<FileKey, LedgerDecision>.Empty
                .Add(FileKey.OfPath(dismissed.RelPath, dismissed.Size), Decision("d1", dismissed, DecisionKind.Dismissed)),
        };
        using var h = ReviewHarness.Create([], planBase: TestPlans.Base(TestPlans.CouncilAnvil(), ledger: ledger, extraEntries: [dismissed]));
        var rescans = 0;
        h.Vm.RescanRequested += () => rescans++;

        var row = Assert.Single(h.Vm.Other.Sections.Single(s => s.Title == "Dismissed by you").Rows);
        await row.UndismissCommand!.ExecuteAsync(null);

        Assert.Equal("d1", Assert.Single(h.Ledger.Writer.Records.OfType<RevokeRecord>()).Decision);
        Assert.Equal(1, rescans);
    }
}
