// tests/UasSort.Testing/GatedPlanDeriver.cs
namespace UasSort.Testing;

/// <summary>One call into <see cref="GatedPlanDeriver"/> (added by Part 10).</summary>
public sealed class DeriveCall
{
    internal DeriveCall(int index, Tuning tuning, ImmutableArray<PlanEdit> edits, SessionFlags flags, int revision, bool held)
    {
        Index = index; Tuning = tuning; Edits = edits; Flags = flags; Revision = revision; Held = held;
    }

    public int Index { get; }
    public Tuning Tuning { get; }
    public ImmutableArray<PlanEdit> Edits { get; }
    public SessionFlags Flags { get; }
    public int Revision { get; }
    public bool Held { get; }
    public bool Completed { get; internal set; }
    public bool Cancelled { get; internal set; }
    internal ManualResetEventSlim Gate { get; } = new(false);
}

/// <summary>An IPlanDeriver whose derives block until the test opens their gate (Ref §13 "Ordering"): Part 06's armed gates,
/// plus Part 10's hold-every-call mode with a per-call record.</summary>
public sealed class GatedPlanDeriver(IPlanDeriver inner) : IPlanDeriver
{
    private readonly List<Gate> _armed = [];
    private readonly Lock _lock = new();
    private readonly List<DeriveCall> _calls = [];
    private readonly List<TaskCompletionSource<DeriveCall>> _started = [];
    private int _callCount;

    // ── Part 06 (unchanged)
    public int CallCount => Volatile.Read(ref _callCount);

    public sealed class Gate(Func<Tuning, IReadOnlyList<PlanEdit>, bool> match)
    {
        internal readonly Func<Tuning, IReadOnlyList<PlanEdit>, bool> Match = match;
        internal readonly TaskCompletionSource EnteredTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleaseTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Entered => EnteredTcs.Task;
        public void Release() => ReleaseTcs.TrySetResult();
    }

    public Gate Arm(Func<Tuning, IReadOnlyList<PlanEdit>, bool> match)
    {
        var g = new Gate(match);
        lock (_lock) _armed.Add(g);
        return g;
    }

    // ── Part 10
    /// <summary>When true, every derive that starts from now on waits for <see cref="Release(int)"/>.</summary>
    public bool Hold { get; set; }

    public IReadOnlyList<DeriveCall> Calls { get { lock (_lock) { return [.. _calls]; } } }

    public Task<DeriveCall> CallStartedAsync(int index)
    {
        lock (_lock) { return Slot(index).Task.WaitAsync(TimeSpan.FromSeconds(10)); }
    }

    public void Release(int index)
    {
        DeriveCall call;
        lock (_lock) { call = _calls[index]; }
        call.Gate.Set();
    }

    public void ReleaseAll()
    {
        Hold = false;
        foreach (var c in Calls) c.Gate.Set();
    }

    public Plan Derive(PlanBase b, Tuning t, IReadOnlyList<PlanEdit> edits, SessionFlags flags, int revision, CancellationToken ct)
    {
        Interlocked.Increment(ref _callCount);
        DeriveCall call;
        Gate? gate;
        lock (_lock)
        {
            gate = _armed.FirstOrDefault(g => g.Match(t, edits));
            if (gate is not null) _armed.Remove(gate);
            call = new DeriveCall(_calls.Count, t, [.. edits], flags, revision, Hold);
            _calls.Add(call);
            Slot(call.Index).TrySetResult(call);
        }
        try
        {
            if (gate is not null)
            {
                gate.EnteredTcs.TrySetResult();
                gate.ReleaseTcs.Task.Wait(CancellationToken.None);
            }
            if (call.Held) call.Gate.Wait(ct);
            ct.ThrowIfCancellationRequested();
            var plan = inner.Derive(b, t, edits, flags, revision, ct);
            call.Completed = true;
            return plan;
        }
        catch (OperationCanceledException)
        {
            call.Cancelled = true;
            throw;
        }
    }

    private TaskCompletionSource<DeriveCall> Slot(int index)
    {
        while (_started.Count <= index) _started.Add(new(TaskCreationOptions.RunContinuationsAsynchronously));
        return _started[index];
    }
}
