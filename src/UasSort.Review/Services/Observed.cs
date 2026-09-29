// src/UasSort.Review/Services/Observed.cs
namespace UasSort.Review;

/// <summary>
/// Ref §12 "log, keep running": the one way a fire-and-forget task or a raw UI callback is started. A fault goes to the caller's
/// handler, which logs it and shows it on an existing error surface (the Card stage message, the Review error InfoBar, the
/// unhandled-exception dialog), instead of vanishing into TaskScheduler.UnobservedTaskException or, for a raw DispatcherQueue
/// callback or timer tick, ending the process without a log line.
/// </summary>
public static class Observed
{
    /// <summary>Awaits <paramref name="task"/> on the caller's context; a fault (never a cancellation) goes to <paramref name="onFault"/>.</summary>
    public static async void Forget(Task task, Action<Exception> onFault)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(onFault);
#pragma warning disable CA1031 // the point of this helper: every fault of a fire-and-forget task is reported, never lost
        try
        {
            await task.ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            onFault(e);
        }
#pragma warning restore CA1031
    }

    /// <summary>Runs a raw callback (a DispatcherQueue.TryEnqueue lambda, a timer tick); an exception goes to <paramref name="onFault"/>.</summary>
    public static void Run(Action callback, Action<Exception> onFault)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(onFault);
#pragma warning disable CA1031 // a raw dispatcher callback or timer tick has no caller to rethrow to: report it and keep running
        try
        {
            callback();
        }
        catch (Exception e)
        {
            onFault(e);
        }
#pragma warning restore CA1031
    }
}
