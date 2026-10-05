// src/UasSort.Review/PhotoCleanup/PhotoCleanupVm.cs
namespace UasSort.Review;

public enum PhotoCleanupStep { Choose, Review, Confirm, Running, Result }

/// <summary>The "Clean up Picture Offload" page (spec 2026-10-04 §2): Choose (cutoff; verify against Lightroom) → Review (rows by day,
/// Keep/Delete) → Confirm (the move acknowledgement; a second one when an unverified row is on Delete) → Running (cancellable between
/// items) → Result.</summary>
public sealed partial class PhotoCleanupVm : ObservableObject, IDisposable
{
    private readonly PhotoCleanupEngine _engine;
    private readonly IDialogService _dialogs;
    private readonly IUiDispatcher _ui;
    private readonly TimeProvider _time;
    private readonly CancellationTokenSource _life = new();
    private readonly HashSet<string> _delete = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _run;
    private PhotoSurvey? _survey;
    private string? _lightroomFolder;
    private Action? _keepOnDevice;

    public PhotoCleanupVm(PhotoCleanupEngine engine, IDialogService dialogs, IUiDispatcher ui, TimeProvider time)
    {
        _engine = engine;
        _dialogs = dialogs;
        _ui = ui;
        _time = time;
        NextCommand = new AsyncRelayCommand(NextAsync, () => CanNext);
        BackCommand = new RelayCommand(Back, () => Step is PhotoCleanupStep.Choose or PhotoCleanupStep.Review or PhotoCleanupStep.Confirm);
        DeleteAllCommand = new RelayCommand(() => SetAll(delete: true), () => Step == PhotoCleanupStep.Review);
        KeepAllCommand = new RelayCommand(() => SetAll(delete: false), () => Step == PhotoCleanupStep.Review);
        RunCommand = new AsyncRelayCommand(RunAsync, () => CanRun);
        CancelCommand = new AsyncRelayCommand(CancelAsync, () => Step == PhotoCleanupStep.Running);
        DoneCommand = new RelayCommand(() => Closed?.Invoke(), () => Step == PhotoCleanupStep.Result);
        KeepOnDeviceCommand = new RelayCommand(KeepOnDevice, () => CanKeepOnDevice);
    }

    public ObservableCollection<PhotoCleanupRowVm> Rows { get; } = [];

    [ObservableProperty] public partial PhotoCleanupStep Step { get; private set; }
    [ObservableProperty] public partial bool IsBusy { get; private set; }
    [ObservableProperty] public partial string? BusyText { get; private set; }
    [ObservableProperty] public partial string? BlockingText { get; private set; }
    [ObservableProperty] public partial bool CanKeepOnDevice { get; private set; }
    [ObservableProperty] public partial string PhotoRootText { get; private set; } = "";
    [ObservableProperty] public partial DateTimeOffset? PickedDate { get; set; }
    [ObservableProperty] public partial DateOnly? Cutoff { get; private set; }
    [ObservableProperty] public partial bool Verify { get; set; }
    [ObservableProperty] public partial bool VerifyEnabled { get; private set; }
    [ObservableProperty] public partial string? VerifyUnavailableText { get; private set; }
    [ObservableProperty] public partial PhotoCleanupPlan? Plan { get; private set; }
    [ObservableProperty] public partial string ModeText { get; private set; } = "";
    [ObservableProperty] public partial string? LightroomProblemText { get; private set; }
    [ObservableProperty] public partial string TotalsText { get; private set; } = "";
    [ObservableProperty] public partial string? NotTouchedText { get; private set; }
    [ObservableProperty] public partial string? NotEligibleText { get; private set; }
    [ObservableProperty] public partial IReadOnlyList<string> NotEligibleLines { get; private set; } = [];
    [ObservableProperty] public partial string AckMoveText { get; private set; } = "";
    [ObservableProperty] public partial bool AckMove { get; set; }
    [ObservableProperty] public partial bool ShowUnverifiedAck { get; private set; }
    [ObservableProperty] public partial string UnverifiedAckText { get; private set; } = "";
    [ObservableProperty] public partial bool AckUnverified { get; set; }
    [ObservableProperty] public partial bool CanNext { get; private set; }
    [ObservableProperty] public partial bool CanRun { get; private set; }
    [ObservableProperty] public partial string RunButtonText { get; private set; } = "Move to the Recycle Bin";
    [ObservableProperty] public partial string ProgressText { get; private set; } = "";
    [ObservableProperty] public partial PhotoCleanupResultVm? Result { get; private set; }

