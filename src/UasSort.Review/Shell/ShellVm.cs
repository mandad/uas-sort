// src/UasSort.Review/Shell/ShellVm.cs
namespace UasSort.Review;

public enum Stage { Setup, Card, Scan, Review, Preflight, Copy, Verdict, Cleanup, Settings }

public enum CleanupOrigin { Review, Verdict }

/// <summary>Factories and facts the shell needs; Part 11's composition root builds them from Core and Platform.</summary>
public sealed record ShellDeps(
    SettingsLoad Settings,
    Func<SettingsLoad, SetupVm> CreateSetup,
    Func<CardStageVm> CreateCard,
    Func<Settings, ScanStageVm> CreateScan,
    Func<CardSource, PlanBase, ReviewVm> CreateReview,
    Func<ReviewVm, PreflightVm> CreatePreflight,
    Func<PreflightVm, CopyVm> CreateCopy,
    Func<ReviewVm, CommitResult?, VerdictVm> CreateVerdict,
    Func<CleanupOrigin, OffloadResult?, CleanupVm> CreateCleanup,
    Func<Settings, SettingsPageVm> CreateSettings,
    Func<ReviewVm, FormatVerdict> AuditNow,
    Func<CardSource, bool> CardPresent,
    Func<CardSource, (string? Refusal, string? Detail)> VolumeRefusal,
    Action<Settings> SaveSettings)
{
    /// <summary>Where the shell logs each change of [Clean up card…]'s availability for a card source (Info, Task U4); none: not logged.</summary>
    public IReviewLog? Log { get; init; }
}

/// <summary>The stage machine behind MainWindow's Frame and TitleBar (Ref §9.1, §9.2).</summary>
public sealed partial class ShellVm : ObservableObject
{
    private readonly ShellDeps _deps;
    private (Stage Stage, object? Current)? _beforeSettings;
    private CommitResult? _lastResult;
    private SettingsPageVm? _settingsPage;
    private string? _loggedCleanupAvailability;

    public ShellVm(ShellDeps deps)
    {
        _deps = deps;
        Settings = deps.Settings.Settings;
        RescanCommand = new AsyncRelayCommand(RescanAsync, () => CanRescan);
        SettingsCommand = new RelayCommand(OpenSettings, () => CanOpenSettings);
        CleanupCommand = new RelayCommand(() => OpenCleanup(Stage == Stage.Verdict || PlanFromVerdict ? CleanupOrigin.Verdict : CleanupOrigin.Review),
                                          () => CleanupEnabled);
    }

    public Settings Settings { get; private set; }
    public CardSource? Source { get; private set; }
    public CardStageVm? Card { get; private set; }
    public ReviewVm? Review { get; private set; }
    public PreflightVm? Preflight { get; private set; }
    public CopyVm? Copy { get; private set; }
    public VerdictVm? Verdict { get; private set; }
    public CleanupVm? Cleanup { get; private set; }

    [ObservableProperty] public partial Stage Stage { get; private set; }
    [ObservableProperty] public partial object? Current { get; private set; }
    [ObservableProperty] public partial string? CardChipText { get; private set; }
    [ObservableProperty] public partial bool CanRescan { get; private set; }
    [ObservableProperty] public partial bool CanOpenSettings { get; private set; }
    [ObservableProperty] public partial bool CanBrowse { get; private set; }
    [ObservableProperty] public partial bool CanUndoRedo { get; private set; }
    [ObservableProperty] public partial bool CleanupEnabled { get; private set; }
    [ObservableProperty] public partial string? CleanupTooltip { get; private set; }
    /// <summary>Why [Clean up card…] is disabled, as visible text (a disabled button shows no tooltip); for a volume that fails the
    /// cleanup volume check it names the failing rule. Null while the button is enabled (Task U4).</summary>
    [ObservableProperty] public partial string? CleanupUnavailableText { get; private set; }
    [ObservableProperty] public partial bool IsScanning { get; private set; }

    public IAsyncRelayCommand RescanCommand { get; }
    public IRelayCommand SettingsCommand { get; }
    public IRelayCommand CleanupCommand { get; }

    /// <summary>An unexpected fault of a fire-and-forget scan or rescan (Ref §12 "log, keep running"): the App logs it; the shell has
    /// already shown it on the Card stage.</summary>
    public event Action<Exception>? Faulted;

    /// <summary>The run whose copies the unhandled-exception dialog lists (Ref §12): the Commit in progress, else the last one shown on
    /// the Verdict page; null once the next card is shown.</summary>
    public string? RunId => Preflight?.Batch?.RunId ?? _lastResult?.Batch.RunId;

