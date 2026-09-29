// src/UasSort.App/SelfTest/SelfTestChecks.Review.cs (Core namespaces come from GlobalUsings.Core.cs)
using System.Text;
using System.Text.Json;

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
}
