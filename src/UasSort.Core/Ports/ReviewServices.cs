namespace UasSort.Core;

public interface IUiDispatcher { bool HasThreadAccess { get; } void Post(Action a); }
public enum DialogResult { Primary, Secondary, Close }
public sealed record DialogRequest(string Title, string Body, string Primary, string? Secondary, string Close);
public interface IDialogService { Task<DialogResult> ShowAsync(DialogRequest r); }   // queued: one ContentDialog open at a time
