// src/UasSort.App/SelfTest/SelfTestChecks.Review.cs (Core namespaces come from GlobalUsings.Core.cs)
using System.Text;
using System.Text.Json;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml.Controls;

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
}