    public Task StartAsync()
    {
        if (!Settings.RootsConfirmed || _deps.Settings.Recovered) ShowSetup();
        else ShowCard();
        return Task.CompletedTask;
    }

    /// <summary>Scans a chosen source and shows its Review. A Cancel returns to the Card stage without picking the card again
    /// (Ref §9.1 Scan); an expected scan error stays on the Scan page with its text and Rescan. Callers that don't await it start
    /// it through <see cref="Observed"/>, so an unexpected fault reaches <see cref="OnScanFault"/> (never a stuck Scan stage).</summary>
    public async Task UseCardAsync(CardSource s)
    {
        Source = s;
        var scan = _deps.CreateScan(Settings);
        PlanBase? b;
        IsScanning = true;
        try
        {
            Go(Stage.Scan, scan);
            b = await scan.RunAsync(s).ConfigureAwait(true);
        }
        finally
        {
            IsScanning = false;
        }
        if (b is null)
        {
            if (scan.ErrorText is null) ShowCard(autoPick: false);
            else UpdateFlags();
            return;
        }
        ShowReview(s, b);
    }

    public Task RescanAsync()
    {
        if (Stage is Stage.Preflight or Stage.Copy or Stage.Verdict or Stage.Cleanup or Stage.Setup || PlanFromVerdict) return Task.CompletedTask;
        if (Source is { } s) return UseCardAsync(s);
        ShowCard();
        return Task.CompletedTask;
    }

    /// <summary>CleanupVm's rescan after the deletes: the normal scan, a fresh Review, and the verdict level recomputed without an offload.</summary>
    public async Task<VerdictLevel> RescanForCleanupAsync()
    {
        var source = Source ?? throw new InvalidOperationException("No card to rescan");
        var scan = _deps.CreateScan(Settings);
        var b = await scan.RunAsync(source).ConfigureAwait(true)
                ?? throw new IOException(scan.ErrorText ?? "The rescan was cancelled");
        Review?.Dispose();
        Review = Wire(_deps.CreateReview(source, b));
        _lastResult = null;
        return _deps.AuditNow(Review).Level;
    }

    public void BeginOffload()
    {
        if (Review is not { CanOffload: true } review) return;
        var preflight = _deps.CreatePreflight(review);
        preflight.BackRequested += () =>
        {
            Preflight = null;
            Go(Stage.Review, Review);
        };
        Preflight = preflight;
        preflight.Open();
        Go(Stage.Preflight, preflight);
    }

    /// <summary>Start offload: the Copy page runs the CommitSession; its CommitResult (verdict, report, offload) goes to the Verdict page.
    /// Every Commit ends on the Verdict page (Ref §9.1, §10.5): when the run returned no result (a Cancel, or CopyVm's IO catch with
    /// its ErrorText), the session is still disposed (offload lock and thumbnail pause released) and the verdict is re-audited
    /// from the card and the ledger.</summary>
    public async Task StartCopyAsync()
    {
        if (Preflight is not { CanStart: true } preflight || Review is not { } review) return;
        var copy = _deps.CreateCopy(preflight);
        Copy = copy;
        Go(Stage.Copy, copy);
        var result = await copy.RunAsync().ConfigureAwait(true);
        var clock = preflight.Plan.Base.Scan.Clock;
        ShowVerdict(review, result, copy.ErrorText, copy.WarningText);
        if (result is { Offload.Stop: null }) SaveLearnedClock(clock);
    }

    /// <summary>Ref §6.1 / §9.14: a successful run keeps what the drone clock learned (SiteLocal: the mode; a fitted zone: mode and zone),
    /// so a later card without videos converts through it. NearestSample and Setting learned nothing to keep.</summary>
    private void SaveLearnedClock(ClockModel clock)
    {
        var learned = DroneClock.ApplyLearned(Settings, clock);
        if (learned == Settings) return;
        Settings = learned;
        try
        {
            _deps.SaveSettings(Settings);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or UnsafeIoException)
        {
            Faulted?.Invoke(e);                                 // logged; the verdict is already shown and the next run saves again
        }
    }

    /// <summary>Remembers the splitter positions (Ref §9.2): Settings.Layout is saved at once through ShellDeps.SaveSettings.</summary>
    public void UpdateLayout(double timelineWidth, double mapHeightRatio)
    {
        var layout = new LayoutSettings(timelineWidth, mapHeightRatio);
        if (Settings.Layout == layout) return;
        Settings = Settings with { Layout = layout };
        _deps.SaveSettings(Settings);
    }

