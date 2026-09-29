// src/UasSort.Review/Review/ReviewVm.cs
namespace UasSort.Review;

public enum InfoSeverity { Informational, Success, Warning, Error }

/// <summary>One InfoBar under the title bar (Ref §9.2).</summary>
public sealed class InfoBarVm(string key, InfoSeverity severity, string message, bool isClosable, IReadOnlyList<QuickFixVm> actions) : IKeyed
{
    public string Key { get; } = key;
    public InfoSeverity Severity { get; } = severity;
    public string Message { get; } = message;
    public bool IsClosable { get; } = isClosable;
    public IReadOnlyList<QuickFixVm> Actions { get; } = actions;
    public override string ToString() => Message;
}

/// <summary>A saved draft for this card, already replayed into its own session (Ref §9.11).</summary>
public sealed record DraftOffer(Draft Draft, PlanSession Session, int Dropped);

/// <summary>The Review stage (Ref §9.2–9.13). Plans arrive from PlanSession off the UI thread and are applied through IUiDispatcher.
/// A fresh session is built by the caller as <c>new PlanSession(b, deriver, tuning, time)</c> (Part 06).</summary>
public sealed partial class ReviewVm : ObservableObject, IReviewActions, ITuningHost, IDisposable
{
    private static readonly HashSet<IssueCode> BannerCodes =
        [IssueCode.LedgerCloudOnly, IssueCode.LedgerNotPinned, IssueCode.LedgerParseIssue, IssueCode.LedgerUnwritable,
         IssueCode.RootMissing, IssueCode.RootsUnconfirmed];

    private readonly ReviewServices _s;
    private readonly IDecisionService _decisions;
    private readonly HashSet<string> _closedBars = new(StringComparer.Ordinal);
    private const string FaultKey = "sessionFault";

    private MapBridge? _map;
    private Action<Plan>? _onChanged;
    private bool _clockMismatchDismissed;
    private string? _faultText;

    public ReviewVm(PlanSession session, ReviewServices services, IDecisionService decisions, DraftOffer? offer = null)
    {
        _s = services;
        _decisions = decisions;
        Session = session;
        Videos = new VideosTabVm(this);
        Photos = new PhotosTabVm(e => ApplyEditAsync(e), UndoConfirmedAsync);
        Other = new OtherTabVm(UndoConfirmedAsync, UndismissEntriesAsync);
        Tuning = new TuningVm(this, services.Time, services.Ui);
        Issues = new IssuesVm(RunIssueFixAsync);
        UndoCommand = new AsyncRelayCommand(UndoAsync, () => CanUndo && !IsReadOnly);
        RedoCommand = new AsyncRelayCommand(RedoAsync, () => CanRedo && !IsReadOnly);
        OffloadCommand = new RelayCommand(() => OffloadRequested?.Invoke(), () => CanOffload);
        // Nothing new: "Show verdict" without an offload. Read-only (the plan shown from the Verdict page): back to that verdict.
        ShowVerdictCommand = new RelayCommand(() => VerdictRequested?.Invoke(), () => IsReadOnly || NothingNew);

        Plan = session.Current;
        Index = new PlanIndex(Plan);
        Videos.MapSelectRequested += (g, ids, fit) => _map?.Send(MapProjection.Select(Plan, g, ids, fit));
        var inv = Plan.Base.Scan.Inventory;
        var files = inv.Entries.Length;
        var bytes = inv.Entries.Sum(e => e.Size);
        CardChipText = inv.Source.Identity is { } id
            ? Fmt.CardChip(inv.Source.Root, id, inv.CameraModel, files, bytes)
            : $"{inv.Source.Root} · {Fmt.ModelName(inv.CameraModel)} · {Fmt.Count(files, "file", "files")} · {Fmt.Size(bytes)}";
        Tuning.SetCommitted(Plan.Tuning);
        MapBase = Plan.Base.Scan.Settings.Map.Base;
        InitDraftOffer(offer);
        Attach(Session);
        Apply(Plan);
    }

