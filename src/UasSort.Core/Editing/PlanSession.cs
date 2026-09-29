// src/UasSort.Core/Editing/PlanSession.cs
using System.Collections.Immutable;
using UasSort.Core.Planning;

namespace UasSort.Core.Editing;

/// <summary>Edit log, undo/redo of (Tuning, edits) states, drafts, and the threading contract of Ref §4.2.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "The SemaphoreSlim never allocates its wait handle (AvailableWaitHandle is unused), so it holds nothing to release; the registry contract has no Dispose.")]
public sealed class PlanSession
{
    private sealed record State(Tuning Tuning, ImmutableList<PlanEdit> Edits);

    private readonly PlanBase _base;
    private readonly IPlanDeriver _deriver;
    private readonly TimeProvider _clock;
    private readonly ImmutableArray<Issue> _sessionIssues;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _lock = new();
    private readonly Stack<State> _undo = new();
    private readonly Stack<State> _redo = new();
    private State _state;
    private SessionFlags _flags = new(false);
    private Plan _current;
    private int _revision;
    private int _published;
    private CancellationTokenSource? _previewCts;
    private Tuning? _previewTuning;

    public event Action<Plan>? Changed;

    public PlanSession(PlanBase b, IPlanDeriver deriver, Tuning tuning, TimeProvider clock)
        : this(b, deriver, new State(tuning, []), clock, []) { }

    private PlanSession(PlanBase b, IPlanDeriver deriver, State state, TimeProvider clock, ImmutableArray<Issue> sessionIssues)
    {
        _base = b;
        _deriver = deriver;
        _clock = clock;
        _sessionIssues = sessionIssues;
        _state = state;
        _current = DeriveNow(state, CancellationToken.None);
        _published = _current.Revision;
    }

    public Plan Current { get { lock (_lock) return _current; } }
    public Tuning CommittedTuning { get { lock (_lock) return _state.Tuning; } }
    public IReadOnlyList<PlanEdit> Edits { get { lock (_lock) return _state.Edits; } }
    public int UndoDepth { get { lock (_lock) return _undo.Count; } }
    public bool CanUndo => UndoDepth > 0;
    public bool CanRedo { get { lock (_lock) return _redo.Count > 0; } }

    public static PlanSession Resume(PlanBase b, Draft d, IPlanDeriver deriver, out int dropped) =>
        Resume(b, d, deriver, TimeProvider.System, out dropped);

    public static PlanSession Resume(PlanBase b, Draft d, IPlanDeriver deriver, TimeProvider clock, out int dropped)
    {
        var known = b.Items.Select(i => i.Raw.Unit.Id).ToHashSet();
        dropped = d.Edits.Count(e => PlanEditRefs.Referenced(e).Any(r => !known.Contains(r)));
        ImmutableArray<Issue> extra = dropped > 0
            ? [new Issue(IssueSeverity.Info, IssueCode.StaleEditsDropped, $"{d.Edits.Length - dropped} of {d.Edits.Length} edits still apply",
                         null, [], false)]
            : [];
        return new PlanSession(b, deriver, new State(d.Tuning, [.. d.Edits]), clock, extra);
    }

    public Draft ToDraft()
    {
        State s;
        lock (_lock) s = _state;
        var inv = _base.Scan.Inventory;
        return new Draft(1, inv.Source.DraftKey, inv.InventoryHash, _clock.GetUtcNow().UtcDateTime, s.Tuning, [.. s.Edits]);
    }

    public Task<EditResult> ApplyAsync(PlanEdit edit, CancellationToken ct) => ApplyAllAsync([edit], ct);

    public async Task<EditResult> ApplyAllAsync(IReadOnlyList<PlanEdit> edits, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => ApplyCore(edits, ct), ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private EditResult ApplyCore(IReadOnlyList<PlanEdit> edits, CancellationToken ct)
    {
        State s;
        Plan plan;
        lock (_lock) { s = _state; plan = _current; }
        for (var i = 0; i < edits.Count; i++)
        {
            if (EditValidator.Validate(plan, edits[i]) is { } rejected) return rejected;
            s = s with { Edits = s.Edits.Add(edits[i]) };
            if (i < edits.Count - 1) plan = DeriveNow(s, ct);        // the next edit is validated against this one
        }
        var result = DeriveNow(s, ct);
        lock (_lock)
        {
            _undo.Push(_state);
            _redo.Clear();
            _state = s;
        }
        return new Applied(Commit(result));
    }

    public void Preview(Tuning t) => _ = PreviewAsync(t);

    public Task PreviewAsync(Tuning t)
    {
        CancellationTokenSource cts;
        lock (_lock)
        {
            _previewCts?.Cancel();
            _previewCts = cts = new CancellationTokenSource();
            _previewTuning = t;
        }
        var token = cts.Token;
        return Task.Run(() => PreviewCore(t, token), CancellationToken.None);
    }

    private void PreviewCore(Tuning t, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            State s;
            lock (_lock) s = _state;
            Plan p;
            try { p = DeriveNow(s with { Tuning = t }, token); }
            catch (OperationCanceledException) { return; }
            lock (_lock)
            {
                if (token.IsCancellationRequested) return;
                if (!ReferenceEquals(s.Edits, _state.Edits)) continue;   // superseded by a completed edit: re-run on the new log
                if (p.Revision <= _published) return;
                _published = p.Revision;
            }
            Changed?.Invoke(p);
            return;
        }
    }

    public async Task CommitTuningAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                Tuning? t;
                State s;
                lock (_lock)
                {
                    t = _previewTuning;
                    _previewTuning = null;
                    _previewCts?.Cancel();
                    _previewCts = null;
                    s = _state;
                }
                if (t is null || t == s.Tuning) return;
                var next = s with { Tuning = t };
                var p = DeriveNow(next, CancellationToken.None);
                lock (_lock)
                {
                    _undo.Push(_state);
                    _redo.Clear();
                    _state = next;
                }
                Commit(p);
            }).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<Plan> UndoAsync() => StepAsync(undo: true);

    public Task<Plan> RedoAsync() => StepAsync(undo: false);

    private async Task<Plan> StepAsync(bool undo)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                State s;
                lock (_lock)
                {
                    var from = undo ? _undo : _redo;
                    var to = undo ? _redo : _undo;
                    if (from.Count == 0) return _current;
                    _previewCts?.Cancel();
                    _previewTuning = null;
                    s = from.Pop();
                    to.Push(_state);
                    _state = s;
                }
                return Commit(DeriveNow(s, CancellationToken.None));
            }).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void AcceptLedgerIssues() => _ = AcceptLedgerIssuesAsync();

    public async Task AcceptLedgerIssuesAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                State s;
                lock (_lock)
                {
                    _flags = new SessionFlags(true);
                    s = _state;
                }
                Commit(DeriveNow(s, CancellationToken.None));
            }).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task WhenIdleAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        _gate.Release();
    }

    private Plan DeriveNow(State s, CancellationToken ct)
    {
        var rev = Interlocked.Increment(ref _revision);
        SessionFlags flags;
        lock (_lock) flags = _flags;
        var p = _deriver.Derive(_base, s.Tuning, s.Edits, flags, rev, ct);
        return _sessionIssues.IsDefaultOrEmpty ? p : p with { Issues = p.Issues.AddRange(_sessionIssues) };
    }

    private Plan Commit(Plan p)
    {
        lock (_lock)
        {
            if (p.Revision <= _published) p = p with { Revision = Interlocked.Increment(ref _revision) };
            _published = p.Revision;
            _current = p;
        }
        Changed?.Invoke(p);
        return p;
    }
}
