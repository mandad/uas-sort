// src/UasSort.Review/Commit/PreflightVm.cs
namespace UasSort.Review;

/// <summary>The ports the Commit pages touch themselves; everything else happens inside Part 07's CommitSession.</summary>
public sealed record CommitPorts(IDraftStore Drafts, IDialogService Dialogs, IUiDispatcher Ui);

/// <summary>One acknowledgement checkbox of the sheet (Ref §10.2), keyed like PreflightAcks.</summary>
public sealed partial class AckVm(AckKey key) : ObservableObject
{
    public AckKey Key { get; } = key;
    public string Text => Key.Message;
    [ObservableProperty] public partial bool IsChecked { get; set; }
    public override string ToString() => Text;
}

/// <summary>The preflight sheet (Ref §10.2) over Part 07's CommitSession: Open begins the session (lock, thumbnail pause, preflight;
/// nothing written), Start offload runs it, Back or the verdict disposes it.</summary>
public sealed partial class PreflightVm : ObservableObject, IDisposable
{
    private readonly Func<Plan, CommitSession> _begin;
    private readonly CommitPorts _ports;

    public PreflightVm(Plan plan, CardSource source, Func<Plan, CommitSession> begin, CommitPorts ports)
    {
        Plan = plan;
        Source = source;
        _begin = begin;
        _ports = ports;
        BackCommand = new RelayCommand(Back);
    }

    public Plan Plan { get; }
    public CardSource Source { get; }
    public CommitSession? Session { get; private set; }
    public OffloadBatch? Batch => Session?.Batch;
    public PreflightReport? Report => Session?.Preflight;
    public ObservableCollection<AckVm> Acks { get; } = [];
    public IReadOnlyList<string> Blocking { get; private set; } = [];
    public IReadOnlyList<string> Warnings { get; private set; } = [];
    public IReadOnlyList<string> Infos { get; private set; } = [];
    public IReadOnlyList<string> FoldersToCreate { get; private set; } = [];
    public IReadOnlyList<string> FoldersAppended { get; private set; } = [];
    public IReadOnlyList<string> VolumeLines { get; private set; } = [];

    [ObservableProperty] public partial bool CanStart { get; private set; }
    [ObservableProperty] public partial string? LockMessage { get; private set; }

    public IRelayCommand BackCommand { get; }
    public event Action? BackRequested;

    /// <summary>Begins the Commit (CommitSession.Begin: lock, thumbnail pause, compile, preflight; writes nothing) and fills the sheet.</summary>
    public void Open()
    {
        var session = Session ??= _begin(Plan);
        var report = session.Preflight;
        LockMessage = report.Issues.FirstOrDefault(i => i.Code == IssueCode.OffloadLockHeld)?.Message;

        Acks.Clear();
        foreach (var key in session.RequiredAcks)
        {
            var ack = new AckVm(key);
            ack.PropertyChanged += (_, _) => UpdateCanStart();
            Acks.Add(ack);
        }
        Blocking = [.. report.Issues.Where(i => i.Severity == IssueSeverity.Blocking).Select(i => i.Message)];
        Warnings = [.. report.Issues.Where(i => i.Severity == IssueSeverity.Warning && !i.RequiresAckAtPreflight).Select(i => i.Message)];
        Infos = [.. report.Issues.Where(i => i.Severity == IssueSeverity.Info).Select(i => i.Message)];
        FoldersToCreate = report.FoldersToCreate;
        FoldersAppended = [.. report.FoldersAppended.Select(f => $"{f.Path} · {f.Confidence}")];
        VolumeLines = [.. report.Volumes.Select(VolumeLine)];
        OnPropertyChanged(string.Empty);
        UpdateCanStart();
    }

    /// <summary>Start offload = CommitSession.StartAsync (Part 07 owns the order); the draft is deleted only for a failure-free run.</summary>
    public async Task<CommitResult> StartAsync(IProgress<OffloadProgress> progress, CancellationToken ct)
    {
        if (!CanStart || Session is not { } session)
            throw new InvalidOperationException("Start offload is disabled until every blocking issue is gone and every box is ticked.");
        var result = await session.StartAsync(Checked(), progress, ct).ConfigureAwait(true);
        if (result.FailureFree) _ports.Drafts.Delete(Source.DraftKey);
        return result;
    }

    /// <summary>Back, or the verdict being shown: releases the offload lock and the thumbnail pause; deletes nothing.</summary>
    public void Dispose() => Session?.Dispose();

    internal static string VolumeLine(VolumeNeed v)
        => $"{v.Volume} · {Fmt.Count(v.Files, "file", "files")} · {Fmt.Size(v.Bytes)} · {Fmt.Size(v.FreeBytes)} free → {Fmt.Size(v.FreeBytes - v.Bytes)} after";

    private void Back()
    {
        Dispose();
        BackRequested?.Invoke();
    }

    private HashSet<AckKey> Checked() => [.. Acks.Where(a => a.IsChecked).Select(a => a.Key)];

    private void UpdateCanStart() => CanStart = Session is { } s && PreflightAcks.CanStart(s.Preflight, Checked());
}