    public PlanSession Session { get; private set; }
    public Plan Plan { get; private set; }
    public PlanIndex Index { get; private set; }
    public VideosTabVm Videos { get; }
    public PhotosTabVm Photos { get; }
    public OtherTabVm Other { get; }
    public TuningVm Tuning { get; }
    public IssuesVm Issues { get; }
    public ObservableCollection<InfoBarVm> InfoBars { get; } = [];
    public string CardChipText { get; }

    [ObservableProperty] public partial int SelectedTab { get; set; }
    [ObservableProperty] public partial string FooterText { get; private set; } = "";
    [ObservableProperty] public partial bool CanOffload { get; private set; }
    [ObservableProperty] public partial string? OffloadDisabledReason { get; private set; }
    [ObservableProperty] public partial bool NothingNew { get; private set; }
    [ObservableProperty] public partial string? EmptyStateText { get; private set; }
    [ObservableProperty] public partial bool CanUndo { get; private set; }
    [ObservableProperty] public partial bool CanRedo { get; private set; }
    [ObservableProperty] public partial string? LastError { get; private set; }
    [ObservableProperty] public partial bool IsReadOnly { get; set; }

    /// <summary>The map base layer ("streets", "satellite" or "none"); the App's selector binds it (Ref §9.6).</summary>
    [ObservableProperty] public partial string MapBase { get; set; } = "streets";

    public IAsyncRelayCommand UndoCommand { get; }
    public IAsyncRelayCommand RedoCommand { get; }
    public IRelayCommand OffloadCommand { get; }
    public IRelayCommand ShowVerdictCommand { get; }

    public event Action? OffloadRequested;
    public event Action? VerdictRequested;
    public event Action? RescanRequested;
    public event Action<ItemId>? FocusRenameRequested;
    public event Action<string>? UiActionRequested;
    public event Action<MapContextMenu>? MapContextMenuRequested;
    public event Action<MapToHost>? MapStatus;

    public MapBridge? Map
    {
        get => _map;
        set
        {
            if (_map is not null) _map.Received -= OnMapMessage;
            _map = value;
            if (_map is null) return;
            _map.Received += OnMapMessage;
            _map.SendData(Plan);
        }
    }

    /// <summary>Applies a plan on the UI thread; a plan with a lower Revision than the one shown is ignored (Ref §4.2). Revisions are
    /// counted per PlanSession, so a plan from a session that is no longer <see cref="Session"/> (a draft was resumed while an edit
    /// of the old session was still deriving) is dropped whatever its revision.</summary>
    internal void OnPlanArrived(PlanSession from, Plan p)
    {
        if (!ReferenceEquals(from, Session) || p.Revision < Plan.Revision) return;
        Apply(p);
    }

    public void CloseInfoBar(string key)
    {
        if (string.Equals(key, "clockMismatch", StringComparison.Ordinal)) _clockMismatchDismissed = true;
        else if (string.Equals(key, FaultKey, StringComparison.Ordinal)) _faultText = null;
        else _closedBars.Add(key);
        RebuildInfoBars();
    }

    public void OpenClip(ItemId id)
        => _s.Shell.OpenFile(PathRules.Join(Plan.Base.Scan.Inventory.Source.Root, id.CardRelPath.Replace('/', '\\')));

    /// <summary>Issues flyout "go to": a video anchor selects the Videos tab, its card (or folded run) and the clip; a photo or set
    /// anchor selects the Photos tab and its day (showing imported days when that day is hidden).</summary>
    public void GoTo(ItemId anchor)
    {
        if (!Index.Items.TryGetValue(anchor, out var item)) return;
        if (item.Raw.Kind == ItemKind.Video)
        {
            SelectedTab = 0;
            Videos.Reveal(anchor);
            return;
        }
        SelectedTab = 1;
        if (!Photos.Days.Any(d => d.Items.Any(i => i.Raw.Unit.Id == anchor))
            && Plan.Base.PhotoDays.Any(d => d.Items.Contains(anchor)))
            Photos.ShowImportedDays = true;
        Photos.SelectedDay = Photos.Days.FirstOrDefault(d => d.Items.Any(i => i.Raw.Unit.Id == anchor)) ?? Photos.SelectedDay;
    }

