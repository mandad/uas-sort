using System.Collections.Concurrent;

namespace UasSort.Platform.Io;

/// <summary>One dedicated STA thread running work items in order (IFileOperation is STA-only). Invoke blocks its caller.</summary>
internal sealed class StaWorker : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

    public StaWorker(string name)
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = name };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public T Invoke<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() =>
        {
#pragma warning disable CA1031 // every failure of the work item is handed back to the calling thread
            try { done.SetResult(work()); }
            catch (Exception e) { done.SetException(e); }
#pragma warning restore CA1031
        });
        return done.Task.GetAwaiter().GetResult();
    }

    private void Loop()
    {
        foreach (var work in _queue.GetConsumingEnumerable()) work();
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _thread.Join();
        _queue.Dispose();
    }
}
