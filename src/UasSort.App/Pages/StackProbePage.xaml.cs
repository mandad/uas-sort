using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Windows.Storage.Pickers;

namespace UasSort.App.Pages;

public sealed partial class StackProbePage : Page
{
    public const string VirtualHost = "map.uas-sort.example";
    public const string ProbeUrl = "https://map.uas-sort.example/probe.html";

    private readonly TaskCompletionSource<string> _mapRoundTrip = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string _runtimeVersion = "";

    public StackProbePage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public StackProbeVm Vm { get; } = new();

    /// <summary>Completes when the page under the virtual host sent `ready` and answered ping 1 with pong 1.</summary>
    public Task<string> MapRoundTrip => _mapRoundTrip.Task;

    internal string? RootCardHeader => RootCard.Header as string;

    internal void CloseWebView() => Web.Close();

    internal static bool IsVirtualHost(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var u)
        && u.Scheme == Uri.UriSchemeHttps
        && string.Equals(u.Host, VirtualHost, StringComparison.OrdinalIgnoreCase);

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await InitWebViewAsync();
        }
#pragma warning disable CA1031 // any WebView2 start-up failure is reported, never thrown from an async void handler
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Vm.Status = "WebView2 failed: " + ex.Message;
            _mapRoundTrip.TrySetException(ex);
        }
    }

    private async Task InitWebViewAsync()
    {
        // Explicit user data folder, never next to the exe (Ref §2.7 #6, §9.6).
        var env = await CoreWebView2Environment.CreateWithOptionsAsync("", App.WebView2Folder, new CoreWebView2EnvironmentOptions());
        _runtimeVersion = env.BrowserVersionString;
        await Web.EnsureCoreWebView2Async(env);
        var core = Web.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDevToolsEnabled = Debugger.IsAttached;
        core.SetVirtualHostNameToFolderMapping(
            VirtualHost, Path.Join(AppContext.BaseDirectory, "MapAssets"), CoreWebView2HostResourceAccessKind.DenyCors);

        // Registered BEFORE Navigate: `ready` can arrive before NavigationCompleted (Ref §2.7 #6).
        core.WebMessageReceived += OnWebMessage;
        core.DownloadStarting += (_, a) => a.Cancel = true;
        core.NavigationStarting += (_, a) => a.Cancel = !IsVirtualHost(a.Uri);
        core.NewWindowRequested += (_, a) => a.Handled = true;
        core.NavigationCompleted += (_, a) =>
        {
            if (!a.IsSuccess)
            {
                _mapRoundTrip.TrySetException(new InvalidOperationException("navigation failed: " + a.WebErrorStatus));
            }
        };
        core.Navigate(ProbeUrl);
    }

    private void OnWebMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        using var doc = JsonDocument.Parse(args.WebMessageAsJson);
        var root = doc.RootElement;
        var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
        switch (type)
        {
            case "ready":
                Vm.Status = "Virtual host ready";
                sender.PostWebMessageAsJson("""{"v":1,"type":"ping","n":1}""");
                break;
            case "pong" when root.TryGetProperty("n", out var n) && n.GetInt32() == 1:
                Vm.Status = "Virtual host ready · pong 1";
                _mapRoundTrip.TrySetResult($"ready and pong from {ProbeUrl}; WebView2 runtime {_runtimeVersion}");
                break;
            case "error":
                var message = root.TryGetProperty("message", out var m) ? m.GetString() : null;
                Vm.Status = "Page error: " + message;
                _mapRoundTrip.TrySetException(new InvalidOperationException("page error: " + message));
                break;
        }
    }

    private async void OnBrowse(object sender, RoutedEventArgs e)
    {
        try
        {
            // Owner window from AppWindow.Id (Windows App SDK 2.x pickers take a WindowId).
            var picker = new FolderPicker(App.MainWindow!.AppWindow.Id) { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
            var result = await picker.PickSingleFolderAsync();
            if (result is not null)
            {
                Vm.FolderText = result.Path;
            }
        }
        catch (COMException ex)
        {
            Vm.Status = "Folder picker failed: " + ex.Message;
        }
    }
}
