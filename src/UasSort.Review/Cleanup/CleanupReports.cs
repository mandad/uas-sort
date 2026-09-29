// src/UasSort.Review/Cleanup/CleanupReports.cs
namespace UasSort.Review;

/// <summary>Builds reports\&lt;ts&gt;-&lt;run8&gt;-cleanup.json content (Ref §10.6 "After the loop" step 4).</summary>
public static class CleanupReports
{
    public static CleanupReport Build(CleanupPlan plan, CleanupResult result, VerdictLevel after)
    {
        var byUnit = result.Outcomes.ToDictionary(o => o.Unit);
        var deletedInOrder = new List<string>();
        var lines = new List<CleanupReportLine>();
        foreach (var c in plan.Delete)
        {
            var outcome = byUnit.GetValueOrDefault(c.Unit);
            foreach (var f in c.Files)
            {
                var deleted = outcome switch
                {
                    Deleted => true,
                    PartiallyDeleted p => p.DeletedPaths.Contains(f.RelPath, StringComparer.OrdinalIgnoreCase),
                    _ => false,
                };
                if (deleted) deletedInOrder.Add(f.RelPath);
                var text = outcome switch
                {
                    null => "not started",
                    Deleted => "deleted",
                    PartiallyDeleted => deleted ? "deleted" : "kept (unit partly deleted)",
                    SkippedChanged => "skipped: changed since the scan",
                    SkippedEvidenceGone => "skipped: evidence gone",
                    CleanupFailed => "failed",
                    CleanupNotStarted => "not started",
                    CleanupCardSwapped => "not deleted: card swapped",
                };
                var primary = c.Proofs.Any(p => string.Equals(p.CardRelPath, f.RelPath, StringComparison.OrdinalIgnoreCase)) || c.Proofs.Length == 0;
                var evidence = c.Eligibility == CleanupEligibility.NotInLibrary ? "notInLibraryConfirmed"
                             : primary ? (c.Source == EvidenceSource.HistoryOnly ? "historyOnly:" : "") + c.Evidence
                             : "companionOf:" + c.Evidence;
                int? win32 = outcome is CleanupFailed cf && string.Equals(cf.CardRelPath, f.RelPath, StringComparison.OrdinalIgnoreCase) ? cf.Win32Error : null;
                lines.Add(new CleanupReportLine(f.RelPath, f.Size, c.Unit.CardRelPath, text, c.Eligibility.ToString(), evidence, c.Reason, win32, deleted));
            }
        }
        if (result.Stop == CleanupStop.LedgerWriteFailed && deletedInOrder.Count > 0)
        {
            var last = deletedInOrder[^1];
            var i = lines.FindIndex(l => string.Equals(l.CardRelPath, last, StringComparison.OrdinalIgnoreCase));
            lines[i] = lines[i] with { LedgerRecorded = false };
        }
        return new CleanupReport(1, result.RunId, plan.Card, plan.Request, plan.Cutoff, [.. lines], plan.NotDeletable, result.Stop,
                                 plan.SpaceBefore.FreeBytes, result.SpaceAfter.FreeBytes, after);
    }
}
