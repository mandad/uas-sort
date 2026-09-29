// tests/UasSort.Testing/Disposer.cs
namespace UasSort.Testing;

/// <summary>Runs an action once on Dispose (defined here; Part 10's cleanup VM tests use it).</summary>
public sealed class Disposer(Action onDispose) : IDisposable
{
    private int _done;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _done, 1) == 0) onDispose();
    }
}
