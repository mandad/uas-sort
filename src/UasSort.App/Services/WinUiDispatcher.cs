// src/UasSort.App/Services/WinUiDispatcher.cs
using Microsoft.UI.Dispatching;

namespace UasSort.App.Services;

/// <summary>The VMs' UI marshalling (ReviewVm.OnPlanArrived, UiProgress, Copy/Cleanup progress). A raw DispatcherQueue callback that
/// throws would end the process without reaching App.UnhandledException, so each one runs through Observed with the App reporter
/// (Ref §12 "log, dialog, keep running").</summary>
public sealed class WinUiDispatcher(DispatcherQueue queue, Action<Exception> onFault) : IUiDispatcher
{
    public bool HasThreadAccess => queue.HasThreadAccess;
    public void Post(Action a)
    {
        if (!queue.TryEnqueue(() => Observed.Run(a, onFault))) throw new InvalidOperationException("The UI dispatcher is shut down.");
    }
}
