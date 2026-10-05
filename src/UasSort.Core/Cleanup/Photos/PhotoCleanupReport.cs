// src/UasSort.Core/Cleanup/Photos/PhotoCleanupReport.cs
namespace UasSort.Core.Cleanup;

public sealed record PhotoCleanupReportLine(string Item, string Kind /* photo|panorama|hyperlapse|set */, int Files, long Bytes,
                                            string Decision /* delete|keep */, bool Verified, string Verification, string Evidence,
                                            string Outcome, string? Error, bool LedgerRecorded);

/// <summary>reports\yyyyMMdd-HHmmss-&lt;run8&gt;-photos.json (spec 2026-10-04 §6): every eligible row with its decision, verification and
/// outcome, then what was not eligible and what was not touched.</summary>
public sealed record PhotoCleanupReport(int V, string RunId, string PhotoRoot, PhotoCleanupMode Mode, DateOnly Cutoff, string? LightroomFolder,
                                        ImmutableArray<PhotoCleanupReportLine> Items, ImmutableArray<string> NotEligible,
                                        ImmutableArray<string> NotTouched, PhotoCleanupStop? Stop, ImmutableArray<string> Unrecorded,
                                        DateTime StartUtc, DateTime EndUtc)
{
    /// <summary>Verify mode: what the Lightroom walk read and couldn't read (branch-2 ruling); null in date mode.</summary>
    public LightroomIndexSummary? Lightroom { get; init; }
}

public static class PhotoCleanupReports
{
    public static PhotoCleanupReport Build(PhotoCleanupResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var plan = result.Plan.Plan;
        var byItem = result.Outcomes.ToDictionary(o => o.Item, StringComparer.OrdinalIgnoreCase);
        var unrecorded = result.Unrecorded.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lines = new List<PhotoCleanupReportLine>();
        foreach (var row in plan.Rows)
        {
            var item = row.Item;
            var chosen = byItem.TryGetValue(row.Key, out var o);
            var (outcome, error) = !chosen ? ("kept", (string?)null) : o! switch
            {
                PhotoRecycled => ("moved to the Recycle Bin", null),
                PhotoSkippedChanged s => ("skipped: changed since review", s.Why),
                PhotoPartlyRecycled p => ("partly moved", $"{p.Why}; still in Picture Offload: {string.Join(", ", p.Left)}"),
                PhotoRecycleFailed f => (f.NotRecyclable ? "kept: Windows would delete it permanently" : "failed", f.Error),
                PhotoNotStarted => ("not started", null),
            };
            var moved = o is PhotoRecycled or PhotoPartlyRecycled;
            var recorded = moved && item.Members.All(m => !unrecorded.Contains(PhotoCleanupPaths.Full(plan.PhotoRoot, m.RelPath)));
            lines.Add(new PhotoCleanupReportLine(item.RelPath, KindText(item), item.Members.Length, item.Bytes, chosen ? "delete" : "keep",
                row.Verification.Verified, row.Verification.Text, chosen ? PhotoDeleteRecords.Evidence(result.Plan.EvidenceOf(row)) : "-",
                outcome, error, recorded));
        }
        return new PhotoCleanupReport(1, result.RunId, plan.PhotoRoot, plan.Request.Mode, plan.Request.Cutoff, plan.Request.LightroomFolder,
            [.. lines], [.. plan.NotEligible.Select(r => $"{r.Key}: {r.Why}")], plan.NotTouched, result.Stop, result.Unrecorded,
            result.StartUtc, result.EndUtc)
        {
            Lightroom = plan.Lightroom,
        };
    }

    private static string KindText(PhotoItem item) => item.Kind == PhotoItemKind.Photo ? "photo" : item.SetKind switch
    {
        PhotoSetKind.Panorama => "panorama",
        PhotoSetKind.Hyperlapse => "hyperlapse",
        _ => "set",
    };
}
