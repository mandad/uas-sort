// tests/UasSort.Testing/GatedPlanDeriver.cs
namespace UasSort.Testing;

/// <summary>Blocks matching Derive calls until released: proves a slow earlier derive never overwrites a later one (Ref §13).</summary>
public sealed class GatedPlanDeriver(IPlanDeriver inner) : IPlanDeriver
{
    private readonly List<Gate> _armed = [];
    private readonly Lock _lock = new();
    private int _callCount;

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

    public Plan Derive(PlanBase b, Tuning t, IReadOnlyList<PlanEdit> edits, SessionFlags flags, int revision, CancellationToken ct)
    {
        Interlocked.Increment(ref _callCount);
        Gate? gate;
        lock (_lock)
        {
            gate = _armed.FirstOrDefault(g => g.Match(t, edits));
            if (gate is not null) _armed.Remove(gate);
        }
        if (gate is not null)
        {
            gate.EnteredTcs.TrySetResult();
            gate.ReleaseTcs.Task.Wait(CancellationToken.None);
        }
        return inner.Derive(b, t, edits, flags, revision, ct);
    }
}
