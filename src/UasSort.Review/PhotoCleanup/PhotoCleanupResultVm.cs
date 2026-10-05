// src/UasSort.Review/PhotoCleanup/PhotoCleanupResultVm.cs
namespace UasSort.Review;

/// <summary>The result page (spec 2026-10-04 §2 step 3): moved / kept / skipped-because-changed / failed, the Recycle Bin hint and the report.</summary>
public sealed class PhotoCleanupResultVm
{
    public PhotoCleanupResultVm(PhotoCleanupResult r, string reportPath, string? reportProblem, IShellLauncher shell)
    {
        ArgumentNullException.ThrowIfNull(r);
        ArgumentNullException.ThrowIfNull(shell);
        var moved = r.Outcomes.OfType<PhotoRecycled>().Where(m => m.NotInRecycleBin.IsEmpty).ToList();
        var notInBin = r.Outcomes.Count(o => o is PhotoRecycled { NotInRecycleBin.IsEmpty: false });   // deferred minor P.11: never "moved"
        var files = moved.Sum(m => m.Files) + r.Outcomes.OfType<PhotoPartlyRecycled>().Sum(p => p.Recycled.Length - p.NotInRecycleBin.Length);
        var bytes = moved.Sum(m => m.Bytes);
        var skipped = r.Outcomes.Count(o => o is PhotoSkippedChanged);
        var failed = r.Outcomes.Count(o => o is PhotoRecycleFailed or PhotoPartlyRecycled);
        HeadlineText = $"Moved {Fmt.Count(moved.Count, "item", "items")} ({Fmt.Count(files, "file", "files")}, {Fmt.Size(bytes)}) to the Recycle Bin";
        CountsText = string.Create(CultureInfo.InvariantCulture,
            $"moved {moved.Count}{(notInBin == 0 ? "" : $" · removed without the Recycle Bin {notInBin}")} · kept {r.Plan.Kept.Length} · skipped because changed {skipped} · failed {failed}");
        Problems = [.. r.Outcomes.Select(Problem).OfType<string>()];
        StopText = r.Stop switch
        {
            null => null,
            PhotoCleanupStop.OffloadLockHeld => "Another uas-sort window is offloading or cleaning up; nothing was moved.",
            PhotoCleanupStop.LedgerUnavailable => "The history folder can't be written; nothing was moved.",
            PhotoCleanupStop.RecyclerRefused => "The safety check refused the cleanup; nothing was moved.",
            PhotoCleanupStop.Cancelled => "Stopped after the current item.",
            PhotoCleanupStop.LedgerWriteFailed => "Recording a move in the history failed; stopped. The report names the file.",
            PhotoCleanupStop.InternalSafetyStop => "Internal safety stop; stopped.",
            _ => r.Stop.ToString(),
        };
        LedgerWarning = r.Unrecorded.IsEmpty ? null
            : $"{Fmt.Count(r.Unrecorded.Length, "moved file", "moved files")} couldn't be recorded in the history; a card that still holds them may show them as New. The report lists them.";
        ReportPath = reportPath;
        ReportProblem = reportProblem;
        OpenReportCommand = new RelayCommand(() => shell.OpenFile(ReportPath), () => ReportPath.Length > 0);
    }

    public string HeadlineText { get; }
    public string CountsText { get; }
    public IReadOnlyList<string> Problems { get; }
    public string? StopText { get; }
    public string? LedgerWarning { get; }
    public string ReportPath { get; }
    public string? ReportProblem { get; }
    public IRelayCommand OpenReportCommand { get; }

    public string RecycleBinText { get; } =
        "Moved items are in the Windows Recycle Bin until it is emptied (OneDrive also keeps deleted files in its own recycle bin); restore them from there if you need them.";

    private static string? Problem(PhotoCleanupOutcome o) => o switch
    {
        PhotoRecycled { NotInRecycleBin.IsEmpty: false } m =>
            $"{m.Item}: removed, not in the Recycle Bin — Windows deleted {string.Join(", ", m.NotInRecycleBin)} instead of moving {(m.NotInRecycleBin.Length == 1 ? "it" : "them")} there",
        PhotoRecycled => null,
        PhotoSkippedChanged s => $"{s.Item}: skipped, {s.Why}",
        PhotoPartlyRecycled p => $"{p.Item}: partly moved ({p.Why}); still in Picture Offload: {string.Join(", ", p.Left)}"
                                 + (p.NotInRecycleBin.IsEmpty ? "" : "; " + PhotoCleanupReports.NotInBinText(p.NotInRecycleBin)),
        PhotoRecycleFailed f => f.NotRecyclable ? $"{f.Item}: kept — {f.Error}" : $"{f.Item}: not moved ({f.Error})",
        PhotoNotStarted => null,
    };
}
