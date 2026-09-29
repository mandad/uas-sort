using System.Globalization;

namespace UasSort.Core.Offload;

/// <summary>Ref §11 "Reports": the per-run report, flattened (no CopyOutcome is serialised; Ref §3 "JSON").</summary>
public static class OffloadReportBuilder
{
    public const int Version = 1;

    public static OffloadReport Build(Plan plan, OffloadBatch batch, OffloadResult result, FormatVerdict verdict)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(verdict);
        return new OffloadReport(Version, result.RunId, plan.Base.Scan.Settings, PlanSummary(batch, plan),
            [.. result.Outcomes.Select(Line)], [.. verdict.Units.SelectMany(u => u.Lines)], verdict.CardChanges, verdict.Level,
            verdict.Headline, result.Stop);
    }

    public static ReportLine Line(CopyOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        var job = outcome.Job;
        var name = outcome.GetType().Name;
        return outcome switch
        {
            Verified v => new ReportLine(job.CardRelPath, job.DestPath, name, null, null,
                                         v.Hash.ToString("x32", CultureInfo.InvariantCulture), v.Mode == VerifyMode.Cached ? "cached" : "unbuffered"),
            AlreadyThere => new ReportLine(job.CardRelPath, job.DestPath, name, null, null, null, "nameSize"),
            Failed f => new ReportLine(job.CardRelPath, job.DestPath, name, f.Phase, f.Error, null, null),
            ChangedOnCard c => new ReportLine(job.CardRelPath, job.DestPath, name, null,
                string.Create(CultureInfo.InvariantCulture, $"now {c.NowSize} bytes, modified {c.NowMtimeUtc:yyyy-MM-dd HH:mm:ss}Z"), null, null),
            CardSwapped s => new ReportLine(job.CardRelPath, job.DestPath, name, null,
                $"a different card is in the reader (serial {VerdictText.Serial(s.Now)})", null, null),
            ConflictAtRename or Cancelled or NotStarted => new ReportLine(job.CardRelPath, job.DestPath, name, null, null, null, null),
        };
    }

    public static string PlanSummary(OffloadBatch batch, Plan plan)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(plan);
        static string N(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";
        var units = plan.Base.Scan.Inventory.Units.ToDictionary(u => u.Id);
        var groups = batch.Jobs.Where(j => j.Group is not null).Select(j => j.Group!.Value).Distinct().Count();
        var videos = batch.Jobs.Count(j => j.Root == DestRoot.Video);
        var photos = batch.Jobs.Where(j => j.Root == DestRoot.Photo && units.GetValueOrDefault(j.Item) is PhotoUnit)
                               .Select(j => j.Item).Distinct().Count();
        var sets = batch.Jobs.Where(j => units.GetValueOrDefault(j.Item) is SetUnit).Select(j => j.Item).Distinct().Count();
        return $"{N(groups, "folder", "folders")} · {N(videos, "video", "videos")} · {N(photos, "photo", "photos")} · "
             + $"{N(sets, "set", "sets")} · {OffloadPaths.Gb(batch.Jobs.Sum(j => j.Size))}";
    }
}