    /// <summary>The clip menu's "Move to group ▸" targets: every other card that can take clips (not AlreadyImported).</summary>
    public IReadOnlyList<GroupCardVm> MoveTargets()
        => [.. Videos.Timeline.OfType<GroupCardVm>()
                  .Where(c => !ReferenceEquals(c, Videos.SelectedCard) && c.Group is { Target: not AlreadyImported })];

    public void Dispose()
    {
        Detach(Session);
        Map = null;
        Tuning.Dispose();
        DisposeDrafts();
    }

    partial void InitDraftOffer(DraftOffer? offer);
    partial void AddDraftInfoBar(List<InfoBarVm> bars);
    partial void OnEditCommitted();
    partial void DisposeDrafts();

    partial void OnIsReadOnlyChanged(bool value)
    {
        UpdateOffload();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    partial void OnMapBaseChanged(string value) => _map?.Send(new MapSetBase(value));

    /// <summary>Subscribes to a session with a Changed handler that remembers which session raised the plan.</summary>
    private void Attach(PlanSession session)
    {
        _onChanged = p => _s.Ui.Post(() => OnPlanArrived(session, p));
        session.Changed += _onChanged;
        session.Faulted += OnSessionFaulted;
    }

    private void Detach(PlanSession session)
    {
        if (_onChanged is not null) session.Changed -= _onChanged;
        session.Faulted -= OnSessionFaulted;
        _onChanged = null;
    }

    /// <summary>Awaits a fire-and-forget action (a keyboard shortcut) so a fault is never swallowed: it is logged and shown as the
    /// same error InfoBar as a session fault; the plan shown stays the last good one.</summary>
    private void Observe(Task task) => _ = ObserveAsync(task);

    private async Task ObserveAsync(Task task)
    {
#pragma warning disable CA1031 // every fault of an unobserved action is reported, never rethrown into nowhere
        try
        {
            await task.ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            OnSessionFaulted(e);
        }
#pragma warning restore CA1031
    }

    /// <summary>A fire-and-forget derive (slider preview, [Accept and continue], a keyboard shortcut's edit) threw: log it and show it
    /// as an error InfoBar; the plan shown stays the last good one.</summary>
    private void OnSessionFaulted(Exception e)
    {
        var text = "Couldn't update the plan: " + e.Message;
        _s.Log.Warn(text);
        _s.Ui.Post(() =>
        {
            _faultText = text;
            RebuildInfoBars();
        });
    }

    private void Accept(PlanSession from, Plan p) => _s.Ui.Post(() => OnPlanArrived(from, p));

    private void Apply(Plan p)
    {
        Plan = p;
        Index = new PlanIndex(p);
        Videos.Update(Index);
        Photos.Update(Index);
        Other.Update(Index);
        Issues.Update(p.Issues);
        Tuning.Sync(p.Tuning, p.Groups.Length);
        FooterText = Footer.Text(p, _s.Space);
        // Review Focus #1: every AlreadyImported group folded (NothingToCopy groups follow the spec's fold rule and stay unfolded),
        // Offload disabled with "Nothing new on this card"; Core's rule (Planner.Derive raises IssueCode.NothingNew when every
        // item is Imported or Decided)
        NothingNew = p.Issues.Any(i => i.Code == IssueCode.NothingNew) || p.Base.Items.All(i => i.Newness is Imported or Decided);
        EmptyStateText = NothingNew ? "Nothing new on this card" : null;
        UpdateUndo();
        UpdateOffload();
        RebuildInfoBars();
        _map?.SendData(p);
    }

    private void UpdateOffload()
    {
        OffloadDisabledReason = IsReadOnly ? "The plan is read-only now"
                              : NothingNew ? "Nothing new on this card"
                              : Issues.HasBlocking ? Issues.BlockedTooltip
                              : Plan.Included.Count == 0 ? "Nothing is ticked to copy"
                              : null;
        CanOffload = OffloadDisabledReason is null;
        OffloadCommand.NotifyCanExecuteChanged();
        ShowVerdictCommand.NotifyCanExecuteChanged();
    }

    private void RebuildInfoBars()
    {
        var bars = new List<InfoBarVm>();
        var items = Plan.Base.Items;
        if (_faultText is { } fault) bars.Add(new InfoBarVm(FaultKey, InfoSeverity.Error, fault, true, []));
        bars.Add(new InfoBarVm("clock", InfoSeverity.Informational, Plan.Base.Clock.Headline, false, []));
        if (!_clockMismatchDismissed && ClockText.MismatchInfoBar(Plan.Base.Clock, Plan.Base.Scan.Clock, items) is { } mismatch)
            bars.Add(new InfoBarVm("clockMismatch", InfoSeverity.Warning, mismatch, true, []));
        AddDraftInfoBar(bars);
        if (FirstRunText() is { } firstRun && !_closedBars.Contains("firstRun"))
            bars.Add(new InfoBarVm("firstRun", InfoSeverity.Informational, firstRun, true, []));
        foreach (var issue in Plan.Issues.Where(i => BannerCodes.Contains(i.Code)))
        {
            var severity = issue.Severity switch
            {
                IssueSeverity.Blocking => InfoSeverity.Error,
                IssueSeverity.Warning => InfoSeverity.Warning,
                _ => InfoSeverity.Informational,
            };
            bars.Add(new InfoBarVm($"issue:{issue.Code}:{issue.Message}", severity, issue.Message, false,
                                   [.. issue.QuickFixes.Select(f => new QuickFixVm(f.Label, () => RunIssueFixAsync(issue, f)))]));
        }
        InfoBars.Clear();
        foreach (var b in bars) InfoBars.Add(b);
    }

    private string? FirstRunText()
    {
        if (Plan.Base.Scan.Ledger.SourceFiles.Length > 0 || Plan.Base.PhotoDays.Length == 0) return null;
        if (Plan.Base.WatermarkUtc is not { } w)
            return "No photo history yet, and no videos were imported outside uas-sort: every photo not already in the library is ticked.";
        var zone = Plan.Base.Items.OrderBy(i => Math.Abs((i.Time.CaptureUtc - w).Ticks)).FirstOrDefault()?.Time.TzId ?? _s.Time.LocalTimeZone.Id;
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(w, DateTimeKind.Utc), TimeZoneInfo.FindSystemTimeZoneById(zone));
        return $"No photo history yet. Photos after {Fmt.Day(DateOnly.FromDateTime(local))} {Fmt.Clock(local)} {ZoneNames.Abbreviation(zone, w)}, within 75 min before it, or on days with new videos are ticked; the others are probably already in Lightroom. You can confirm them on the last screen.";
    }

