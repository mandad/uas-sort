// src/UasSort.Review/Cleanup/CleanupVm.cs
namespace UasSort.Review;

/// <summary>The Card cleanup page (Ref §10.6): Choose → Not-in-library review → Confirm → Deleting → Result.</summary>
public sealed partial class CleanupVm : ObservableObject, IDisposable
{
    private readonly CleanupEngine _engine;
    private readonly IDialogService _dialogs;
    private readonly IUiDispatcher _ui;
    private readonly TimeProvider _time;
    private readonly HashSet<ItemId> _firstShown = [];
    private CleanupRows _rows = new([], []);
    private CleanupInputs? _inputs;
    private ImmutableArray<CleanupCandidate> _candidates = [];
    private string? _ackFingerprint;
    private bool _reviewShown;
    private CancellationTokenSource? _cts;
    private Action? _keepOnDevice;

    public CleanupVm(CleanupEngine engine, IDialogService dialogs, IUiDispatcher ui, TimeProvider time)
    {
        _engine = engine;
        _dialogs = dialogs;
        _ui = ui;
        _time = time;
        ContinueCommand = new RelayCommand(Continue, () => CanContinue);
        BackCommand = new RelayCommand(Back, () => Step is CleanupStep.Choose or CleanupStep.Review or CleanupStep.Confirm);
        KeepAllCommand = new RelayCommand(() => SetAll(keep: true));
        DeleteAllCommand = new RelayCommand(() => SetAll(keep: false));
        IncludeNotInLibraryCommand = new RelayCommand(() => IncludeNotInLibrary = true, () => CanIncludeNotInLibraryFix);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => CanDelete);
        CancelCommand = new AsyncRelayCommand(CancelAsync, () => Step == CleanupStep.Deleting);
        RescanCommand = new RelayCommand(() => RescanRequested?.Invoke(), () => CanRescan);
        KeepOnDeviceCommand = new RelayCommand(KeepOnDevice, () => CanKeepOnDevice);
        DoneCommand = new RelayCommand(() => Closed?.Invoke(), () => Step == CleanupStep.Result);
        GbValue = double.NaN; // after the commands: the change handler rebuilds and notifies them
    }

    public ObservableCollection<CleanupRowVm> Rows { get; } = [];
    public ObservableCollection<KeptGroupVm> Kept { get; } = [];

    [ObservableProperty] public partial CleanupStep Step { get; private set; }
    [ObservableProperty] public partial string? BlockingText { get; private set; }
    [ObservableProperty] public partial bool CanRescan { get; private set; }
    [ObservableProperty] public partial bool CanKeepOnDevice { get; private set; }
    [ObservableProperty] public partial CleanupMode Mode { get; set; }
    [ObservableProperty] public partial DateTimeOffset? PickedDate { get; set; }
    [ObservableProperty] public partial DateOnly? Before { get; private set; }
    [ObservableProperty] public partial FreeSpaceKind FreeKind { get; set; }
    [ObservableProperty] public partial double GbValue { get; set; }
    [ObservableProperty] public partial bool IncludeNotInLibrary { get; set; }
    [ObservableProperty] public partial CleanupPlan? Plan { get; private set; }
    [ObservableProperty] public partial string CardSummary { get; private set; } = "";
    [ObservableProperty] public partial string FreeNowText { get; private set; } = "";
    [ObservableProperty] public partial string WillDeleteText { get; private set; } = "";
    [ObservableProperty] public partial string CutoffText { get; private set; } = "";
    [ObservableProperty] public partial string? ShortfallText { get; private set; }
    [ObservableProperty] public partial bool CanIncludeNotInLibraryFix { get; private set; }
    [ObservableProperty] public partial string CountsText { get; private set; } = "";
    [ObservableProperty] public partial string FilesText { get; private set; } = "";
    [ObservableProperty] public partial string RangeText { get; private set; } = "";
    [ObservableProperty] public partial string FreeAfterText { get; private set; } = "";
    [ObservableProperty] public partial string EvidenceText { get; private set; } = "";
    [ObservableProperty] public partial string? NeverCopiedText { get; private set; }
    [ObservableProperty] public partial string NeverTouchedText { get; private set; } = "";
    [ObservableProperty] public partial bool AckCantBeRecovered { get; set; }
    [ObservableProperty] public partial bool AckNotInLibrary { get; set; }
    [ObservableProperty] public partial bool ShowNotInLibraryAck { get; private set; }
    [ObservableProperty] public partial string NotInLibraryAckText { get; private set; } = "";
    [ObservableProperty] public partial string DeleteButtonText { get; private set; } = "Delete";
    [ObservableProperty] public partial bool CanDelete { get; private set; }
    [ObservableProperty] public partial bool CanContinue { get; private set; }
    [ObservableProperty] public partial string ProgressText { get; private set; } = "";
    [ObservableProperty] public partial CleanupRowVm? FirstUndecided { get; private set; }
    [ObservableProperty] public partial CleanupResultVm? Result { get; private set; }

    public string CantBeRecoveredText { get; } = "Files deleted from a memory card can't be recovered.";

    public IRelayCommand ContinueCommand { get; }
    public IRelayCommand BackCommand { get; }
    public IRelayCommand KeepAllCommand { get; }
    public IRelayCommand DeleteAllCommand { get; }
    public IRelayCommand IncludeNotInLibraryCommand { get; }
    public IAsyncRelayCommand DeleteCommand { get; }
    public IAsyncRelayCommand CancelCommand { get; }
    public IRelayCommand RescanCommand { get; }
    public IRelayCommand KeepOnDeviceCommand { get; }
    public IRelayCommand DoneCommand { get; }

    public event Action? Closed;
    public event Action? RescanRequested;

    public void Open()
    {
        var prep = _engine.Prepare();
        BlockingText = prep.BlockingText;
        CanRescan = prep.OfferRescan;
        _keepOnDevice = prep.KeepOnDevice;
        CanKeepOnDevice = prep.KeepOnDevice is not null;
        KeepOnDeviceCommand.NotifyCanExecuteChanged();
        _inputs = prep.Inputs;
        if (_inputs is { } inputs)
        {
            _candidates = CleanupPlanner.Candidates(inputs);
            var v = inputs.Volume;
            var bus = v.BusType switch { "Sd" or "Mmc" => "SD card", "Usb" => "USB drive", _ => v.BusType };
            var volume = CleanupTexts.VolumeName(v.Root);
            CardSummary = $"{volume} · {bus} · {v.Identity.FileSystem} · {Fmt.Size(v.Identity.TotalBytes)} · serial {Fmt.Serial(v.Identity.VolumeSerial)}";
            FreeNowText = $"{volume} {Fmt.Size(inputs.Space.FreeBytes)} free of {Fmt.Size(inputs.Space.TotalBytes)}";
            var never = inputs.Inventory.Entries.Count(e => e.RelPath.StartsWith("MISC/", StringComparison.OrdinalIgnoreCase)
                                                         || !e.RelPath.StartsWith("DCIM/", StringComparison.OrdinalIgnoreCase));
            NeverTouchedText = $"Never touched: MISC (DJI index), system files · {Fmt.Count(never, "file", "files")}";
        }
        CanRescan = prep.OfferRescan;
        RescanCommand.NotifyCanExecuteChanged();
        Rebuild();
    }

    /// <summary>[Keep on this device] on a cloud-only ledger folder refusal: pin the folder, then prepare again (a still cloud-only
    /// file keeps the page blocked until OneDrive has downloaded it).</summary>
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
        Open();
    }

    /// <summary>The cutoff is the picker's own calendar day, never converted through UtcDateTime or ToLocalTime (Ref §10.6 Mode 1).</summary>
    public void PickDate(DateTimeOffset? picked) => PickedDate = picked;

    public void Dispose() => _cts?.Dispose();

    partial void OnPickedDateChanged(DateTimeOffset? value)
    {
        Before = value is { } v ? DateOnly.FromDateTime(v.DateTime) : null;
        Rebuild();
    }

    partial void OnModeChanged(CleanupMode value) => Rebuild();
    partial void OnFreeKindChanged(FreeSpaceKind value) => Rebuild();
    partial void OnGbValueChanged(double value) => Rebuild();

    partial void OnIncludeNotInLibraryChanged(bool value)
    {
        if (value && Step == CleanupStep.Confirm) Step = CleanupStep.Review;
        if (value && Step == CleanupStep.Review) ShowReview();
        Rebuild();
    }

    partial void OnAckCantBeRecoveredChanged(bool value) => UpdateGates();
    partial void OnAckNotInLibraryChanged(bool value) => UpdateGates();

    private CleanupRequest? Request() => Mode switch
    {
        CleanupMode.BeforeDate when Before is { } d => new CleanupRequest(CleanupMode.BeforeDate, d, null, IncludeNotInLibrary),
        CleanupMode.FreeSpace when !double.IsNaN(GbValue) && GbValue >= 0 =>
            new CleanupRequest(CleanupMode.FreeSpace, null, new FreeSpaceGoal(FreeKind, (long)Math.Round(GbValue * 1e9)), IncludeNotInLibrary),
        _ => null,
    };

    private void Rebuild()
    {
        if (_inputs is { } inputs && Request() is { } request)
        {
            Plan = CleanupPlanner.Build(inputs, _candidates, request, _rows, _firstShown);
            if (!string.Equals(Plan.Fingerprint, _ackFingerprint, StringComparison.Ordinal))
            {
                _ackFingerprint = Plan.Fingerprint;
                AckCantBeRecovered = false;
                AckNotInLibrary = false;
            }
        }
        else
        {
            Plan = null;
        }
        SyncRows();
        UpdateTexts();
        UpdateGates();
    }

    /// <summary>First time the review list is shown: every in-scope row gets its start state through CleanupRowOps.Set
    /// ("ticked for offload" rows on Keep, the others on Delete); rows that join later stay undecided (Ref §10.6).</summary>
    private void ShowReview()
    {
        if (_reviewShown || Plan is null) return;
        _reviewShown = true;
        foreach (var c in Plan.NotInLibraryInScope)
        {
            _firstShown.Add(c.Unit);
            if (_rows.Keep.Contains(c.Unit) || _rows.Delete.Contains(c.Unit)) continue;
            _rows = CleanupRowOps.Set(_rows, c.Unit, delete: !c.TickedForOffload);
        }
        Rebuild();
    }

    private void SetRow(CleanupRowVm row, RowDecision decision)
    {
        if (decision == RowDecision.Undecided) return;
        _rows = CleanupRowOps.Set(_rows, row.Unit, delete: decision == RowDecision.Delete);
        Rebuild();
    }

    /// <summary>[Keep all] / [Delete all] are Part 08's CleanupRowOps (Delete all leaves "ticked for offload" rows as they are).</summary>
    private void SetAll(bool keep)
    {
        if (Plan is not { } plan) return;
        _rows = keep ? CleanupRowOps.KeepAll(plan) : CleanupRowOps.DeleteAll(plan);
        Rebuild();
    }

    private void SyncRows()
    {
        IReadOnlyList<CleanupCandidate> inScope = Plan is { } p && IncludeNotInLibrary && _reviewShown ? p.NotInLibraryInScope : [];
        CollectionSync.Sync(Rows, inScope, c => c.Unit.CardRelPath, c => new CleanupRowVm(c, SetRow),
            (vm, c) => vm.Update(c,
                _rows.Keep.Contains(c.Unit) ? RowDecision.Keep : _rows.Delete.Contains(c.Unit) ? RowDecision.Delete : RowDecision.Undecided,
                Plan!.Undecided.Contains(c.Unit)));
        FirstUndecided = Rows.FirstOrDefault(r => r.Decision == RowDecision.Undecided);
    }

    private void UpdateTexts()
    {
        Kept.Clear();
        if (Plan is not { } p || _inputs is null)
        {
            CutoffText = Mode == CleanupMode.BeforeDate ? "Pick a date" : "Enter an amount in GB";
            WillDeleteText = "";
            ShortfallText = null;
            CanIncludeNotInLibraryFix = false;
            DeleteButtonText = "Delete";
            CountsText = "";
            FilesText = "";
            RangeText = "";
            FreeAfterText = "";
            EvidenceText = "";
            NeverCopiedText = null;
            ShowNotInLibraryAck = false;
            return;
        }
        var notInLib = p.Delete.Where(c => c.Eligibility == CleanupEligibility.NotInLibrary).ToList();
        var reviewedFiles = notInLib.Sum(c => c.Files.Length);

        // Part 08's CleanupTexts own the cutoff / nothing-to-delete / shortfall / evidence / never-copied wording (Ref §10.6).
        CutoffText = CleanupTexts.CutoffLine(p);
        WillDeleteText = p.Request.Mode == CleanupMode.FreeSpace
            ? $"will delete ≈ {Fmt.Size(p.AllocatedBytes)}" + (FreeKind == FreeSpaceKind.HaveFree ? $" = free up ≈ {Fmt.Size(p.AllocatedBytes)}" : "")
            : "";
        ShortfallText = CleanupTexts.ShortfallLine(p);
        CanIncludeNotInLibraryFix = p.Shortfall is { HeldByNotInLibrary: > 0 } && !IncludeNotInLibrary;
        IncludeNotInLibraryCommand.NotifyCanExecuteChanged();

        var kinds = new List<string>();
        void Kind(ItemKind k, string one, string many)
        {
            var n = p.Delete.Count(c => c.Kind == k);
            if (n > 0) kinds.Add(Fmt.Count(n, one, many));
        }
        Kind(ItemKind.Video, "video", "videos");
        Kind(ItemKind.Photo, "photo", "photos");
        Kind(ItemKind.Set, "set", "sets");
        CountsText = kinds.Count == 0 ? "nothing" : string.Join(" · ", kinds);
        FilesText = $"{Fmt.Count(p.FileCount, "file", "files")} · {Fmt.Size(p.AllocatedBytes)}";
        RangeText = p.Delete.Length == 0 ? "" : $"captured {Fmt.DateRange(p.Delete.Min(c => c.LocalDate), p.Delete.Max(c => c.LocalDate))}";
        FreeAfterText = $"{Fmt.Size(p.SpaceBefore.FreeBytes)} free now → ≈ {Fmt.Size(p.ExpectedFreeAfter)} after";
        EvidenceText = CleanupTexts.EvidenceSplit(p);
        NeverCopiedText = CleanupTexts.NeverCopiesLine(p);

        ShowNotInLibraryAck = notInLib.Count > 0;
        NotInLibraryAckText = $"Includes {Fmt.Count(reviewedFiles, "file", "files")} not proven to be in your library.";
        DeleteButtonText = p.Undecided.Count > 0
            ? $"Decide {Fmt.Count(p.Undecided.Count, "new row", "new rows")}"
            : $"Delete {Fmt.Count(p.FileCount, "file", "files")} ({Fmt.Size(p.AllocatedBytes)})";
        foreach (var g in p.NotDeletable.GroupBy(k => k.Reason, StringComparer.Ordinal))
            Kept.Add(new KeptGroupVm(g.Key, [.. g.Select(k =>
                $"{Path.GetFileName(k.CardRelPaths.FirstOrDefault() ?? "")}{(k.CardRelPaths.Length > 1 ? $" +{k.CardRelPaths.Length - 1}" : "")} · {Fmt.Size(k.Bytes)}")]));
    }

    private void UpdateGates()
    {
        var ready = Plan is not null && BlockingText is null;
        CanContinue = Step switch
        {
            CleanupStep.Choose => ready,
            CleanupStep.Review => ready && Plan!.Undecided.Count == 0,
            _ => false,
        };
        CanDelete = ready && Step == CleanupStep.Confirm && Plan!.Delete.Length > 0 && Plan.Undecided.Count == 0
                    && AckCantBeRecovered && (!ShowNotInLibraryAck || AckNotInLibrary);
        ContinueCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
    }

    partial void OnStepChanged(CleanupStep value)
    {
        UpdateGates();
        CancelCommand.NotifyCanExecuteChanged();
        DoneCommand.NotifyCanExecuteChanged();
    }

    private void Continue()
    {
        if (Step == CleanupStep.Choose && IncludeNotInLibrary)
        {
            Step = CleanupStep.Review;
            ShowReview();
        }
        else if (Step is CleanupStep.Choose or CleanupStep.Review)
        {
            Step = CleanupStep.Confirm;
        }
    }

    private void Back()
    {
        switch (Step)
        {
            case CleanupStep.Confirm:
                Step = IncludeNotInLibrary ? CleanupStep.Review : CleanupStep.Choose;
                break;
            case CleanupStep.Review:
                Step = CleanupStep.Choose;
                break;
            default:
                Closed?.Invoke();
                break;
        }
    }

    private async Task DeleteAsync()
    {
        if (!CanDelete || Plan is not { } plan) return;
        var notInLibrary = plan.Delete.Where(c => c.Eligibility == CleanupEligibility.NotInLibrary).Select(c => c.Unit).ToImmutableHashSet();
        var confirmed = plan.Confirm(new CleanupAck(plan.Fingerprint, AckCantBeRecovered, notInLibrary.Count > 0 && AckNotInLibrary, notInLibrary), _time);
        _cts = new CancellationTokenSource();
        Step = CleanupStep.Deleting;
        var progress = new UiProgress<CleanupProgress>(_ui, p =>
            ProgressText = string.Create(CultureInfo.InvariantCulture, $"{p.FilesDone} / {Fmt.Count(p.FilesTotal, "file", "files")} · {Fmt.Size(p.BytesDone)} of {Fmt.Size(p.BytesTotal)}"));
        CleanupResult result;
#pragma warning disable CA1031 // the page must never stay stuck on Deleting after an irreversible delete; every failure is shown (Ref §10.6)
        try
        {
            result = await _engine.Run(confirmed, progress, _cts.Token).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // Part 08's executor turns every known failure into a CleanupResult.Stop; anything else leaves no result to report on.
            // Each deleted file already has its cardDelete ledger record, so the page offers Rescan (and Back) to see the card as it is.
            BlockingText = $"The cleanup stopped unexpectedly: {ex.Message}. Some files may already be deleted; Rescan to see what is on the card.";
            CanRescan = true;
            RescanCommand.NotifyCanExecuteChanged();
            Step = CleanupStep.Choose;
            return;
        }
        VerdictLevel after;
        try
        {
            after = await _engine.Rescan().ConfigureAwait(true);
        }
        catch (Exception)
        {
            after = VerdictLevel.NotSafe;   // any rescan failure, a cancelled one included: the verdict after is NotSafe (Ref §10.6 step 4)
        }
        string reportPath;
        try
        {
            reportPath = _engine.SaveReport(CleanupReports.Build(plan, result, after));
        }
        catch (Exception ex)
        {
            reportPath = "";
            BlockingText = $"The cleanup report couldn't be saved: {ex.Message}";
        }
#pragma warning restore CA1031
        Result = new CleanupResultVm(result, after, reportPath, plan.CardRoot, _engine.Eject);
        Step = CleanupStep.Result;
    }

    private async Task CancelAsync()
    {
        var answer = await _dialogs.ShowAsync(new DialogRequest("Stop the cleanup?", "Stop the cleanup after the current file?", "Stop", null, "Keep going"))
                                   .ConfigureAwait(true);
        if (answer == DialogResult.Primary && _cts is { } cts) await cts.CancelAsync().ConfigureAwait(true);
    }
}
