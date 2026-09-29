// src/UasSort.App/SelfTest/SelfTestChecks.Review.cs (Core namespaces come from GlobalUsings.Core.cs)
using System.Text;
using System.Text.Json;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace UasSort.App.SelfTest;

internal static partial class SelfTestChecks
{
    private static Task<SelfTestCheck> ProbeStill(SelfTestContext ctx)
    {
        using var dng = SelfTestFixture.Open("selftest.dng");
        var info = StillProbe.Read(dng);
        bool gps = info.Gps is GpsFix fix && Math.Abs(fix.Point.Lat - 57.5368) < 1e-4 && Math.Abs(fix.Point.Lon + 153.7484) < 1e-4;
        return Task.FromResult(info.DtoNaive == new DateTime(2026, 9, 27, 14, 5, 0) && gps && info.Model == "FC9113"
            ? SelfTestCheck.Pass("probe.still", "selftest.dng: DTO 2026-09-27 14:05:00, GPS Zachar Bay, FC9113")
            : SelfTestCheck.Fail("probe.still", $"DTO {info.DtoNaive:O}, gps {gps}, model {info.Model}"));
    }

    private static Task<SelfTestCheck> JsonLedger(SelfTestContext ctx)
    {
        var lines = Encoding.UTF8.GetString(SelfTestFixture.ReadAll("ledger-v1.jsonl"))
                            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var kinds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var record = JsonSerializer.Deserialize(line, LedgerJsonContext.Default.LedgerRecord)
                         ?? throw new JsonException("null record: " + line);
            var first = JsonSerializer.Serialize(record, LedgerJsonContext.Default.LedgerRecord);
            var again = JsonSerializer.Deserialize(first, LedgerJsonContext.Default.LedgerRecord);
            // compare serialised forms: records holding ImmutableDictionary/ImmutableArray have reference equality for those members
            if (again is null || again.GetType() != record.GetType()
                || JsonSerializer.Serialize(again, LedgerJsonContext.Default.LedgerRecord) != first
                || LedgerCodec.TryParse(line, out _) is null)             // Part 05's reader accepts the line too
                return Task.FromResult(SelfTestCheck.Fail("json.ledger", "round-trip changed " + line));
            kinds.Add(record.GetType().Name);
        }
        string[] expected = ["FileRecord", "FolderRecord", "SeenRecord", "DecisionRecord", "RevokeRecord", "RunRecord", "TornRecord", "CardDeleteRecord"];
        var missing = expected.Where(k => !kinds.Contains(k)).ToList();
        return Task.FromResult(missing.Count == 0
            ? SelfTestCheck.Pass("json.ledger", $"{lines.Length} lines, every record kind round-trips")
            : SelfTestCheck.Fail("json.ledger", "missing kinds: " + string.Join(", ", missing)));
    }

    /// <summary>Materialises the synthetic card once and scans it through Browse to folder; later checks reuse the ReviewVm.</summary>
    public static async Task<ReviewVm> EnsureReviewAsync(SelfTestContext ctx)
    {
        var shell = ctx.Services.Shell;
        if (ctx.Shared.ContainsKey("reviewScanned") && shell.Review is { } done) return done;
        SelfTestFixture.Materialize(ctx.Sandbox);
        ctx.Shared["reviewScanned"] = true;
        if (!await WaitUntilAsync(() => shell.Stage == Stage.Card && shell.Card is not null, TimeSpan.FromSeconds(10)))
            throw new InvalidOperationException("Card stage not reached (stage " + shell.Stage + ")");
        shell.Card!.Browse(ctx.Sandbox.CardRoot);                // CardChosen → ShellVm.UseCardAsync → Scan → Review
        if (!await WaitUntilAsync(() => shell.Stage == Stage.Review && shell.Review is not null, TimeSpan.FromSeconds(20)))
            throw new InvalidOperationException($"Review not reached: stage {shell.Stage}; refusal '{shell.Card?.Message}'");
        return shell.Review!;
    }

    private static async Task<SelfTestCheck> ReviewScan(SelfTestContext ctx)
    {
        var review = await EnsureReviewAsync(ctx);
        var groups = review.Videos.Timeline.OfType<GroupCardVm>().ToList();
        var chipText = groups.Count > 1 ? groups[1].Chip?.Text ?? "" : "";
        bool chip = groups.Count == 2 && chipText.Contains("63 days", StringComparison.Ordinal);
        bool photos = review.Photos.Days.Count == 1;
        return groups.Count == 2 && chip && photos
            ? SelfTestCheck.Pass("review.scan", $"2 groups ('{groups[0].Description}', '{groups[1].Description}'), chip '{chipText}', 1 photo day")
            : SelfTestCheck.Fail("review.scan", $"groups {groups.Count}, chip '{chipText}', photo days {review.Photos.Days.Count}");
    }

    public static async Task<ReviewPage> ReviewPageAsync(SelfTestContext ctx)
    {
        await EnsureReviewAsync(ctx);
        if (!await WaitUntilAsync(() => CurrentPage<ReviewPage>(ctx) is { IsLoaded: true }, TimeSpan.FromSeconds(10)))
            throw new InvalidOperationException("ReviewPage not shown");
        return CurrentPage<ReviewPage>(ctx)!;
    }

    private static async Task<SelfTestCheck> ReviewLayout(SelfTestContext ctx)
    {
        var page = await ReviewPageAsync(ctx);
        var tabs = page.Tabs.Items.Select(i => i.Text).ToList();
        int splitters = VisualTree.FindAll<GridSplitter>(page).Count;
        var offload = VisualTree.FindDescendant<Button>(page, b => (b.Content as string)?.StartsWith("Offload", StringComparison.Ordinal) == true);
        bool ok = tabs.Count == 3 && tabs[0].StartsWith("Videos", StringComparison.Ordinal) && tabs[1].StartsWith("Photos", StringComparison.Ordinal)
                  && tabs[2].StartsWith("Other", StringComparison.Ordinal) && splitters == 2 && offload is not null
                  && VisualTree.FindDescendant<MapPane>(page) is not null;
        return ok ? SelfTestCheck.Pass("review.layout", $"tabs [{string.Join(" | ", tabs)}], 2 splitters, map, Offload")
                  : SelfTestCheck.Fail("review.layout", $"tabs [{string.Join(" | ", tabs)}], splitters {splitters}, offload {offload is not null}");
    }

    /// <summary>Ref §9.2/§6.5: the fixture's drone clock is US Eastern while every clip is in Alaska → the Warning InfoBar
    /// on Review and a "clock ≠ local" chip on the cards.</summary>
    private static async Task<SelfTestCheck> ReviewClock(SelfTestContext ctx)
    {
        var page = await ReviewPageAsync(ctx);
        var bar = VisualTree.FindDescendant<InfoBar>(page, b => b.Severity == InfoBarSeverity.Warning
                      && b.Message.Contains("Drone clock is set to", StringComparison.Ordinal) && b.Message.Contains("Alaska", StringComparison.Ordinal));
        var groups = page.Vm.Videos.Timeline.OfType<GroupCardVm>().ToList();
        bool chips = groups.Count == 2 && groups.All(g => g.Chips.Any(c => c.Kind == ChipKind.ClockMismatch && c.Text == "clock ≠ local"));
        return bar is not null && chips
            ? SelfTestCheck.Pass("review.clock", "clock-mismatch InfoBar shown; 'clock ≠ local' on both cards")
            : SelfTestCheck.Fail("review.clock", $"infobar {bar is not null}, chips {chips}");
    }

    private static async Task<SelfTestCheck> ReviewTuning(SelfTestContext ctx)
    {
        var page = await ReviewPageAsync(ctx);
        var strip = VisualTree.FindDescendant<TuningStrip>(page)!;
        var review = page.Vm;
        var tuning = review.Tuning;
        strip.OnDragStarted();                                  // what PointerPressed (handledEventsToo) calls
        strip.RadiusSlider.Value = 30;
        strip.RadiusSlider.Value = 25;                          // two previews, latest wins
        await strip.OnDragEndedAsync();                         // what PointerReleased / PointerCaptureLost call: ONE undo entry
        if (!await WaitUntilAsync(() => review.CanUndo && tuning.RadiusMiles == 25, TimeSpan.FromSeconds(5)))
            return SelfTestCheck.Fail("review.tuning", $"after drag: R {tuning.RadiusMiles}, canUndo {review.CanUndo}");
        await review.UndoCommand.ExecuteAsync(null);
        bool back = await WaitUntilAsync(() => tuning.RadiusMiles == 50 && !review.CanUndo, TimeSpan.FromSeconds(5));
        return back ? SelfTestCheck.Pass("review.tuning", "drag 50→30→25 committed once; one undo restored 50 mi")
                    : SelfTestCheck.Fail("review.tuning", $"after undo: R {tuning.RadiusMiles}, canUndo {review.CanUndo}");
    }

    private static async Task<SelfTestCheck> ReviewMap(SelfTestContext ctx)
    {
        var page = await ReviewPageAsync(ctx);
        if (!await WaitUntilAsync(() => page.Map.IsReady || page.Map.IsUnavailable, TimeSpan.FromSeconds(15)))
            return SelfTestCheck.Fail("review.map", "map not ready");
        var groups = page.Vm.Videos.Timeline.OfType<GroupCardVm>().ToList();
        page.Vm.Videos.SelectedEntry = groups[1];                // selecting a card sends select + fit (Ref §9.6 Sync)
        var stats = await PollEvaluateAsync(page.Map, "JSON.stringify(window.__uas.stats())",
            s => s is not null && s.Contains("\"items\":3", StringComparison.Ordinal) && s.Contains("\"groups\":2", StringComparison.Ordinal)
                 && s.Contains("\"radius\":1", StringComparison.Ordinal), TimeSpan.FromSeconds(8));
        bool ok = stats is not null && stats.Contains("\"items\":3", StringComparison.Ordinal) && stats.Contains("\"radius\":1", StringComparison.Ordinal);
        return ok ? SelfTestCheck.Pass("review.map", "bridge drew " + stats) : SelfTestCheck.Fail("review.map", "stats " + (stats ?? "(none)"));
    }

    private static async Task<(ReviewPage Page, TimelineView Timeline, List<GroupCardVm> Groups)> TimelineAsync(SelfTestContext ctx)
    {
        var page = await ReviewPageAsync(ctx);
        page.ShowTab(0);
        var timeline = VisualTree.FindDescendant<TimelineView>(page) ?? throw new InvalidOperationException("no TimelineView");
        var groups = page.Vm.Videos.Timeline.OfType<GroupCardVm>().ToList();
        await WaitUntilAsync(() => groups.All(g => timeline.ContainerFor(g) is not null), TimeSpan.FromSeconds(5));
        return (page, timeline, groups);
    }

    private static async Task<SelfTestCheck> TemplateGroupCard(SelfTestContext ctx)
    {
        var (_, timeline, groups) = await TimelineAsync(ctx);
        var second = timeline.ContainerFor(groups[1]);
        var first = timeline.ContainerFor(groups[0]);
        if (first is null || second is null) return SelfTestCheck.Fail("template.groupCard", "cards not realised");
        var chip = VisualTree.FindDescendant<FrameworkElement>(second, e => e.Name == "ChipHeader");
        bool chipText = VisualTree.FindDescendant<TextBlock>(second, t => t.Text.Contains("63 days", StringComparison.Ordinal)) is not null;
        bool merge = VisualTree.FindDescendant<Button>(second, b => (b.Content as string) == "Merge") is not null;
        int thumbs = VisualTree.FindAll<Image>(first, i => Thumb.GetKey(i).CardRelPath.Length > 0).Count;
        bool badge = VisualTree.FindDescendant<TextBlock>(first, t => t.Text == groups[0].Badge && t.Text.Length > 0) is not null;
        bool ok = chip?.Visibility == Visibility.Visible && chipText && merge && thumbs == 2 && badge;
        return ok ? SelfTestCheck.Pass("template.groupCard", $"chip header '63 days' + [Merge], badge '{groups[0].Badge}', {thumbs} thumbnails")
                  : SelfTestCheck.Fail("template.groupCard", $"chip {chip?.Visibility} text {chipText} merge {merge} thumbs {thumbs} badge {badge}");
    }

    private static async Task<SelfTestCheck> TemplateSuggestion(SelfTestContext ctx)
    {
        var (page, timeline, groups) = await TimelineAsync(ctx);
        var box = VisualTree.FindDescendant<AutoSuggestBox>(timeline.ContainerFor(groups[0])!, b => b.Name == "DescriptionBox");
        if (box is null) return SelfTestCheck.Fail("template.suggestion", "no rename box on the first card");
        box.Focus(FocusState.Programmatic);
        box.IsSuggestionListOpen = true;                                     // the popup, opened in code (Ref §13)
        bool found = await WaitUntilAsync(() => VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot)
            .Any(p => p.Child is not null && VisualTree.FindDescendant<TextBlock>(p.Child, t => t.Text == "Anvil Mountain") is not null),
            TimeSpan.FromSeconds(5));
        bool noRecordText = !VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot)
            .Any(p => p.Child is not null && VisualTree.FindDescendant<TextBlock>(p.Child, t => t.Text.Contains("SuggestionVm", StringComparison.Ordinal)) is not null);
        box.IsSuggestionListOpen = false;
        return found && noRecordText ? SelfTestCheck.Pass("template.suggestion", "popup shows 'Anvil Mountain'")
                                     : SelfTestCheck.Fail("template.suggestion", $"found {found}, record text absent {noRecordText}");
    }

    private static async Task<SelfTestCheck> TemplateTargetMenu(SelfTestContext ctx)
    {
        var (_, timeline, groups) = await TimelineAsync(ctx);
        var button = VisualTree.FindDescendant<DropDownButton>(timeline.ContainerFor(groups[0])!, b => b.Name == "TargetButton");
        if (button?.Flyout is not MenuFlyout flyout) return SelfTestCheck.Fail("template.targetMenu", "no target DropDownButton");
        flyout.ShowAt(button);                                                  // Opening builds the items (Ref §9.4)
        await WaitUntilAsync(() => flyout.Items.Count > 0, TimeSpan.FromSeconds(3));
        var options = groups[0].RetargetOptions();
        var texts = flyout.Items.OfType<MenuFlyoutItem>().Select(i => i.Text).ToList();
        flyout.Hide();
        bool ok = texts.Count == options.Count
                  && options.Any(o => o.Kind == RetargetKind.Browse) && options.Any(o => o.Kind == RetargetKind.Skip)
                  && options.Any(o => o.Kind == RetargetKind.Auto) && options.Any(o => o.Kind == RetargetKind.NewFolder);
        return ok ? SelfTestCheck.Pass("template.targetMenu", "menu built on Opening: " + string.Join(" | ", texts))
                  : SelfTestCheck.Fail("template.targetMenu", "items: " + string.Join(" | ", texts));
    }

    private static async Task<SelfTestCheck> TemplateClipRow(SelfTestContext ctx)
    {
        var (page, _, groups) = await TimelineAsync(ctx);
        page.Vm.Videos.SelectedEntry = groups[0];                // the Anvil group: clip A (Jul 25) and clip B (Jul 26)
        var list = VisualTree.FindDescendant<ClipListView>(page)!;
        if (!await WaitUntilAsync(() => page.Vm.Videos.Clips.Count == 2 && list.ContainerFor(page.Vm.Videos.Clips[1]) is not null, TimeSpan.FromSeconds(5)))
            return SelfTestCheck.Fail("template.clipRow", $"clip rows: {page.Vm.Videos.Clips.Count}");
        var rowB = page.Vm.Videos.Clips[1];
        var c = list.ContainerFor(rowB)!;
        var banner = VisualTree.FindDescendant<FrameworkElement>(c, e => e.Name == "DayBanner");
        bool splitHere = VisualTree.FindDescendant<Button>(c, b => (b.Content as string) == "Split here") is not null;
        bool name = VisualTree.FindDescendant<TextBlock>(c, t => t.Text == rowB.Name) is not null;
        var img = VisualTree.FindDescendant<Image>(c, i => Thumb.GetKey(i) == rowB.ThumbKey);
        bool noBannerOnA = VisualTree.FindDescendant<FrameworkElement>(list.ContainerFor(page.Vm.Videos.Clips[0])!, e => e.Name == "DayBanner")?.Visibility != Visibility.Visible;
        bool ok = rowB.Banner is not null && banner?.Visibility == Visibility.Visible && splitHere && name && img is not null && noBannerOnA;
        return ok ? SelfTestCheck.Pass("template.clipRow", $"row '{rowB.Name}' carries banner '{rowB.Banner?.Text}' with [Split here]")
                  : SelfTestCheck.Fail("template.clipRow", $"banner {banner?.Visibility} split {splitHere} name {name} thumb {img is not null} noBannerOnA {noBannerOnA}");
    }

    private static async Task<SelfTestCheck> ThumbKeyRecheck(SelfTestContext ctx)
    {
        await EnsureReviewAsync(ctx);
        var cache = ctx.Services.Thumbnails;
        var a = new ItemId(SelfTestFixture.ClipA);
        var b = new ItemId(SelfTestFixture.ClipB);
        cache.Clear();
        var image = new Image();
        Thumb.SetKey(image, a);
        Thumb.SetKey(image, b);                                  // recycled before A's load finished
        var imgA = await cache.GetAsync(a);
        var imgB = await cache.GetAsync(b);
        await WaitUntilAsync(() => image.Source is not null, TimeSpan.FromSeconds(3));
        await Task.Delay(300);                                   // let any late completion of A run
        bool ok = imgA is not null && imgB is not null && !ReferenceEquals(imgA, imgB) && ReferenceEquals(image.Source, imgB);
        return ok ? SelfTestCheck.Pass("thumb.keyRecheck", "late load for the old key was not assigned; Source is the new key's image")
                  : SelfTestCheck.Fail("thumb.keyRecheck", $"A {imgA is not null}, B {imgB is not null}, source is B {ReferenceEquals(image.Source, imgB)}");
    }

    private static async Task<SelfTestCheck> TemplatePhotoTile(SelfTestContext ctx)
    {
        var page = await ReviewPageAsync(ctx);
        page.Vm.SelectedTab = 1;
        var photos = VisualTree.FindDescendant<PhotosTab>(page)!;
        page.Vm.Photos.SelectedDay = page.Vm.Photos.Days[0];
        if (!await WaitUntilAsync(() => page.Vm.Photos.Tiles.Count == 1 && photos.ContainerFor(page.Vm.Photos.Tiles[0]) is not null, TimeSpan.FromSeconds(5)))
            return SelfTestCheck.Fail("template.photoTile", $"tiles {page.Vm.Photos.Tiles.Count}");
        var tile = page.Vm.Photos.Tiles[0];
        var c = photos.ContainerFor(tile)!;
        bool thumb = VisualTree.FindDescendant<Image>(c, i => Thumb.GetKey(i).CardRelPath == SelfTestFixture.Dng) is not null;
        bool status = VisualTree.FindDescendant<TextBlock>(c, t => t.Text == tile.StatusText && t.Text.Length > 0) is not null;
        bool dayRow = VisualTree.FindDescendant<TextBlock>(photos.DayList, t => t.Text == page.Vm.Photos.Days[0].DayText) is not null;
        bool layout = photos.Wall.Layout is LinedFlowLayout;
        page.Vm.SelectedTab = 0;
        return thumb && status && dayRow && layout
            ? SelfTestCheck.Pass("template.photoTile", $"day '{page.Vm.Photos.Days[0].DayText}', tile '{tile.StatusText}' on a LinedFlowLayout wall")
            : SelfTestCheck.Fail("template.photoTile", $"thumb {thumb} status {status} day {dayRow} lined {layout}");
    }

    private static async Task<SelfTestCheck> TemplateOtherTab(SelfTestContext ctx)
    {
        var page = await ReviewPageAsync(ctx);
        page.Vm.SelectedTab = 2;
        // Ruling P11-C3: the synthetic card has no Other-tab content, so a probe section proves the template renders rows.
        var probe = new OtherSectionVm("Selftest section", "selftest note", (OtherRowVm[])[new OtherRowVm("selftest-row.txt", "1 KB", null)], false);   // explicit array: CsWinRT1032
        page.Vm.Other.Sections.Add(probe);
        try
        {
            var other = VisualTree.FindDescendant<OtherTab>(page)!;
            await WaitUntilAsync(() => other.IsLoaded, TimeSpan.FromSeconds(3));
            bool row = await WaitUntilAsync(() => VisualTree.FindDescendant<TextBlock>(other, t => t.Text == "selftest-row.txt") is not null, TimeSpan.FromSeconds(3));
            int expanders = VisualTree.FindAll<Expander>(other).Count;
            int sections = page.Vm.Other.Sections.Count;
            page.Vm.SelectedTab = 0;
            return row && expanders == sections && expanders >= 1
                ? SelfTestCheck.Pass("template.otherTab", $"{expanders} sections rendered; row 'selftest-row.txt'")
                : SelfTestCheck.Fail("template.otherTab", $"{expanders} expanders for {sections} sections; row {row}");
        }
        finally
        {
            page.Vm.Other.Sections.Remove(probe);
        }
    }
}
