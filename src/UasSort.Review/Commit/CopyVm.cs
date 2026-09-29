// src/UasSort.Review/Commit/CopyVm.cs
namespace UasSort.Review;

/// <summary>The Copy page: progress at 10 Hz and Cancel (Ref §7.2 step 12, §10.3).</summary>
public sealed partial class CopyVm : ObservableObject, IDisposable
{
    private readonly PreflightVm _preflight;
    private readonly IDialogService _dialogs;
    private readonly IUiDispatcher _ui;
    private readonly CancellationTokenSource _cts = new();

    public CopyVm(PreflightVm preflight, IDialogService dialogs, IUiDispatcher ui)
    {
        _preflight = preflight;
        _dialogs = dialogs;
        _ui = ui;
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
        try
        {
            var result = await _preflight.StartAsync(new UiProgress<OffloadProgress>(_ui, Apply), _cts.Token).ConfigureAwait(true);
            if (result.Offload.Stop == StopReason.LedgerWriteFailed && result.Offload.Outcomes.All(o => o is NotStarted))
                ErrorText = "Couldn't create or open the history file, so nothing was copied. The verdict below lists every file as not copied.";
            return result;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UnsafeIoException)
        {
            ErrorText = $"The offload stopped before it could finish: {ex.Message}";
            return null;
        }
        finally
        {
            IsRunning = false;
        }
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
