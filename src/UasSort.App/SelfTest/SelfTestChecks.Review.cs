// src/UasSort.App/SelfTest/SelfTestChecks.Review.cs (Core namespaces come from GlobalUsings.Core.cs)
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Popup = Microsoft.UI.Xaml.Controls.Primitives.Popup;   // the Primitives namespace would make Thumb ambiguous

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
        string[] expected = ["FileRecord", "FolderRecord", "SeenRecord", "DecisionRecord", "RevokeRecord", "RunRecord", "TornRecord", "CardDeleteRecord", "PhotoDeleteRecord"];
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
        int splitters = VisualTree.FindAll<PaneSplitter>(page).Count;
        var offload = VisualTree.FindDescendant<Button>(page, b => (b.Content as string)?.StartsWith("Offload", StringComparison.Ordinal) == true);
        bool ok = tabs.Count == 3 && tabs[0].StartsWith("Videos", StringComparison.Ordinal) && tabs[1].StartsWith("Photos", StringComparison.Ordinal)
                  && tabs[2].StartsWith("Other", StringComparison.Ordinal) && splitters == 2 && offload is not null
                  && VisualTree.FindDescendant<MapPane>(page) is not null;
        return ok ? SelfTestCheck.Pass("review.layout", $"tabs [{string.Join(" | ", tabs)}], 2 splitters, map, Offload")
                  : SelfTestCheck.Fail("review.layout", $"tabs [{string.Join(" | ", tabs)}], splitters {splitters}, offload {offload is not null}");
    }

    /// <summary>Task U1 (user-reported crash): drives BOTH Review splitters through PaneSplitter's drag path — BeginDrag, DragTo,
    /// EndDrag, what the pointer handlers call — and the arrow-key nudge, and checks that the tracks moved, stopped at their
    /// Min sizes, and that the new layout reached Settings.Layout (ShellVm.UpdateLayout). Under Native AOT the toolkit
    /// GridSplitter threw InvalidCastException on every drag: an exception here fails this check, and one raised in a layout
    /// pass reaches App.UnhandledException, which under --selftest logs it and fails the run ("unhandled").</summary>
    private static async Task<SelfTestCheck> ReviewSplitters(SelfTestContext ctx)
    {
        var page = await ReviewPageAsync(ctx);
        page.Vm.SelectedTab = 0;
        page.ShowTab(0);
        page.UpdateLayout();
        int count = VisualTree.FindAll<PaneSplitter>(page).Count;
        if (count != 2) return SelfTestCheck.Fail("review.splitters", $"{count} PaneSplitters on the Review page, expected 2");
        var problems = new List<string>();

        // Columns: TimelineColumn (pixels, min 280) | splitter | SideColumn (star, min 420)
        var cols = page.ColumnSplitter;
        double Left() => page.TimelineColumn.ActualWidth;
        double Side() => page.SideColumn.ActualWidth;
        double w0 = Left(), colTotal = w0 + Side();
        double wDrag = Math.Clamp(w0 + 40, page.TimelineColumn.MinWidth, colTotal - page.SideColumn.MinWidth);
        Drag(cols, 20, 40);
        Expect("column drag +40", Left(), wDrag);
        if (Math.Abs(Left() - w0) < 1) problems.Add($"column drag +40 did not move the boundary (width {Left():0.#})");
        cols.ApplyDrag(-10_000);
        page.UpdateLayout();
        Expect("column drag to the left end", Left(), page.TimelineColumn.MinWidth);
        cols.ApplyDrag(10_000);
        page.UpdateLayout();
        Expect("column drag to the right end (side pane)", Side(), page.SideColumn.MinWidth);
        double before = Left();
        cols.ApplyDrag(PaneSplitter.NudgeFor(VirtualKey.Left, resizesRows: false));
        page.UpdateLayout();
        Expect("column Left key", Left(), before - PaneSplitter.KeyStep);

        // Rows: MapRow (star, min 160) | tuning (Auto) | splitter | ClipRow (star, min 160)
        var rows = page.RowSplitter;
        double MapH() => page.MapRow.ActualHeight;
        double ClipH() => page.ClipRow.ActualHeight;
        double h0 = MapH(), rowTotal = h0 + ClipH();
        double hDrag = Math.Clamp(h0 + 30, page.MapRow.MinHeight, rowTotal - page.ClipRow.MinHeight);
        Drag(rows, 15, 30);
        Expect("row drag +30", MapH(), hDrag);
        if (Math.Abs(MapH() - h0) < 1) problems.Add($"row drag +30 did not move the boundary (map {MapH():0.#})");
        Expect("row drag keeps the map + clip height", MapH() + ClipH(), rowTotal);
        rows.ApplyDrag(-10_000);
        page.UpdateLayout();
        Expect("row drag to the top (map)", MapH(), page.MapRow.MinHeight);
        rows.ApplyDrag(10_000);
        page.UpdateLayout();
        Expect("row drag to the bottom (clip list)", ClipH(), page.ClipRow.MinHeight);
        before = MapH();
        rows.ApplyDrag(PaneSplitter.NudgeFor(VirtualKey.Up, resizesRows: true));
        page.UpdateLayout();
        Expect("row Up key", MapH(), before - PaneSplitter.KeyStep);

        // Persistence (Ref §9.3): a resting layout is saved through ShellVm.UpdateLayout; then the original layout is put back.
        cols.ApplyDrag(wDrag - Left());
        rows.ApplyDrag(hDrag - MapH());
        page.UpdateLayout();
        var shell = ctx.Services.Shell;
        double wantRatio = Math.Round(MapH() / (MapH() + ClipH()), 3), wantWidth = Math.Round(Left());
        bool saved = await WaitUntilAsync(() => Math.Abs(shell.Settings.Layout.TimelineWidth - wantWidth) < 1
                                                && Math.Abs(shell.Settings.Layout.MapHeightRatio - wantRatio) < 0.006, TimeSpan.FromSeconds(3));
        if (!saved) problems.Add($"layout not saved: Settings.Layout {shell.Settings.Layout}, expected width {wantWidth}, ratio {wantRatio}");
        cols.ApplyDrag(w0 - Left());
        rows.ApplyDrag(h0 - MapH());
        page.UpdateLayout();

        return problems.Count == 0
            ? SelfTestCheck.Pass("review.splitters", string.Create(CultureInfo.InvariantCulture,
                  $"columns {w0:0}→{wDrag:0} px (min {page.TimelineColumn.MinWidth:0}/{page.SideColumn.MinWidth:0} held), rows map {h0:0}→{hDrag:0} px (min 160/160 held), keys ±{PaneSplitter.KeyStep:0}, layout saved"))
            : SelfTestCheck.Fail("review.splitters", string.Join("; ", problems));

        // a pointer drag: press, two moves, release — the boundary follows the offset from the press point
        void Drag(PaneSplitter splitter, double firstMove, double secondMove)
        {
            if (!splitter.BeginDrag()) { problems.Add("BeginDrag found no tracks to resize"); return; }
            splitter.DragTo(firstMove);
            splitter.DragTo(secondMove);
            splitter.EndDrag();
            page.UpdateLayout();
        }

        void Expect(string what, double actual, double expected)
        {
            if (Math.Abs(actual - expected) > 1)
                problems.Add(string.Create(CultureInfo.InvariantCulture, $"{what}: {actual:0.#} px, expected {expected:0.#}"));
        }
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
        // A click the plan ignores (here: a read-only session) must not leave the include box showing a state the plan lacks.
        var box = VisualTree.FindDescendant<CheckBox>(c, b => b.Name == "TileInclude");
        bool snapBack = false;
        if (box is not null)
        {
            page.Vm.IsReadOnly = true;
            try
            {
                new CheckBoxAutomationPeer(box).Toggle();                   // OnClick: flips the box, then runs ToggleCommand
                await Task.Delay(300);
                snapBack = box.IsChecked == tile.IsIncluded;
            }
            finally { page.Vm.IsReadOnly = false; }
        }
        page.Vm.SelectedTab = 0;
        return thumb && status && dayRow && layout && snapBack
            ? SelfTestCheck.Pass("template.photoTile", $"day '{page.Vm.Photos.Days[0].DayText}', tile '{tile.StatusText}' on a LinedFlowLayout wall; an ignored click snaps the box back")
            : SelfTestCheck.Fail("template.photoTile", $"thumb {thumb} status {status} day {dayRow} lined {layout} snapBack {snapBack} (box {box?.IsChecked}, plan {tile.IsIncluded})");
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

    // Ref §9.12 / §13: "A smoke test types a space into the rename box and checks that Included is unchanged."
    private static async Task<SelfTestCheck> KeysSpaceInRenameBox(SelfTestContext ctx)
    {
        var (page, timeline, groups) = await TimelineAsync(ctx);
        page.Vm.Videos.SelectedEntry = groups[0];
        var list = VisualTree.FindDescendant<ClipListView>(page)!;
        await WaitUntilAsync(() => page.Vm.Videos.Clips.Count == 2 && list.ContainerFor(page.Vm.Videos.Clips[1]) is not null, TimeSpan.FromSeconds(5));
        var before = page.Vm.Videos.Clips.Select(c => c.IsIncluded).ToList();

        var box = VisualTree.FindDescendant<AutoSuggestBox>(timeline.ContainerFor(groups[0])!, b => b.Name == "DescriptionBox")!;
        box.Focus(FocusState.Keyboard);
        await WaitUntilAsync(() => KeyRouting.IsTextInput(FocusManager.GetFocusedElement(page.XamlRoot)), TimeSpan.FromSeconds(2));
        var focused = FocusManager.GetFocusedElement(page.XamlRoot);
        bool t = timeline.HandleKey(VirtualKey.Space, KeyMods.None, focused);
        bool c = list.HandleKey(VirtualKey.Space, KeyMods.None, focused);
        await Task.Delay(300);                                   // any (wrong) edit would have been applied by now
        var afterText = page.Vm.Videos.Clips.Select(x => x.IsIncluded).ToList();

        var rowB = page.Vm.Videos.Clips[1];
        var container = list.ContainerFor(rowB)!;
        container.Focus(FocusState.Keyboard);
        await WaitUntilAsync(() => KeyRouting.IsInItemContainer(FocusManager.GetFocusedElement(page.XamlRoot)), TimeSpan.FromSeconds(2));
        bool handled = list.HandleKey(VirtualKey.Space, KeyMods.None, FocusManager.GetFocusedElement(page.XamlRoot));
        bool toggled = await WaitUntilAsync(() => page.Vm.Videos.Clips.Count == 2 && page.Vm.Videos.Clips[1].IsIncluded != before[1], TimeSpan.FromSeconds(3));
        if (toggled) list.HandleKey(VirtualKey.Space, KeyMods.None, FocusManager.GetFocusedElement(page.XamlRoot));   // restore
        await WaitUntilAsync(() => page.Vm.Videos.Clips.Select(x => x.IsIncluded).SequenceEqual(before), TimeSpan.FromSeconds(3));

        // Ctrl/Shift+Space are the list's own multi-select: row A stays the selection while row B has focus.
        var selection = page.Vm.Videos.SelectedClipIds;
        var rowA = page.Vm.Videos.Clips[0];
        bool ctrl, shift, keptSelection, onButton, buttonLeftAlone;
        try
        {
            list.SelectRows((ItemId[])[rowA.Id]);                  // explicit array: CsWinRT1032
            var focusedB = list.ContainerFor(rowB);                    // row B's container has the keyboard focus
            ctrl = list.HandleKey(VirtualKey.Space, KeyMods.Ctrl, focusedB);
            shift = list.HandleKey(VirtualKey.Space, KeyMods.Shift, focusedB);
            keptSelection = page.Vm.Videos.SelectedClipIds.SequenceEqual((ItemId[])[rowA.Id]);
            // Space on a button inside a row (Split before) is the button's, never an Include toggle.
            var split = VisualTree.FindDescendant<Button>(list.ContainerFor(rowB)!, b => b.Name == "SplitBeforeButton");
            onButton = split is not null && list.HandleKey(VirtualKey.Space, KeyMods.None, split);
            await Task.Delay(300);
            buttonLeftAlone = split is not null && page.Vm.Videos.Clips.Select(x => x.IsIncluded).SequenceEqual(before);
        }
        finally { list.SelectRows(selection); }
        bool ok = !t && !c && afterText.SequenceEqual(before) && handled && toggled && !ctrl && !shift && keptSelection && !onButton && buttonLeftAlone;
        return ok ? SelfTestCheck.Pass("keys.spaceInRenameBox", "space in the rename box changed nothing; space on a clip row toggled it; Ctrl/Shift+Space kept the selection; space on Split before was the button's")
                  : SelfTestCheck.Fail("keys.spaceInRenameBox", $"timeline {t} list {c} unchanged {afterText.SequenceEqual(before)} rowHandled {handled} toggled {toggled} ctrl {ctrl} shift {shift} keptSelection {keptSelection} onButton {onButton} buttonLeftAlone {buttonLeftAlone}");
    }

    private static async Task<SelfTestCheck> KeysAccelerators(SelfTestContext ctx)
    {
        var (page, timeline, groups) = await TimelineAsync(ctx);
        string[] expected = ["undo", "redo", "mergeNext", "moveToNewGroup", "tab1", "tab2", "tab3", "rescan", "offload"];
        var actions = ReviewPage.AcceleratorTable.Select(a => a.Action).Distinct().ToList();
        bool unmodifiedLetters = ReviewPage.AcceleratorTable.Any(a => a.Modifiers == VirtualKeyModifiers.None && a.Key != VirtualKey.F5);
        var box = VisualTree.FindDescendant<AutoSuggestBox>(timeline.ContainerFor(groups[0])!, b => b.Name == "DescriptionBox")!;
        box.Focus(FocusState.Keyboard);
        await WaitUntilAsync(() => KeyRouting.IsTextInput(FocusManager.GetFocusedElement(page.XamlRoot)), TimeSpan.FromSeconds(2));
        bool undoInText = page.TryHandleAccelerator("undo", FocusManager.GetFocusedElement(page.XamlRoot));
        bool tab = page.TryHandleAccelerator("tab2", FocusManager.GetFocusedElement(page.XamlRoot)) && page.Vm.SelectedTab == 1;
        page.Vm.SelectedTab = 0;
        bool ok = expected.All(actions.Contains) && page.KeyboardAccelerators.Count == ReviewPage.AcceleratorTable.Count
                  && !unmodifiedLetters && !undoInText && tab;
        return ok ? SelfTestCheck.Pass("keys.accelerators", $"{page.KeyboardAccelerators.Count} accelerators; Ctrl+Z left to the TextBox; Ctrl+2 → Photos")
                  : SelfTestCheck.Fail("keys.accelerators", $"actions [{string.Join(",", actions)}] unmodified {unmodifiedLetters} undoInText {undoInText} tab {tab}");
    }

    private static (int Rev, int Items) MapStats(string? json)
    {
        if (string.IsNullOrEmpty(json)) return (-1, -1);
        using var doc = JsonDocument.Parse(json);
        return (doc.RootElement.GetProperty("rev").GetInt32(), doc.RootElement.GetProperty("items").GetInt32());
    }

    /// <summary>A rescan hands the cached ReviewPage a new ReviewVm and a new MapBridge whose setData starts again at rev 1:
    /// the map must show the new session's data, not keep the previous scan's (a higher rev) and drop the new one.</summary>
    private static async Task<SelfTestCheck> ReviewMapNewSession(SelfTestContext ctx)
    {
        var page = await ReviewPageAsync(ctx);
        if (!await WaitUntilAsync(() => page.Map.IsReady && page.Vm.Map is not null, TimeSpan.FromSeconds(15)))
            return SelfTestCheck.Fail("review.map.newSession", "map not ready");
        const string Stats = "JSON.stringify(window.__uas.stats())";
        var first = page.Vm;
        var bridgeA = first.Map!;
        for (int i = 0; i < 4; i++)                                     // raise the old session's rev (throttle: 100 ms)
        {
            bridgeA.SendData(first.Plan);
            await Task.Delay(150);
        }
        var a = MapStats(await PollEvaluateAsync(page.Map, Stats, s => MapStats(s).Rev == bridgeA.Rev, TimeSpan.FromSeconds(5)));
        var shell = ctx.Services.Shell;
        await shell.RescanAsync();                                      // F5: a new scan, a new ReviewVm on the cached page
        if (!await WaitUntilAsync(() => CurrentPage<ReviewPage>(ctx) is { } p && p.Vm is { Map: not null } r && !ReferenceEquals(r, first),
                                  TimeSpan.FromSeconds(15)))
            return SelfTestCheck.Fail("review.map.newSession", $"rescan did not reach a new Review (stage {shell.Stage})");
        page = CurrentPage<ReviewPage>(ctx)!;
        var bridgeB = page.Vm.Map!;
        var b = MapStats(await PollEvaluateAsync(page.Map, Stats, s => MapStats(s) is var m && m.Rev == bridgeB.Rev && m.Items == 3,
                                                 TimeSpan.FromSeconds(8)));
        bool ok = a.Rev >= 5 && b.Rev == bridgeB.Rev && b.Rev < a.Rev && b.Items == 3;
        return ok ? SelfTestCheck.Pass("review.map.newSession", $"old session rev {a.Rev}; after the rescan the map shows the new session at rev {b.Rev} with {b.Items} items")
                  : SelfTestCheck.Fail("review.map.newSession", $"old rev {a.Rev}; map rev {b.Rev} items {b.Items}; new bridge rev {bridgeB.Rev}");
    }

    /// <summary>ContainerFor (VisualTree.RealizedContainer) returns only realised containers. With an x:Bind template whose root
    /// sets DataContext="{x:Bind}" (the Timeline, clip list, photo wall and cleanup rows) a recycled container stays a child of
    /// the ItemsRepeater, off-screen, still holding its last item; it must not be returned for that item. Uses the Cleanup
    /// page's row template, which needs no ReviewVm.</summary>
    private static async Task<SelfTestCheck> TemplateRealizedOnly(SelfTestContext ctx)
    {
        await EnsureReviewAsync(ctx);                                   // the card files exist, so the rows' thumbnails can load
        var items = Enumerable.Range(0, 120).Select(_ => SelfTestCleanupRow((_, _) => { })).ToList();
        var owner = new CleanupPage();
        var view = new ItemsView { Height = 300, Width = 600, ItemTemplate = owner.ReviewList.ItemTemplate, ItemsSource = items };
        var popup = new Popup { XamlRoot = ctx.Window.Content.XamlRoot, Child = view, IsOpen = true };
        try
        {
            if (!await WaitUntilAsync(() => VisualTree.RealizedContainer(view, items[0]) is not null, TimeSpan.FromSeconds(5)))
                return SelfTestCheck.Fail("template.realizedOnly", "first row not realised");
            view.StartBringItemIntoView(items.Count - 1, new BringIntoViewOptions());
            await WaitUntilAsync(() => VisualTree.RealizedContainer(view, items[^1]) is not null, TimeSpan.FromSeconds(5));
            view.Height = 60;                                           // fewer rows needed: the rest go to the recycle pool
            view.StartBringItemIntoView(0, new BringIntoViewOptions());
            await WaitUntilAsync(() => VisualTree.RealizedContainer(view, items[0]) is not null, TimeSpan.FromSeconds(5));
            await Task.Delay(200);
            var repeater = VisualTree.FindDescendant<ItemsRepeater>(view)!;
            bool lastGone = VisualTree.RealizedContainer(view, items[^1]) is null;
            // what the old depth-first DataContext walk returned: a pooled container still holding a scrolled-away row
            int pooled = VisualTree.FindAll<ItemContainer>(view, c => c.DataContext is CleanupRowVm && repeater.GetElementIndex(c) < 0).Count;
            int realised = 0;
            bool indexesMatch = true;
            for (int i = 0; i < items.Count; i++)
            {
                if (VisualTree.RealizedContainer(view, items[i]) is not { } c) continue;
                realised++;
                indexesMatch &= repeater.GetElementIndex(c) == i;
            }
            bool ok = lastGone && indexesMatch && realised > 0 && realised < items.Count;
            return ok ? SelfTestCheck.Pass("template.realizedOnly", $"{realised} realised containers, each at its row's index; {pooled} pooled containers still hold a row and are skipped")
                      : SelfTestCheck.Fail("template.realizedOnly", $"lastGone {lastGone} indexesMatch {indexesMatch} realised {realised} pooled {pooled}");
        }
        finally { popup.IsOpen = false; }
    }
}
