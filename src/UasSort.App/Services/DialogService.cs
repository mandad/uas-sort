// src/UasSort.App/Services/DialogService.cs — Ref §2.7 #11: only one ContentDialog may be open, so dialogs queue
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace UasSort.App.Services;

#pragma warning disable CA1001 // app-lifetime singleton; the semaphore never allocates a wait handle (AvailableWaitHandle is unused)
public sealed class DialogService(Func<XamlRoot?> xamlRoot, DispatcherQueue ui) : IDialogService
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public int Shown { get; private set; }

    public Task<DialogResult> ShowAsync(DialogRequest r)
    {
        if (ui.HasThreadAccess) return ShowOnUiAsync(r);
        var tcs = new TaskCompletionSource<DialogResult>();
        ui.TryEnqueue(async () =>
        {
            try { tcs.SetResult(await ShowOnUiAsync(r)); }
#pragma warning disable CA1031 // handed to the awaiting caller through the task, not swallowed
            catch (Exception ex) { tcs.SetException(ex); }
#pragma warning restore CA1031
        });
        return tcs.Task;
    }

    private async Task<DialogResult> ShowOnUiAsync(DialogRequest r)
    {
        await _gate.WaitAsync();
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = xamlRoot(),
                Title = r.Title,
                Content = new TextBlock { Text = r.Body, TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = r.Primary,
                SecondaryButtonText = r.Secondary ?? "",
                CloseButtonText = r.Close,
                DefaultButton = ContentDialogButton.Close,
            };
            Shown++;
            var result = await dialog.ShowAsync();
            return result switch
            {
                ContentDialogResult.Primary => DialogResult.Primary,
                ContentDialogResult.Secondary => DialogResult.Secondary,
                _ => DialogResult.Close,
            };
        }
        finally { _gate.Release(); }
    }
}
#pragma warning restore CA1001
