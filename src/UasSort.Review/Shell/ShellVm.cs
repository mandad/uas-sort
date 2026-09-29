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
    Func<CardSource, string?> VolumeRefusal,
    Action<Settings> SaveSettings);

/// <summary>The stage machine behind MainWindow's Frame and TitleBar (Ref §9.1, §9.2).</summary>
public sealed partial class ShellVm : ObservableObject
{
    private readonly ShellDeps _deps;
    private (Stage Stage, object? Current)? _beforeSettings;
    private CommitResult? _lastResult;
    private SettingsPageVm? _settingsPage;

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
    [ObservableProperty] public partial bool IsScanning { get; private set; }

    public IAsyncRelayCommand RescanCommand { get; }
    public IRelayCommand SettingsCommand { get; }
    public IRelayCommand CleanupCommand { get; }

    public Task StartAsync()
    {
        if (!Settings.RootsConfirmed || _deps.Settings.Recovered) ShowSetup();
        else ShowCard();
        return Task.CompletedTask;
    }

    public async Task UseCardAsync(CardSource s)
    {
        Source = s;
        var scan = _deps.CreateScan(Settings);
        IsScanning = true;
        Go(Stage.Scan, scan);
        var b = await scan.RunAsync(s).ConfigureAwait(true);
        IsScanning = false;
        if (b is null)
        {
            if (scan.ErrorText is null) ShowCard();
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
        _lastResult = result;
        ShowVerdict(review, result, copy.ErrorText);
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
        cleanup.RescanRequested += () => _ = UseCardAsync(Source!);
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

    public void CloseSettings()
    {
        if (_settingsPage is { } page)
        {
            Settings = page.Current;
            page.Dispose();
            _settingsPage = null;
        }
        if (_beforeSettings is { } back) Go(back.Stage, back.Current);
        _beforeSettings = null;
    }

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

    private void ShowCard()
    {
        Review?.Dispose();
        Review = null;
        Preflight?.Dispose();
        Preflight = null;
        Verdict = null;
        Source = null;
        CardChipText = null;
        var card = _deps.CreateCard();
        // A stale Card VM (the stage moved on while a folder picker was open, or a newer Card VM replaced it) never starts a scan.
        card.CardChosen += s =>
        {
            if (Stage == Stage.Card && ReferenceEquals(Card, card)) _ = UseCardAsync(s);
        };
        Card = card;
        Go(Stage.Card, card);
        card.Refresh();
    }

    private void ShowReview(CardSource s, PlanBase b)
    {
        Review?.Dispose();
        Review = Wire(_deps.CreateReview(s, b));
        CardChipText = Review.CardChipText;
        Go(Stage.Review, Review);
    }

    private ReviewVm Wire(ReviewVm review)
    {
        review.OffloadRequested += BeginOffload;
        review.RescanRequested += () => _ = RescanAsync();
        // From the read-only plan ("Show plan" on the Verdict page) the same verdict comes back, with its result, decisions and ejects.
        review.VerdictRequested += () =>
        {
            if (review.IsReadOnly && Verdict is { } v) Go(Stage.Verdict, v);
            else ShowVerdict(review, null);
        };
        review.UiActionRequested += label =>
        {
            if (string.Equals(label, "Open Settings", StringComparison.Ordinal)) OpenSettings();
            else if (string.Equals(label, "Rescan", StringComparison.Ordinal)) _ = RescanAsync();
        };
        return review;
    }

    /// <summary>stopText: why the Commit stopped before it finished (CopyVm.ErrorText); the Verdict page shows it at the top.</summary>
    private void ShowVerdict(ReviewVm review, CommitResult? result, string? stopText = null)
    {
        var verdict = _deps.CreateVerdict(review, result);
        verdict.StopText = stopText;
        Preflight?.Dispose();   // disposes the CommitSession: releases the offload lock and the thumbnail pause
        Preflight = null;
        verdict.DoneRequested += ShowCard;
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
        var (enabled, tooltip) = CleanupAvailability.For(new CleanupContext(committing, IsScanning, Source,
            Source is { } s && _deps.CardPresent(s), Source is { } s2 ? _deps.VolumeRefusal(s2) : null));
        if (Stage is Stage.Cleanup or Stage.Settings or Stage.Setup) enabled = false;
        CleanupEnabled = enabled;
        CleanupTooltip = tooltip;
        Verdict?.SetCleanupAvailability(enabled, tooltip);
        RescanCommand.NotifyCanExecuteChanged();
        SettingsCommand.NotifyCanExecuteChanged();
        CleanupCommand.NotifyCanExecuteChanged();
    }
}
