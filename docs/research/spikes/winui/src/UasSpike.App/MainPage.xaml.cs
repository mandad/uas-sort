using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using UasSpike.App.ViewModels;
using UasSpike.Core;

namespace UasSpike.App;

public sealed partial class MainPage : Page
{
    private const string VirtualHost = "uas-sort.example"; // .example TLD: avoids the mDNS delay of *.local
    public MainPageViewModel ViewModel { get; } = new();
    private bool _selfTestDone;

    public MainPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        ViewModel.RadiusChanged += miles => PostJson($$"""{"type":"radius","miles":{{miles.ToString(CultureInfo.InvariantCulture)}}}""");
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            SpikeLog.Write("MainPage.Loaded");
            var zb = new GeoPoint(57.5504, -153.7390);
            var kd = new GeoPoint(57.7996, -152.3902);
            SpikeLog.Write($"Core: ZacharBay->Kodiak = {zb.MilesTo(kd):F2} mi");

            // Explicit user-data folder (real app: %LOCALAPPDATA%\uas-sort\WebView2).
            var udf = Path.Combine(AppContext.BaseDirectory, "WebView2Data");
            var env = await CoreWebView2Environment.CreateWithOptionsAsync("", udf, new CoreWebView2EnvironmentOptions());
            SpikeLog.Write($"WebView2 runtime {env.BrowserVersionString}, udf={udf}");
            await MapView.EnsureCoreWebView2Async(env);
            var core = MapView.CoreWebView2;

            core.SetVirtualHostNameToFolderMapping(VirtualHost,
                Path.Combine(AppContext.BaseDirectory, "Web"), CoreWebView2HostResourceAccessKind.DenyCors);
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.WebMessageReceived += OnWebMessage;
            core.NavigationCompleted += OnNavigationCompleted;
            core.Navigate($"https://{VirtualHost}/map.html");
        }
        catch (Exception ex)
        {
            SpikeLog.Write("ERROR init: " + ex);
            ViewModel.Status = "WebView2 init failed: " + ex.Message;
        }
    }

    private void OnWebMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        var json = args.WebMessageAsJson;
        SpikeLog.Write("web->host " + json);
        using var doc = JsonDocument.Parse(json);
        var type = doc.RootElement.GetProperty("type").GetString();
        if (type == "click")
        {
            var lat = doc.RootElement.GetProperty("lat").GetDouble();
            var lon = doc.RootElement.GetProperty("lon").GetDouble();
            var miles = new GeoPoint(lat, lon).MilesTo(new GeoPoint(57.5504, -153.7390));
            ViewModel.Status = $"Clicked {lat:F4}, {lon:F4} ({miles:F1} mi from Zachar Bay)";
            SpikeLog.Write($"CLICK lat={lat:F5} lon={lon:F5} distZB={miles:F2}mi");
        }
    }

    private async void OnNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        SpikeLog.Write($"NavigationCompleted success={args.IsSuccess} status={args.WebErrorStatus} http={args.HttpStatusCode}");
        ViewModel.Status = args.IsSuccess ? "Map loaded" : $"Navigation failed: {args.WebErrorStatus}";
        if (!args.IsSuccess || _selfTestDone) return;
        _selfTestDone = true;
        try { await SelfTestAsync(sender); }
        catch (Exception ex) { SpikeLog.Write("ERROR selftest: " + ex); }
    }

    /// <summary>Automated checks for the CLI launch: host->page message, real (CDP) mouse click, radius update, screenshot.</summary>
    private async Task SelfTestAsync(CoreWebView2 core)
    {
        core.PostWebMessageAsJson("""{"type":"ping"}""");
        await Task.Delay(1500);

        // Real input event via DevTools protocol -> Leaflet 'click' -> postMessage -> OnWebMessage.
        double x = MapView.ActualWidth * 0.30, y = MapView.ActualHeight * 0.60;
        string ev(string t) => $$"""{"type":"{{t}}","x":{{x.ToString(CultureInfo.InvariantCulture)}},"y":{{y.ToString(CultureInfo.InvariantCulture)}},"button":"left","clickCount":1}""";
        await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", ev("mousePressed"));
        await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", ev("mouseReleased"));
        SpikeLog.Write($"selftest: dispatched CDP click at ({x:F0},{y:F0})");

        await Task.Delay(500);
        ViewModel.RadiusMiles = 55; // C# -> JS via PostWebMessageAsJson
        await Task.Delay(2500);

        var png = Path.Combine(Path.GetDirectoryName(SpikeLog.Path)!, "webview-capture.png");
        using (var fs = File.Create(png))
            await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, fs.AsRandomAccessStream());
        SpikeLog.Write($"selftest: CapturePreview -> {png} ({new FileInfo(png).Length} bytes)");
        SpikeLog.Write("SELFTEST DONE");
    }

    private void PostJson(string json)
    {
        if (MapView.CoreWebView2 is { } core) core.PostWebMessageAsJson(json);
        SpikeLog.Write("host->web " + json);
    }
}
