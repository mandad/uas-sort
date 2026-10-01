// src/UasSort.Review/Commit/VerdictVm.cs
namespace UasSort.Review;

/// <summary>What the Verdict page writes to and reads from; Part 11 binds Reaudit to "reload the ledger, re-list, CardAudit.Audit".</summary>
public sealed record VerdictPorts(ILedgerStore Ledger, string Machine, TimeProvider Time, IDialogService Dialogs, IShellLauncher Shell,
                                  IDeviceEject Eject, Func<FormatVerdict> Reaudit);

/// <summary>One "Not copied" row: Part 07's NotCopiedRow plus the display name. A row that no decision could clear
/// (CanDecide false: added or changed since the scan, or the card was swapped) can't be selected; CannotDecideReason is its hint.</summary>
public sealed partial class NotCopiedRowVm : ObservableObject
{
    public NotCopiedRowVm(NotCopiedRow row, string text, Action<NotCopiedRowVm> toggle)
    {
        Row = row;
        Text = text;
        ToggleCommand = new RelayCommand(() => toggle(this), () => CanDecide);
    }

    public NotCopiedRow Row { get; }
    public ItemId Unit => Row.Unit;
    public NotCopiedKind Kind => Row.Kind;
    public string Text { get; }
    public string Detail => Row.Detail;
    public long Bytes => Row.Bytes;
    public string SizeText => Fmt.Size(Bytes);
    public DateOnly? LocalDate => Row.LocalDate;
    public bool IsPhotoLike => Kind is NotCopiedKind.Photo or NotCopiedKind.Set;
    public bool CanDecide => Row.CanDecide;
    public string? CannotDecideReason => Row.CannotDecideReason;
    [ObservableProperty] public partial bool IsSelected { get; set; }
    public IRelayCommand ToggleCommand { get; }
    public override string ToString() => Text;
}

public sealed class NotCopiedDayVm(DateOnly date, string text, IRelayCommand selectDayCommand)
{
    public DateOnly Date { get; } = date;
    public string Text { get; } = text;
    public IRelayCommand SelectDayCommand { get; } = selectDayCommand;
    public override string ToString() => Text;
}

public sealed partial class EjectVm : ObservableObject
{
    public EjectVm(string volume, IDeviceEject eject)
    {
        Volume = volume;
        Text = $"Eject {Fmt.Drive(volume)}";
        EjectCommand = new RelayCommand(() => ResultText = eject.Eject(volume) switch
        {
            Ejected e => $"Ejected {Fmt.Drive(e.Volume)}",
            EjectRefused r => $"Couldn't eject {Fmt.Drive(r.Volume)}: {r.Reason}",
        });
    }

    public string Volume { get; }
    public string Text { get; }
    public IRelayCommand EjectCommand { get; }
    [ObservableProperty] public partial string? ResultText { get; private set; }
    public override string ToString() => Text;
}

public sealed class VerdictGroupRowVm(string text, string path, IRelayCommand openFolderCommand)
{
    public string Text { get; } = text;
    public string Path { get; } = path;
    public IRelayCommand OpenFolderCommand { get; } = openFolderCommand;
    public override string ToString() => Text;
}

/// <summary>The Verdict page (Ref §10.5).</summary>
public sealed partial class VerdictVm : ObservableObject
{
    private readonly Plan _plan;
    private readonly VerdictPorts _ports;
    private readonly string? _reportPath;
    private readonly string? _runId;
    private ImmutableArray<string> _lastDecisionIds = [];
    private bool _cleanupEnabled;