    private void OnMapMessage(MapToHost m)
    {
        switch (m)
        {
            case MapClick c: Videos.OnMapClick(c); break;
            case MapClickEmpty: Videos.OnMapClickEmpty(); break;
            case MapContextMenu cm: MapContextMenuRequested?.Invoke(cm); break;
            default: MapStatus?.Invoke(m); break;
        }
    }

    private Task UndoConfirmedAsync(IReadOnlyList<Item> items)
    {
        var targets = items.SelectMany(i => DecisionTargets.For(i)).ToList();
        _decisions.Revoke(_decisions.DecisionIdsFor(targets, Plan.Base.Scan.Ledger));
        RescanRequested?.Invoke();
        return Task.CompletedTask;
    }

    /// <summary>[Un-dismiss] on an unknown file marked not needed on the Verdict page: one revoke per live decision on it (Ref §10.5).</summary>
    private Task UndismissEntriesAsync(IReadOnlyList<CardEntry> entries)
    {
        var targets = entries.Select(DecisionTargets.ForEntry).ToList();
        _decisions.Revoke(_decisions.DecisionIdsFor(targets, Plan.Base.Scan.Ledger));
        RescanRequested?.Invoke();
        return Task.CompletedTask;
    }

    private Task RunIssueFixAsync(Issue issue, QuickFix fix)
        => fix.Edits.Length > 0 ? ApplyEditsAsync(fix.Edits) : RunUiFix(fix.Label, issue.Anchor);

    private Task RunUiFix(string label, ItemId? anchor)
    {
        switch (label)
        {
            case "Accept and continue":
                Session.AcceptLedgerIssues();
                break;
            case "Name it" when anchor is { } a:
                FocusRenameRequested?.Invoke(a);
                break;
            default:
                UiActionRequested?.Invoke(label);
                break;
        }
        return Task.CompletedTask;
    }
}