    public IAsyncRelayCommand NextCommand { get; }
    public IRelayCommand BackCommand { get; }
    public IRelayCommand DeleteAllCommand { get; }
    public IRelayCommand KeepAllCommand { get; }
    public IAsyncRelayCommand RunCommand { get; }
    public IAsyncRelayCommand CancelCommand { get; }
    public IRelayCommand DoneCommand { get; }
    public IRelayCommand KeepOnDeviceCommand { get; }

    public event Action? Closed;

    /// <summary>Prepares the page off the UI thread (ledger gate, listing, survey). Expected failures become BlockingText.</summary>
    public async Task OpenAsync()
    {
        IsBusy = true;
        BusyText = "Reading Picture Offload…";
        BlockingText = null;
        UpdateGates();
        var progress = new UiProgress<PhotoScanProgress>(_ui, p => { if (IsBusy) BusyText = ProgressLine(p); });
        PhotoCleanupPreparation prep;
        try
        {
            prep = await _engine.Prepare(progress, _life.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or UnsafeIoException)
        {
            BlockingText = $"Picture Offload couldn't be read: {e.Message}";
            return;
        }
        finally
        {
            IsBusy = false;
            BusyText = null;
            UpdateGates();
        }
        _survey = prep.Survey;
        _lightroomFolder = prep.LightroomFolder;
        _keepOnDevice = prep.KeepOnDevice;
        CanKeepOnDevice = prep.KeepOnDevice is not null;
        BlockingText = prep.BlockingText;
        VerifyEnabled = prep.Survey is not null && prep.VerifyUnavailableText is null;
        VerifyUnavailableText = prep.VerifyUnavailableText;
        if (!VerifyEnabled) Verify = false;
        PhotoRootText = prep.Survey is { } s
            ? $"{s.PhotoRoot} · {Fmt.Count(s.Items.Length, "item", "items")}"
              + (s.NotTouched.IsEmpty ? "" : $" · {Fmt.Count(s.NotTouched.Length, "other file or folder", "other files and folders")} not touched")
            : "";
        UpdateGates();
    }

    /// <summary>The cutoff is the picker's own calendar day, never converted through UtcDateTime or ToLocalTime (as CleanupVm.PickDate).</summary>
    public void PickDate(DateTimeOffset? picked) => PickedDate = picked;

    public void Dispose()
    {
        _life.Cancel();
        _life.Dispose();
        _run?.Dispose();
    }

    partial void OnPickedDateChanged(DateTimeOffset? value)
    {
        Cutoff = value is { } v ? DateOnly.FromDateTime(v.DateTime) : null;
        UpdateGates();
    }

    partial void OnVerifyChanged(bool value)
    {
        if (value && !VerifyEnabled)
        {
            Verify = false;
            return;
        }
        UpdateGates();
    }

    partial void OnAckMoveChanged(bool value) => UpdateGates();
    partial void OnAckUnverifiedChanged(bool value) => UpdateGates();
    partial void OnStepChanged(PhotoCleanupStep value) => UpdateGates();

    private async Task NextAsync()
    {
        switch (Step)
        {
            case PhotoCleanupStep.Choose:
                await BuildPlanAsync().ConfigureAwait(true);
                break;
            case PhotoCleanupStep.Review:
                AckMove = false;
                AckUnverified = false;
                Step = PhotoCleanupStep.Confirm;
                UpdateTexts();
                break;
        }
    }

    private async Task BuildPlanAsync()
    {
        if (_survey is not { } survey || Cutoff is not { } cutoff) return;
        var request = new PhotoCleanupRequest(Verify ? PhotoCleanupMode.Verify : PhotoCleanupMode.BeforeDate, cutoff, Verify ? _lightroomFolder : null);
        IsBusy = true;
        BusyText = Verify ? "Checking the Lightroom library…" : "Building the list…";
        UpdateGates();
        var progress = new UiProgress<PhotoScanProgress>(_ui, p => { if (IsBusy) BusyText = ProgressLine(p); });
        PhotoCleanupPlan plan;
        try
        {
            plan = await _engine.Plan(survey, request, progress, _life.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or UnsafeIoException)
        {
            BlockingText = $"The list couldn't be built: {e.Message}";
            return;
        }
        finally
        {
            IsBusy = false;
            BusyText = null;
            UpdateGates();
        }
        Plan = plan;
        _delete.Clear();
        _delete.UnionWith(plan.DefaultDelete());
        Step = PhotoCleanupStep.Review;
        Sync();
        UpdateTexts();
        UpdateGates();
    }

    private void SetRow(PhotoCleanupRowVm row, RowDecision decision)
    {
        if (Step != PhotoCleanupStep.Review) return;
        if (decision == RowDecision.Delete) _delete.Add(row.Key);
        else _delete.Remove(row.Key);
        Sync();
        UpdateTexts();
        UpdateGates();
    }

    /// <summary>[Delete all] puts every row on Delete (unverified ones then need the second acknowledgement); [Keep all] none.</summary>
    private void SetAll(bool delete)
    {
        if (Plan is not { } p) return;
        _delete.Clear();
        if (delete) _delete.UnionWith(p.Rows.Select(r => r.Key));
        Sync();
        UpdateTexts();
        UpdateGates();
    }

    /// <summary>Rows in plan order (oldest first); the first row of each local date carries the day header.</summary>
    private void Sync()
    {
        IReadOnlyList<PhotoRow> rows = Plan is { } p ? p.Rows : [];
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal);
        DateOnly? last = null;
        foreach (var r in rows)
        {
            var day = r.Item.FirstDate;
            headers[r.Key] = day is { } d && d != last ? Fmt.DayWithWeekday(d) : null;
            last = day;
        }
        CollectionSync.Sync(Rows, rows, r => r.Key, r => new PhotoCleanupRowVm(r, SetRow),
            (vm, r) => vm.Update(r, _delete.Contains(r.Key) ? RowDecision.Delete : RowDecision.Keep, headers[r.Key]));
        NotEligibleLines = Plan is { } q ? [.. q.NotEligible.Select(r => $"{r.Key} · {r.Why}")] : [];
    }

    private void UpdateTexts()
    {
        if (Plan is not { } p)
        {
            ModeText = "";
            LightroomProblemText = null;
            TotalsText = "";
            NotTouchedText = null;
            NotEligibleText = null;
            AckMoveText = "";
            ShowUnverifiedAck = false;
            return;
        }
        var t = p.Totals(_delete);
        var items = t.Photos + t.Sets;
        ModeText = p.Request.Mode == PhotoCleanupMode.BeforeDate
            ? "By date: rows are not checked against Lightroom."
            : string.Create(CultureInfo.InvariantCulture,
                $"Checked against {p.Request.LightroomFolder}: {p.Rows.Count(r => r.Verification.Verified)} of {Fmt.Count(p.Rows.Length, "row", "rows")} confirmed.");
        LightroomProblemText = p.Lightroom?.Problem;                                                     // branch-2 ruling: never swallowed
        TotalsText = $"{Fmt.Count(t.Photos, "photo", "photos")}, {Fmt.Count(t.Sets, "set", "sets")}, {Fmt.Size(t.Bytes)} to the Recycle Bin";
        NotTouchedText = p.NotTouched.IsEmpty ? null
            : $"Not touched: {Fmt.Count(p.NotTouched.Length, "other file or folder", "other files and folders")} in Picture Offload";
        NotEligibleText = p.NotEligible.IsEmpty ? null : $"Not eligible (kept): {Fmt.Count(p.NotEligible.Length, "item", "items")}";
        AckMoveText = $"Move {Fmt.Count(items, "item", "items")} ({Fmt.Size(t.Bytes)}) from Picture Offload to the Recycle Bin"
                      + (p.Request.Mode == PhotoCleanupMode.BeforeDate ? " — not checked against Lightroom" : "");   // resolved ambiguity 8b
        ShowUnverifiedAck = t.Unverified > 0;
        UnverifiedAckText = string.Create(CultureInfo.InvariantCulture,
            $"{t.Unverified} of them are not confirmed in Lightroom or your library — the Recycle Bin may hold their only copy");
        RunButtonText = $"Move {Fmt.Count(items, "item", "items")} to the Recycle Bin";
        if (!ShowUnverifiedAck) AckUnverified = false;
    }

    private void UpdateGates()
    {
        var ready = !IsBusy && BlockingText is null;
        CanNext = Step switch
        {
            PhotoCleanupStep.Choose => ready && _survey is not null && Cutoff is not null && (!Verify || VerifyEnabled),
            PhotoCleanupStep.Review => ready && Plan is not null && _delete.Count > 0,
            _ => false,
        };
        CanRun = ready && Step == PhotoCleanupStep.Confirm && Plan is not null && _delete.Count > 0 && AckMove && (!ShowUnverifiedAck || AckUnverified);
        NextCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
        DeleteAllCommand.NotifyCanExecuteChanged();
        KeepAllCommand.NotifyCanExecuteChanged();
        RunCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        DoneCommand.NotifyCanExecuteChanged();
        KeepOnDeviceCommand.NotifyCanExecuteChanged();
    }

    private void Back()
    {
        switch (Step)
        {
            case PhotoCleanupStep.Confirm:
                Step = PhotoCleanupStep.Review;
                break;
            case PhotoCleanupStep.Review:
                Step = PhotoCleanupStep.Choose;
                break;
            default:
                Closed?.Invoke();
                break;
        }
    }

    private async Task RunAsync()
    {
        if (!CanRun || Plan is not { } plan) return;
        ConfirmedPhotoCleanupPlan confirmed;
        try
        {
            confirmed = plan.Confirm(new PhotoCleanupAck(plan.Fingerprint, _delete.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase), AckMove,
                                                         ShowUnverifiedAck && AckUnverified), _time);
        }
        catch (InvalidOperationException e)
        {
            BlockingText = e.Message;
            return;
        }
        _run = new CancellationTokenSource();
        Step = PhotoCleanupStep.Running;
        ProgressText = "Starting…";
        var progress = new UiProgress<PhotoCleanupProgress>(_ui, p => ProgressText = string.Create(CultureInfo.InvariantCulture,
            $"{p.ItemsDone} / {Fmt.Count(p.ItemsTotal, "item", "items")} · {Fmt.Size(p.BytesDone)} of {Fmt.Size(p.BytesTotal)}{(p.Current is null ? "" : " · " + p.Current)}"));
        PhotoCleanupResult result;
#pragma warning disable CA1031 // the page must never stay stuck on Running after items may have moved; every failure is shown
        try
        {
            result = await _engine.Run(confirmed, progress, _run.Token).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            BlockingText = $"The cleanup stopped unexpectedly: {ex.Message}. Items already moved are in the Recycle Bin and recorded in the history; open the page again to see what is left.";
            Step = PhotoCleanupStep.Review;
            return;
        }
        string reportPath;
        string? reportProblem = null;
        try
        {
            reportPath = _engine.SaveReport(PhotoCleanupReports.Build(result));
        }
        catch (Exception ex)
        {
            reportPath = "";
            reportProblem = $"The report couldn't be saved: {ex.Message}";
        }
#pragma warning restore CA1031
        Result = new PhotoCleanupResultVm(result, reportPath, reportProblem, _engine.Shell);
        Step = PhotoCleanupStep.Result;
    }

    private async Task CancelAsync()
    {
        var answer = await _dialogs.ShowAsync(new DialogRequest("Stop the cleanup?",
            "Stop after the current item? Items already moved stay in the Recycle Bin.", "Stop", null, "Keep going")).ConfigureAwait(true);
        if (answer == DialogResult.Primary && _run is { } run) await run.CancelAsync().ConfigureAwait(true);
    }

    /// <summary>[Keep on this device] on a cloud-only ledger folder refusal: pin it, then prepare again.</summary>
    private void KeepOnDevice()
    {
        if (_keepOnDevice is not { } keep) return;
        try
        {
            keep();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or UnsafeIoException)
        {
            BlockingText = $"Keeping the history folder on this device failed: {e.Message}";
            return;
        }
        Observed.Forget(OpenAsync(), e => BlockingText = e.Message);
    }

    private static string ProgressLine(PhotoScanProgress p) => string.Create(CultureInfo.InvariantCulture, $"{p.Phase}… {p.Done} / {p.Total}");
}
