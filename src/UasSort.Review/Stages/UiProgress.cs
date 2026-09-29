// src/UasSort.Review/Stages/UiProgress.cs
namespace UasSort.Review;

/// <summary>IProgress that always lands on the UI thread through IUiDispatcher (never a captured SynchronizationContext).</summary>
public sealed class UiProgress<T>(IUiDispatcher ui, Action<T> onReport) : IProgress<T>
{
    public void Report(T value) => ui.Post(() => onReport(value));
}