    public VerdictVm(FormatVerdict verdict, Plan plan, OffloadResult? result, string? reportPath, VerdictPorts ports)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(ports);
        _plan = plan;
        _ports = ports;
        _reportPath = reportPath;
        _runId = result?.RunId;
        RecordImportedCommand = new AsyncRelayCommand(() => DecideAsync(DecisionKind.AssumedImported), () => CanDecide(DecisionKind.AssumedImported));
        MarkNotNeededCommand = new AsyncRelayCommand(() => DecideAsync(DecisionKind.Dismissed), () => CanDecide(DecisionKind.Dismissed));
        UndoCommand = new AsyncRelayCommand(UndoAsync, () => _lastDecisionIds.Length > 0);
        SelectAllPhotosAndSetsCommand = new RelayCommand(SelectAllPhotosAndSets, () => NotCopied.Any(r => InSomeDay(r) && !r.IsSelected));
        ClearSelectionCommand = new RelayCommand(ClearSelection, () => NotCopied.Any(r => r.IsSelected));
        OpenPhotoRootCommand = new RelayCommand(() => _ports.Shell.OpenFolder(plan.Base.Scan.Settings.PhotoRoot));
        OpenReportCommand = new RelayCommand(() => _ports.Shell.OpenFile(_reportPath!), () => _reportPath is not null);
        CleanupCommand = new RelayCommand(() => CleanupRequested?.Invoke(), () => _cleanupEnabled);
        DoneCommand = new RelayCommand(() => DoneRequested?.Invoke());
        ShowPlanCommand = new RelayCommand(() => ShowPlanRequested?.Invoke());
        Ejects = [.. (result?.VolumesNeedingSafeRemoval ?? []).Select(v => new EjectVm(v, ports.Eject))];
        var root = plan.Base.Scan.Settings.VideoRoot;
        Groups = [.. plan.Groups.Select(g => g.Target switch
            {
                NewFolder n => System.IO.Path.Join(root, n.RelPath),
                Append a => a.Folder.FullPath,
                AlreadyImported ai => ai.Folder.FullPath,
                NothingToCopy or SkipGroup => null,
            }).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(p => new VerdictGroupRowVm(System.IO.Path.GetFileName(p), p, new RelayCommand(() => _ports.Shell.OpenFolder(p))))];
        ApplyVerdict(verdict);
    }

    [ObservableProperty] public partial VerdictLevel Level { get; private set; }
    [ObservableProperty] public partial string Headline { get; private set; } = "";
    [ObservableProperty] public partial string LevelText { get; private set; } = "";
    [ObservableProperty] public partial IReadOnlyList<string> CategoryLines { get; private set; } = [];
    [ObservableProperty] public partial IReadOnlyList<string> CardChanges { get; private set; } = [];
    [ObservableProperty] public partial string? SafeRemovalNote { get; private set; }
    [ObservableProperty] public partial string? SelectionText { get; private set; }
    [ObservableProperty] public partial bool CanCleanup { get; private set; }
    [ObservableProperty] public partial string? CleanupTooltip { get; private set; }
    /// <summary>Why the Commit stopped before it finished (CopyVm.ErrorText: the IO stop, or the history file that couldn't be
    /// opened); null after a run that finished. ShellVm sets it; the Verdict page shows it as an error InfoBar at the top.</summary>
    [ObservableProperty] public partial string? StopText { get; internal set; }
    [ObservableProperty] public partial string? HistoryWarning { get; internal set; }

    public ObservableCollection<NotCopiedRowVm> NotCopied { get; } = [];
    public ObservableCollection<NotCopiedDayVm> NotCopiedDays { get; } = [];
    public IReadOnlyList<EjectVm> Ejects { get; }
    public IReadOnlyList<VerdictGroupRowVm> Groups { get; }

    public IAsyncRelayCommand RecordImportedCommand { get; }
    public IAsyncRelayCommand MarkNotNeededCommand { get; }
    public IAsyncRelayCommand UndoCommand { get; }
    /// <summary>Selects every photo and set that a day button covers (decidable, with a local date): exactly the union of
    /// clicking every day button. Never selects a video, an unknown file or a row that can't be decided. Disabled when
    /// every such row is already selected (or there is none).</summary>
    public IRelayCommand SelectAllPhotosAndSetsCommand { get; }
    /// <summary>Deselects every row; disabled when nothing is selected.</summary>
    public IRelayCommand ClearSelectionCommand { get; }
    public IRelayCommand OpenPhotoRootCommand { get; }
    public IRelayCommand OpenReportCommand { get; }
    public IRelayCommand CleanupCommand { get; }
    public IRelayCommand DoneCommand { get; }
    public IRelayCommand ShowPlanCommand { get; }

    public event Action? CleanupRequested;
    public event Action? DoneRequested;
    public event Action? ShowPlanRequested;

    public static string LevelName(VerdictLevel l) => l switch
    {
        VerdictLevel.Safe => "Safe to format",
        VerdictLevel.SafeWithAssumptions => "Safe, with assumptions",
        VerdictLevel.NotSafe => "Don't format yet",
        _ => l.ToString(),
    };

    public static string CategoryName(AuditCategory c) => c switch
    {
        AuditCategory.VerifiedThisRun => "Verified this run",
        AuditCategory.InLedger => "In the history, verified",
        AuditCategory.ConfirmedByYou => "Confirmed by you",
        AuditCategory.NameSizeMatch => "Matched by name and size",
        AuditCategory.SkippedByRule => "Skipped by rule",
        AuditCategory.AssumedByRule => "Assumed imported",
        AuditCategory.Unaccounted => "Not accounted for",
        _ => c.ToString(),
    };

    public void SetCleanupAvailability(bool enabled, string? tooltip)
    {
        _cleanupEnabled = enabled;
        CanCleanup = enabled;
        CleanupTooltip = tooltip;
        CleanupCommand.NotifyCanExecuteChanged();
    }

    private void ApplyVerdict(FormatVerdict v)
    {
        ArgumentNullException.ThrowIfNull(v);
        Level = v.Level;
        Headline = v.Headline;
        LevelText = LevelName(v.Level);
        CategoryLines = [.. Enum.GetValues<AuditCategory>().Where(c => v.Counts.GetValueOrDefault(c) > 0)
                                .Select(c => string.Create(CultureInfo.InvariantCulture, $"{CategoryName(c)}: {v.Counts[c]}"))];
        CardChanges = v.CardChanges;
        SafeRemovalNote = v.SafeRemovalNote;

        var items = _plan.Base.Items.ToDictionary(i => i.Raw.Unit.Id);
        NotCopied.Clear();
        foreach (var row in VerdictDecisions.NotCopied(v, _plan))
            NotCopied.Add(new NotCopiedRowVm(row, items.TryGetValue(row.Unit, out var item) ? item.Raw.Name : row.Unit.CardRelPath, Toggle));
        NotCopiedDays.Clear();
        foreach (var day in NotCopied.Where(r => r.IsPhotoLike && r.CanDecide && r.LocalDate is not null)
                                     .GroupBy(r => r.LocalDate!.Value).OrderBy(g => g.Key))
        {
            var d = day.Key;
            NotCopiedDays.Add(new NotCopiedDayVm(d, $"{Fmt.DayWithWeekday(d)} · {Fmt.Count(day.Count(), "item", "items")}",
                                                 new RelayCommand(() => SelectDay(d))));
        }
        SelectionChanged();
    }

    /// <summary>A video or unknown file is selected alone; selecting a photo or set clears a selected video/unknown file.
    /// A row that can't be decided is never selected.</summary>
    private void Toggle(NotCopiedRowVm row)
    {
        if (!row.CanDecide) return;
        var select = !row.IsSelected;
        if (select)
        {
            foreach (var other in NotCopied.Where(r => r != row && r.IsSelected && (!row.IsPhotoLike || !r.IsPhotoLike)))
                other.IsSelected = false;
        }
        row.IsSelected = select;
        SelectionChanged();
    }

    /// <summary>Per-day selection covers exactly VerdictDecisions.Day (photos and sets of that local day) that can be decided,
    /// and clears a selected video or unknown file.</summary>
    private void SelectDay(DateOnly day)
    {
        var dayUnits = VerdictDecisions.Day(NotCopied.Select(r => r.Row), day).Select(r => r.Unit).ToHashSet();
        foreach (var r in NotCopied)
            r.IsSelected = r.IsPhotoLike && r.CanDecide && (r.IsSelected || dayUnits.Contains(r.Unit));
        SelectionChanged();
    }

    /// <summary>A row some day button covers: NotCopiedDays is built from exactly these rows.</summary>
    private static bool InSomeDay(NotCopiedRowVm r) => r.IsPhotoLike && r.CanDecide && r.LocalDate is not null;

    /// <summary>The union of SelectDay over every day: covered rows are added, selected photos and sets stay, and a selected
    /// video or unknown file is cleared.</summary>
    private void SelectAllPhotosAndSets()
    {
        foreach (var r in NotCopied)
            r.IsSelected = r.IsPhotoLike && r.CanDecide && (r.IsSelected || InSomeDay(r));
        SelectionChanged();
    }

    private void ClearSelection()
    {
        foreach (var r in NotCopied) r.IsSelected = false;
        SelectionChanged();
    }

    private List<NotCopiedRowVm> Selected() => [.. NotCopied.Where(r => r.IsSelected)];

    private bool CanDecide(DecisionKind kind) => VerdictDecisions.Check(kind, [.. Selected().Select(r => r.Row)]).Ok;

    private void SelectionChanged()
    {
        var s = Selected();
        SelectionText = s.Count == 0 ? null : $"{Describe(s)} · {Fmt.Size(s.Sum(r => r.Bytes))}";
        RecordImportedCommand.NotifyCanExecuteChanged();
        MarkNotNeededCommand.NotifyCanExecuteChanged();
        SelectAllPhotosAndSetsCommand.NotifyCanExecuteChanged();
        ClearSelectionCommand.NotifyCanExecuteChanged();
    }

    private static string Describe(IReadOnlyList<NotCopiedRowVm> rows)
    {
        var parts = new List<string>();
        void Add(NotCopiedKind k, string one, string many)
        {
            var n = rows.Count(r => r.Kind == k);
            if (n > 0) parts.Add(Fmt.Count(n, one, many));
        }
        Add(NotCopiedKind.Video, "video", "videos");
        Add(NotCopiedKind.Photo, "photo", "photos");
        Add(NotCopiedKind.Set, "set", "sets");
        Add(NotCopiedKind.Unknown, "unknown file", "unknown files");
        return string.Join(", ", parts);
    }

    /// <summary>Ref §10.5: confirm with Core's text, write one decision record per file through the own ledger file, re-audit.
    /// A selection Core can't resolve to scanned files (VerdictDecisions.Records throws) is shown as an error; nothing is written.
    /// A ledger write failure (the video root went away, the disk is full, the guard refused the file) is shown as a dialog; the
    /// records actually written become the [Undo] target and the page is re-audited when anything was written.</summary>
    private async Task DecideAsync(DecisionKind kind)
    {
        List<NotCopiedRow> rows = [.. Selected().Select(r => r.Row)];
        if (!VerdictDecisions.Check(kind, rows).Ok) return;
        var title = kind == DecisionKind.AssumedImported ? "Record as already imported?" : "Mark as not needed?";
        var body = VerdictDecisions.Confirmation(kind, rows) + " uas-sort records this in the history; you can undo it.";
        var answer = await _ports.Dialogs.ShowAsync(new DialogRequest(title, body, kind == DecisionKind.AssumedImported ? "Record" : "Mark", null, "Cancel"))
                                         .ConfigureAwait(true);
        if (answer != DialogResult.Primary) return;
        ImmutableArray<DecisionRecord> records;
        try
        {
            records = VerdictDecisions.Records(kind, rows, _plan, _runId, _ports.Machine, _ports.Time);
        }
        catch (InvalidOperationException ex)
        {
            await _ports.Dialogs.ShowAsync(new DialogRequest("Nothing was recorded", ex.Message, "OK", null, "Close")).ConfigureAwait(true);
            return;
        }
        var (written, failure) = AppendAll(records, r => r.Id);
        if (written.Count > 0)
        {
            _lastDecisionIds = [.. written];
            UndoCommand.NotifyCanExecuteChanged();
            ApplyVerdict(_ports.Reaudit());
        }
        if (failure is not null)
            await _ports.Dialogs.ShowAsync(new DialogRequest(written.Count == 0 ? "Nothing was recorded" : "Only part of the selection was recorded",
                WriteFailure(failure) + (written.Count == 0 ? "" : " You can undo what was recorded."), "OK", null, "Close")).ConfigureAwait(true);
    }

    /// <summary>Undo writes one revoke per decision just made (VerdictDecisions.Revokes), then re-audits. When a revoke can't be
    /// written, the dialog says so and [Undo] stays available for the decisions not yet revoked.</summary>
    private async Task UndoAsync()
    {
        var (revoked, failure) = AppendAll(VerdictDecisions.Revokes(_lastDecisionIds, _ports.Machine, _ports.Time), r => r.Decision);
        _lastDecisionIds = [.. _lastDecisionIds.Except(revoked, StringComparer.Ordinal)];
        UndoCommand.NotifyCanExecuteChanged();
        if (revoked.Count > 0) ApplyVerdict(_ports.Reaudit());
        if (failure is not null)
            await _ports.Dialogs.ShowAsync(new DialogRequest("Couldn't undo", WriteFailure(failure), "OK", null, "Close")).ConfigureAwait(true);
    }

    /// <summary>Appends through this PC's own ledger file; returns the keys of the records written before any IO or guard failure.</summary>
    private (List<string> Written, Exception? Failure) AppendAll<T>(IEnumerable<T> records, Func<T, string> key) where T : LedgerRecord
    {
        var written = new List<string>();
        try
        {
            using var writer = _ports.Ledger.OpenOwn();
            foreach (var r in records)
            {
                writer.Append(r);
                written.Add(key(r));
            }
            return (written, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UnsafeIoException)
        {
            return (written, ex);
        }
    }

    private static string WriteFailure(Exception ex)
        => ex is UnsafeIoException ? "Internal safety stop: " + ex.Message : "The history file couldn't be written: " + ex.Message;
}
