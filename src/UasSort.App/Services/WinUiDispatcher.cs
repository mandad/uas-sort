// src/UasSort.App/Services/WinUiDispatcher.cs
using Microsoft.UI.Dispatching;

namespace UasSort.App.Services;

public sealed class WinUiDispatcher(DispatcherQueue queue) : IUiDispatcher
{
    public bool HasThreadAccess => queue.HasThreadAccess;
    public void Post(Action a)
    {
        if (!queue.TryEnqueue(() => a())) throw new InvalidOperationException("The UI dispatcher is shut down.");
    }
}
