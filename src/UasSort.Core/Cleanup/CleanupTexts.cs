// src/UasSort.Core/Cleanup/CleanupTexts.cs
using System.Globalization;

namespace UasSort.Core.Cleanup;

/// <summary>Summary wording built from a CleanupPlan (Ref §10.6 Mode 1/Mode 2/Summary; defined here).</summary>
public static class CleanupTexts
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static string Plural(int n, string one, string many) => n == 1 ? $"1 {one}" : string.Create(Inv, $"{n} {many}");

    public static string VolumeName(string cardRoot)
    {
        ArgumentNullException.ThrowIfNull(cardRoot);
        return cardRoot.TrimEnd('\\', '/');
    }

    public static string CutoffLine(CleanupPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.Request.Mode == CleanupMode.BeforeDate ? BeforeDateLine(plan) : FreeSpaceLine(plan);
    }

    private static string BeforeDateLine(CleanupPlan plan)
    {
        var before = plan.Request.Before ?? plan.Cutoff.BeforeDate
                     ?? throw new InvalidOperationException("A before-date plan without a date.");
        var day = CleanupFormat.MonthDayYear(before);
        var dayShort = CleanupFormat.MonthDay(before);
        var keptFiles = plan.NotDeletable.Where(k => k.Unit is not null).Sum(k => k.CardRelPaths.Length);
        var evidenceFiles = plan.Delete.Where(c => c.Eligibility == CleanupEligibility.Evidence).Sum(c => c.Files.Length);
        var reviewedFiles = plan.Delete.Where(c => c.Eligibility == CleanupEligibility.NotInLibrary).Sum(c => c.Files.Length);
        string line;
        if (keptFiles == 0 && !plan.Delete.IsEmpty)
            line = $"Deletes everything captured before {day} (local time at each site); {dayShort} and later are kept.";
        else if (keptFiles == 0)
            line = $"Nothing was captured before {day} (local time at each site).";
        else
        {
            var plus = reviewedFiles > 0 ? string.Create(Inv, $", plus {reviewedFiles} you reviewed") : "";
            var older = keptFiles == 1 ? "1 older file is kept" : string.Create(Inv, $"{keptFiles} older files are kept");
            line = $"Deletes {Plural(evidenceFiles, "file", "files")} captured before {day} (local time at each site) that are in your library{plus}; "
                 + $"{dayShort} and later are kept. {older} (see 'Kept').";
        }
        if (plan.Cutoff.FlightContinuesLocal is { } t && plan.Cutoff.TzId is { } tz && plan.Cutoff.LastCaptureUtc is { } utc)
            line += $" A flight continues past the cutoff ({t.ToString("MMM d HH:mm", Inv)} {CleanupFormat.Abbrev(tz, utc)}); its later clips are kept.";
        return line;
    }

    private static string FreeSpaceLine(CleanupPlan plan)
    {
        if (plan.Delete.IsEmpty) return NothingToDelete(plan) ?? "Nothing on this card can be deleted.";
        var first = plan.Delete[0];
        var c = plan.Cutoff;
        var lastUtc = c.LastCaptureUtc ?? plan.Delete[^1].CaptureUtc;
        var lastTz = c.TzId ?? plan.Delete[^1].TzId;
        var lastLocal = c.LastLocalTime ?? plan.Delete[^1].LocalTime;
        var lastAbbr = CleanupFormat.Abbrev(lastTz, lastUtc);
        var firstAbbr = CleanupFormat.Abbrev(first.TzId, first.CaptureUtc);
        var from = firstAbbr == lastAbbr ? CleanupFormat.MonthDay(first.LocalDate) : $"{CleanupFormat.MonthDay(first.LocalDate)} {firstAbbr}";
        var cut = lastLocal.ToString("MMM d HH:mm", Inv);
        var cutDay = CleanupFormat.MonthDay(DateOnly.FromDateTime(lastLocal));
        return string.Create(Inv,
            $"Deletes {Plural(plan.FileCount, "file", "files")} · {CleanupFormat.Gb(plan.AllocatedBytes)} · captured {from} – {cut} {lastAbbr} → cutoff: {cut} ({c.FilesDeletedOnCutoffDay} of {c.FilesOnCutoffDay} files from {cutDay})");
    }

    public static string? NothingToDelete(CleanupPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.Request.Mode == CleanupMode.FreeSpace && plan.Delete.IsEmpty && plan.Shortfall is null
            ? $"{VolumeName(plan.CardRoot)} already has {CleanupFormat.Gb(plan.SpaceBefore.FreeBytes)} free; nothing to delete"
            : null;
    }

    public static string? ShortfallLine(CleanupPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Shortfall is not { } sf) return null;
        return !plan.Request.IncludeNotInLibrary && sf.HeldByNotInLibrary > 0
            ? $"Only {CleanupFormat.Gb(sf.FreeableBytes)} can be freed; {CleanupFormat.Gb(sf.HeldByNotInLibrary)} is held by files that are not in your library"
            : $"Only {CleanupFormat.Gb(sf.FreeableBytes)} can be freed; {CleanupFormat.Gb(sf.HeldByNever)} is held by files uas-sort never deletes (unknown files, changed files, DJI system files)";
    }

    public static string EvidenceSplit(CleanupPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var listed = plan.Delete.Count(c => c.Source == EvidenceSource.Listed);
        var history = plan.Delete.Count(c => c.Source == EvidenceSource.HistoryOnly);
        var text = string.Create(Inv, $"{listed} in the library listing");
        return history == 0 ? text
            : text + $" · {Plural(history, "photo", "photos")} found only in the history (Lightroom may have moved them)";
    }

    public static string? NeverCopiesLine(CleanupPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var files = plan.Delete.SelectMany(c => c.NeverCopied).ToList();
        if (files.Count == 0) return null;
        int Ends(string ext) => files.Count(f => f.Class != EntryClass.PhotoTwin && f.RelPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
        var twins = files.Count(f => f.Class == EntryClass.PhotoTwin);
        var lrf = Ends(".LRF");
        var srt = Ends(".SRT");
        var covers = Ends(".JPG");
        var recovery = files.Count - twins - lrf - srt - covers;
        var parts = new List<string>();
        if (twins > 0) parts.Add(Plural(twins, "JPG twin", "JPG twins") + " (copying is off in Settings)");
        if (lrf > 0) parts.Add(Plural(lrf, "LRF proxy", "LRF proxies"));
        if (srt > 0) parts.Add(Plural(srt, "SRT caption", "SRT captions"));
        if (covers > 0) parts.Add(Plural(covers, "video cover", "video covers"));
        if (recovery > 0) parts.Add(Plural(recovery, "recovery file", "recovery files"));
        return $"Also deletes {Plural(files.Count, "file", "files")} uas-sort never copies: {string.Join(", ", parts)}";
    }
}
