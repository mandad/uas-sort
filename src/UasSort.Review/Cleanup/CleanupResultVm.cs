// src/UasSort.Review/Cleanup/CleanupResultVm.cs
namespace UasSort.Review;

/// <summary>The cleanup result page (Ref §10.6 "After the loop" step 5). When the closing re-read failed
/// (<see cref="CleanupResult.ClosingReadError"/>), that text replaces the "now has … free" line and StillListed is not shown,
/// because neither the free space nor the re-list can be trusted.</summary>
public sealed class CleanupResultVm
{
    public CleanupResultVm(CleanupResult r, VerdictLevel after, string reportPath, string cardRoot, IDeviceEject eject)
    {
        ArgumentNullException.ThrowIfNull(r);
        var (files, bytes) = DeletedTotals(r);
        var drive = CleanupTexts.VolumeName(cardRoot);
        var spaceLine = r.ClosingReadError ?? $"{drive} now has {Fmt.Size(r.SpaceAfter.FreeBytes)} free";
        HeadlineText = $"Deleted {Fmt.Count(files, "file", "files")} ({Fmt.Size(bytes)}) · {spaceLine}";
        Problems = [.. r.Outcomes.Select(Problem).OfType<string>()];
        StillListed = r.ClosingReadError is not null
            ? []
            : [.. r.StillListed.Select(p => $"still on the card: {p} (another program held it open)")];
        VerdictText = VerdictVm.LevelName(after);
        StopText = r.Stop switch
        {
            null => null,
            CleanupStop.OffloadLockHeld => "Another uas-sort window is offloading; nothing was deleted.",
            CleanupStop.LedgerUnavailable => "The history folder can't be written; nothing was deleted.",
            CleanupStop.Cancelled => "Stopped after the current file.",
            CleanupStop.CardSwapped => "A different card is in the drive; stopped.",
            CleanupStop.CardRemoved => "The card was removed; stopped.",
            CleanupStop.WriteProtected => "The card became write-protected (lock switch); stopped.",
            CleanupStop.LedgerWriteFailed => "Recording a delete in the history failed; stopped. The report names the file.",
            CleanupStop.InternalSafetyStop => "Internal safety stop; stopped.",
            _ => r.Stop.ToString(),
        };
        Eject = new EjectVm(cardRoot, eject);
        ReportPath = reportPath;
    }

    public string HeadlineText { get; }
    public IReadOnlyList<string> Problems { get; }
    public IReadOnlyList<string> StillListed { get; }
    public string VerdictText { get; }
    public string? StopText { get; }
    public string ReportPath { get; }
    public EjectVm Eject { get; }

    public string SafeRemovalText { get; } =
        "Safely remove the card before putting it back in the drone. The drone may show old thumbnails until it rebuilds its index; formatting in the drone is the clean option.";

    public static (int Files, long Bytes) DeletedTotals(CleanupResult r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var files = 0;
        long bytes = 0;
        foreach (var o in r.Outcomes)
        {
            if (o is Deleted d) { files += d.Files; bytes += d.Bytes; }
            else if (o is PartiallyDeleted p) files += p.DeletedPaths.Length;
        }
        return (files, bytes);
    }

    private static string Name(string relPath) => relPath[(relPath.LastIndexOfAny(['/', '\\']) + 1)..];

    private static string? Problem(CleanupOutcome o) => o switch
    {
        Deleted d when !d.SetFolderRemoved && d.Unit.CardRelPath.Contains("PANORAMA", StringComparison.OrdinalIgnoreCase) => $"{Name(d.Unit.CardRelPath)}: folder kept",
        Deleted => null,
        SkippedChanged s => $"{Name(s.CardRelPath)}: skipped, it changed since the scan",
        SkippedEvidenceGone e => $"{Name(e.CardRelPath)}: skipped, {e.Why}",
        PartiallyDeleted p => $"{Name(p.Unit.CardRelPath)}: partly deleted ({p.Why}); still on the card: {string.Join(", ", p.StillOnCard.Select(Name))}",
        CleanupFailed f => $"{Name(f.CardRelPath)}: not deleted ({f.Error})",
        CleanupNotStarted => null,
        CleanupCardSwapped c => $"{Name(c.Unit.CardRelPath)}: not deleted, a different card is in the drive",
    };
}