    public void OpenCleanup(CleanupOrigin origin)
    {
        if (!CleanupEnabled) return;
        var returnStage = origin == CleanupOrigin.Verdict ? Stage.Verdict : Stage.Review;
        var cleanup = _deps.CreateCleanup(origin, origin == CleanupOrigin.Verdict ? _lastResult?.Offload : null);
        cleanup.RescanRequested += () => Observed.Forget(UseCardAsync(Source!), OnScanFault);
        cleanup.Closed += () =>
        {
            var finished = cleanup.Step == CleanupStep.Result;
            Cleanup = null;
            if (finished || returnStage == Stage.Review) Go(Stage.Review, Review);
            else Go(Stage.Verdict, Verdict);
        };
        Cleanup = cleanup;
        Go(Stage.Cleanup, cleanup);
        cleanup.Open();
    }

    public void OpenSettings()
    {
        if (!CanOpenSettings) return;
        _beforeSettings = (Stage, Current);
        _settingsPage = _deps.CreateSettings(Settings);
        Go(Stage.Settings, _settingsPage);
    }

    /// <summary>Back from Settings. New roots, a new JPG-twin choice or drone clock (anything the plan was derived from) invalidate
    /// the Review: it is rescanned with the new settings (the draft keeps the edits), so an offload never runs against stale roots or
    /// a stale ledger.</summary>
    public void CloseSettings()
    {
        var before = Settings;
        if (_settingsPage is { } page)
        {
            Settings = page.Current;
            page.Dispose();
            _settingsPage = null;
        }
        var back = _beforeSettings;
        _beforeSettings = null;
        if (back is not { } b) return;
        Go(b.Stage, b.Current);
        if (b.Stage == Stage.Review && PlanInputsChanged(before, Settings)) Observed.Forget(RescanAsync(), OnScanFault);
    }

    internal static bool PlanInputsChanged(Settings a, Settings b) =>
        !PathRules.Equal(a.VideoRoot, b.VideoRoot)
        || !PathRules.Equal(a.PhotoRoot, b.PhotoRoot)
        || !a.PreviousPhotoRoots.SequenceEqual(b.PreviousPhotoRoots, StringComparer.OrdinalIgnoreCase)
        || a.CopyJpgTwin != b.CopyJpgTwin
        || a.DroneClockMode != b.DroneClockMode
        || !string.Equals(a.DroneClockZone, b.DroneClockZone, StringComparison.Ordinal)
        || a.RootsConfirmed != b.RootsConfirmed;

    /// <summary>Device arrival or removal (WM_DEVICECHANGE, Part 11); ignored during Commit and Cleanup.</summary>
    public void DeviceChanged()
    {
        if (Stage == Stage.Card) Card?.Refresh();
        else UpdateFlags();
    }

    private void ShowSetup()
    {
        var setup = _deps.CreateSetup(_deps.Settings);
        setup.Confirmed += s =>
        {
            Settings = s;
            ShowCard();
        };
        Go(Stage.Setup, setup);
    }

    /// <summary>The Card stage. autoPick false after a Cancel or a failed scan: the card is listed but not scanned again by itself;
    /// message says why the scan failed.</summary>
    private void ShowCard(bool autoPick = true, string? message = null)
    {
        Review?.Dispose();
        Review = null;
        Preflight?.Dispose();
        Preflight = null;
        Verdict = null;
        Source = null;
        CardChipText = null;
        _lastResult = null;                                     // a later card never inherits this card's OffloadResult
        var card = _deps.CreateCard();
        // A stale Card VM (the stage moved on while a folder picker was open, or a newer Card VM replaced it) never starts a scan.
        card.CardChosen += s =>
        {
            if (Stage == Stage.Card && ReferenceEquals(Card, card)) Observed.Forget(UseCardAsync(s), OnScanFault);
        };
        Card = card;
        Go(Stage.Card, card);
        card.Refresh(autoPick);
        if (message is not null) card.Report(message);
    }

    /// <summary>An unexpected fault of a scan started without an awaiting caller (Ref §12 "log, keep running"): reported for the log,
    /// then shown on the Card stage, where Rescan, Browse and Settings work again.</summary>
    private void OnScanFault(Exception e)
    {
        var root = Source?.Root;
        IsScanning = false;
        Faulted?.Invoke(e);
        ShowCard(autoPick: false, message: $"Couldn't scan {root}: {e.Message}");
    }

    private void ShowReview(CardSource s, PlanBase b)
    {
        Review?.Dispose();
        Review = null;
        Review = Wire(_deps.CreateReview(s, b));
        CardChipText = Review.CardChipText;
        Go(Stage.Review, Review);
    }

