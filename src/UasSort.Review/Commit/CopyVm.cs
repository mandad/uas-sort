// src/UasSort.Review/Commit/CopyVm.cs
namespace UasSort.Review;

/// <summary>The Copy page: progress at 10 Hz and Cancel (Ref §7.2 step 12, §10.3).</summary>
public sealed partial class CopyVm : ObservableObject, IDisposable
{
    private readonly PreflightVm _preflight;
    private readonly IDialogService _dialogs;
    private readonly IUiDispatcher _ui;
    private readonly IReviewLog? _log;
    private readonly CancellationTokenSource _cts = new();

    public CopyVm(PreflightVm preflight, IDialogService dialogs, IUiDispatcher ui, IReviewLog? log = null)
    {
        _preflight = preflight;
        _dialogs = dialogs;
        _ui = ui;
        _log = log;
        CancelCommand = new AsyncRelayCommand(CancelAsync);
    }

    [ObservableProperty] public partial string FilesText { get; private set; } = "";
    [ObservableProperty] public partial string BytesText { get; private set; } = "";
    [ObservableProperty] public partial string SpeedText { get; private set; } = "";
    [ObservableProperty] public partial string EtaText { get; private set; } = "";
    [ObservableProperty] public partial string CurrentText { get; private set; } = "";
    [ObservableProperty] public partial string PhaseText { get; private set; } = "";
    [ObservableProperty] public partial double Fraction { get; private set; }
    [ObservableProperty] public partial bool IsRunning { get; private set; }
    [ObservableProperty] public partial string? ErrorText { get; private set; }
    /// <summary>The history or the offload report couldn't be fully written after the copy (CommitResult.LedgerComplete false,
    /// ReportPath null); the Verdict page shows it as a warning.</summary>
    [ObservableProperty] public partial string? WarningText { get; private set; }

    public const string InternalSafetyStopTitle = "Internal safety stop";

    /// <summary>Why the offload stopped before it finished, and what to do (Ref §10.3 stop causes, §12); null for a finished run or
    /// a Cancel.</summary>
    public static string? StopTextFor(OffloadResult r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return r.Stop switch
        {
            null or StopReason.Cancelled => null,
            StopReason.LedgerWriteFailed when r.Outcomes.All(o => o is NotStarted) =>
                "Couldn't create or open the history file, so nothing was copied. The verdict below lists every file as not copied.",
            StopReason.LedgerWriteFailed =>
                "Writing the history file failed; the offload stopped. Fix the video drive, then rescan: the files already copied are found by name and size.",
            StopReason.CardSwapped => "A different card is in the drive; the offload stopped. Put the scanned card back, then rescan.",
            StopReason.CardRemoved => "The card was removed; the offload stopped. Reinsert it, then offload again (finished files show as imported).",
            StopReason.DestinationFull => "The destination disk is full; the offload stopped. Free space, then offload again (finished files show as imported).",
            StopReason.DestinationLost =>
                "The destination drive went away; the offload stopped. Reconnect it, then offload again (finished files show as imported).",
            StopReason.InternalSafetyStop => SafetyStopText(null),
            _ => $"The offload stopped: {r.Stop}.",
        };
    }

    internal static string? WarningFor(CommitResult r)
    {
        var lines = new List<string>();
        if (!r.LedgerComplete && r.Offload.Stop != StopReason.LedgerWriteFailed && !r.Offload.Outcomes.All(o => o is NotStarted))
            lines.Add("The history couldn't be fully written; rescan before formatting the card.");
        if (r.ReportPath is null) lines.Add("The offload report couldn't be saved.");
        return lines.Count == 0 ? null : string.Join(" ", lines);
    }

    private static string SafetyStopText(string? detail)
        => "Internal safety stop: uas-sort refused a file operation it must never make, so the offload stopped and nothing more was copied."
           + (detail is null ? "" : $" ({detail})") + " Please report this.";

    public IAsyncRelayCommand CancelCommand { get; }

    public static string PhaseName(CopyPhase p) => p switch
    {
        CopyPhase.CardCheck => "Checking the card",
        CopyPhase.Stat => "Checking the file",
        CopyPhase.CreateTemp => "Creating",
        CopyPhase.Copy => "Copying",
        CopyPhase.Flush => "Flushing",
        CopyPhase.Verify => "Verifying",
        CopyPhase.Finalize => "Finishing",
        CopyPhase.Rename => "Renaming",
        CopyPhase.Confirm => "Confirming",
        CopyPhase.Ledger => "Recording",
        _ => p.ToString(),
    };

    /// <summary>Runs the Commit through PreflightVm.StartAsync (CommitSession). A Cancel or a stop comes back as a CommitResult with its
    /// StopReason (the verdict still follows); null only when the run could not return a result at all.</summary>
    public async Task<CommitResult?> RunAsync()
    {
        IsRunning = true;
        ErrorText = null;
        WarningText = null;
        try
        {
            var result = await _preflight.StartAsync(new UiProgress<OffloadProgress>(_ui, Apply), _cts.Token).ConfigureAwait(true);
            ErrorText = StopTextFor(result.Offload);
            WarningText = WarningFor(result);
            if (result.Offload.Stop == StopReason.InternalSafetyStop) ReportSafetyStop(ErrorText!);
            return result;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (UnsafeIoException ex)
        {
            ErrorText = SafetyStopText(ex.Message);
            ReportSafetyStop(ErrorText);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorText = $"The offload stopped before it could finish: {ex.Message}";
            return null;
        }
        finally
        {
            IsRunning = false;
        }
    }

    /// <summary>Ref §12 UnsafeIoException: stop, log, dialog "internal safety stop". The dialog is not awaited, so the Verdict page
    /// follows at once; the dialog service queues it.</summary>
    private void ReportSafetyStop(string text)
    {
        _log?.Warn(text);
        Observed.Forget(_dialogs.ShowAsync(new DialogRequest(InternalSafetyStopTitle, text, "OK", null, "Close")),
                        e => _log?.Warn("The internal safety stop dialog failed: " + e.Message));
    }

    internal void Apply(OffloadProgress p)
    {
        FilesText = string.Create(CultureInfo.InvariantCulture, $"{p.FilesDone} / {Fmt.Count(p.FilesTotal, "file", "files")}");
        BytesText = $"{Fmt.Size(p.BytesDone)} of {Fmt.Size(p.BytesTotal)}";
        SpeedText = string.Create(CultureInfo.InvariantCulture, $"{Math.Round(p.MBps):0} MB/s");
        EtaText = p.Eta switch
        {
            null => "",
            { TotalMinutes: < 1 } => "under a minute left",
            { TotalHours: >= 1 } e => string.Create(CultureInfo.InvariantCulture, $"{(int)e.TotalHours} h {e.Minutes} min left"),
            { } e => string.Create(CultureInfo.InvariantCulture, $"{Math.Round(e.TotalMinutes):0} min left"),
        };
        CurrentText = p.CurrentFile ?? "";
        PhaseText = PhaseName(p.Phase);
        Fraction = p.BytesTotal == 0 ? 0 : (double)p.BytesDone / p.BytesTotal;
    }

    public void Dispose() => _cts.Dispose();

    private async Task CancelAsync()
    {
        var answer = await _dialogs.ShowAsync(new DialogRequest("Stop the offload?",
            "The file being copied is discarded; everything already copied stays, and the card is not changed.", "Stop", null, "Keep going"))
            .ConfigureAwait(true);
        if (answer == DialogResult.Primary) await _cts.CancelAsync().ConfigureAwait(true);
    }
}