    private ReviewVm Wire(ReviewVm review)
    {
        review.OffloadRequested += BeginOffload;
        review.RescanRequested += () => Observed.Forget(RescanAsync(), OnScanFault);
        // From the read-only plan ("Show plan" on the Verdict page) the same verdict comes back, with its result, decisions and ejects.
        review.VerdictRequested += () =>
        {
            if (review.IsReadOnly && Verdict is { } v) Go(Stage.Verdict, v);
            else ShowVerdict(review, null);
        };
        review.UiActionRequested += label =>
        {
            if (string.Equals(label, "Open Settings", StringComparison.Ordinal)) OpenSettings();
            else if (string.Equals(label, "Rescan", StringComparison.Ordinal)) Observed.Forget(RescanAsync(), OnScanFault);
        };
        return review;
    }

    /// <summary>stopText: why the Commit stopped before it finished (CopyVm.ErrorText); the Verdict page shows it at the top.
    /// historyWarning: the history or the report couldn't be fully written (CopyVm.WarningText). The result is remembered only for
    /// this verdict: the Show-verdict page of a later card (result null) has none (Ref §10.6 Verdict-origin cleanup).</summary>
    private void ShowVerdict(ReviewVm review, CommitResult? result, string? stopText = null, string? historyWarning = null)
    {
        _lastResult = result;
        var verdict = _deps.CreateVerdict(review, result);
        verdict.StopText = stopText;
        verdict.HistoryWarning = historyWarning;
        Preflight?.Dispose();   // disposes the CommitSession: releases the offload lock and the thumbnail pause
        Preflight = null;
        verdict.DoneRequested += () => ShowCard();
        verdict.CleanupRequested += () => OpenCleanup(CleanupOrigin.Verdict);
        verdict.ShowPlanRequested += () =>
        {
            review.IsReadOnly = true;
            Go(Stage.Review, review);
        };
        Verdict = verdict;
        Go(Stage.Verdict, verdict);
    }

    /// <summary>The read-only plan shown from the Verdict page: it belongs to the Verdict stage (Ref §9.1), so it keeps the Commit flags
    /// (no Rescan, no Settings) and the Verdict cleanup origin.</summary>
    private bool PlanFromVerdict => Stage == Stage.Review && Review is { IsReadOnly: true } && Verdict is not null;

    private void Go(Stage stage, object? current)
    {
        Stage = stage;
        Current = current;
        UpdateFlags();
    }

    private void UpdateFlags()
    {
        var committing = Stage is Stage.Preflight or Stage.Copy;
        CanRescan = !PlanFromVerdict && (Stage is Stage.Card or Stage.Review or Stage.Scan && !IsScanning);
        CanOpenSettings = !PlanFromVerdict && Stage is Stage.Card or Stage.Review;
        CanBrowse = Stage == Stage.Card;
        CanUndoRedo = Stage == Stage.Review && Review is { IsReadOnly: false };
        var (refusal, detail) = Source is { } s2 ? _deps.VolumeRefusal(s2) : default;
        var context = new CleanupContext(committing, IsScanning, Source, Source is { } s && _deps.CardPresent(s), refusal, detail);
        var (enabled, tooltip) = CleanupAvailability.For(context);
        var text = CleanupAvailability.Text(context);
        LogCleanupAvailability(enabled, text);
        var pageWithoutCleanup = Stage is Stage.Cleanup or Stage.Settings or Stage.Setup;
        if (pageWithoutCleanup) enabled = false;
        var noCaption = pageWithoutCleanup || Stage == Stage.Verdict || Source is null;   // Verdict shows its own footer; no card, nothing to say
        CleanupEnabled = enabled;
        CleanupTooltip = tooltip;
        CleanupUnavailableText = enabled || noCaption ? null : text;
        Verdict?.SetCleanupAvailability(enabled, tooltip, text);
        RescanCommand.NotifyCanExecuteChanged();
        SettingsCommand.NotifyCanExecuteChanged();
        CleanupCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Task U4: one Info line per change of the card's cleanup availability (deduped by source root and reason), so a later
    /// "why is it disabled" can be answered from the log. Uses the card's availability (Ref §10.6 table), not the stage override
    /// (the Cleanup, Settings and Setup pages disable the title-bar button without a reason).</summary>
    private void LogCleanupAvailability(bool enabled, string? text)
    {
        if (_deps.Log is not { } log || Source is not { } source) return;
        var line = enabled ? $"Card cleanup on {source.Root} available" : $"Card cleanup on {source.Root} unavailable: {text}";
        if (string.Equals(line, _loggedCleanupAvailability, StringComparison.Ordinal)) return;
        _loggedCleanupAvailability = line;
        log.Info(line);
    }
}
