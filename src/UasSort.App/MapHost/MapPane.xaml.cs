// src/UasSort.App/MapHost/MapPane.xaml.cs — Ref §9.6 Host (WebView2 fenced to the virtual host)
using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace UasSort.App.MapHost;

public sealed partial class MapPane : UserControl
{
    public const string Host = "map.uas-sort.example";
    public static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(5);
    private const string Origin = "https://" + Host + "/";

    private Task? _init;
    private AppServices? _services;
    private CoreWebView2Environment? _env;
    private DispatcherQueueTimer? _readyTimer;

    public MapPane() => InitializeComponent();

    public bool IsReady { get; private set; }
    public bool IsUnavailable { get; private set; }
    public bool IsNavigated { get; private set; }
    public Func<Uri?>? OpenInBrowserUri { get; set; }
    public CoreWebView2? Core => MapView.CoreWebView2;

    public event Action<string>? MessageReceived;
    public event Action? Ready;

    public Task InitializeAsync(AppServices services) => _init ??= InitCoreAsync(services);

    private async Task InitCoreAsync(AppServices services)
    {
        _services = services;
        _env = await CoreWebView2Environment.CreateWithOptionsAsync(null, services.WebView2DataDir, new CoreWebView2EnvironmentOptions());
        await MapView.EnsureCoreWebView2Async(_env);
        var core = MapView.CoreWebView2;
        var s = core.Settings;
        s.AreDefaultContextMenusEnabled = false;          // right-click goes to JS, then a WinUI MenuFlyout
        s.AreBrowserAcceleratorKeysEnabled = false;
        s.IsZoomControlEnabled = false;
        s.IsStatusBarEnabled = false;
        s.IsGeneralAutofillEnabled = false;
        s.IsPasswordAutosaveEnabled = false;
        s.AreDevToolsEnabled = Debugger.IsAttached;
        s.UserAgent = s.UserAgent + " uas-sort/" + (typeof(MapPane).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");
        core.Profile.PreferredColorScheme = ActualTheme == ElementTheme.Dark
            ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
        core.SetVirtualHostNameToFolderMapping(Host, Path.Combine(AppContext.BaseDirectory, "MapAssets"),
                                               CoreWebView2HostResourceAccessKind.DenyCors);
        core.NavigationStarting += (_, e) => { if (!e.Uri.StartsWith(Origin, StringComparison.Ordinal)) e.Cancel = true; };
        core.NavigationCompleted += (_, e) => IsNavigated = e.IsSuccess;
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;                                // never a second browser window
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps) services.Platform.Shell.OpenHttps(u);
        };
        core.DownloadStarting += (_, e) => { e.Cancel = true; e.Handled = true; };
        core.WebMessageReceived += OnWebMessageReceived;     // registered before Navigate (Ref §2.7 #6)
        StartReadyTimer();
        core.Navigate(Origin + "index.html");
    }

    private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var json = e.WebMessageAsJson;
        string? type = null;
        bool webgl2 = true;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("type", out var t)) type = t.GetString();
            if (type == "ready" && doc.RootElement.TryGetProperty("webgl2", out var w)) webgl2 = w.ValueKind == JsonValueKind.True;
        }
        catch (JsonException) { _services?.Platform.Log.Warn("map: unparseable message " + json); }

        if (type is "error" or "tileError" or "baseUnavailable") _services?.Platform.Log.Warn("map: " + json);
        if (type == "ready")
        {
            if (!webgl2) ShowUnavailable("This PC can't run the map (no WebGL 2).");
            else { IsReady = true; HideUnavailable(); Ready?.Invoke(); }
        }
        MessageReceived?.Invoke(json);
    }

    private void StartReadyTimer()
    {
        _readyTimer = DispatcherQueue.CreateTimer();
        _readyTimer.Interval = ReadyTimeout;
        _readyTimer.IsRepeating = false;
        _readyTimer.Tick += (_, _) => { if (!IsReady) ShowUnavailable("The map didn't start within 5 seconds."); };
        _readyTimer.Start();
    }

    /// <summary>Posts one host→map message; callable from any thread (MapBridge's 10/s throttle posts from a timer thread).</summary>
    public void PostJson(string json)
    {
        if (DispatcherQueue.HasThreadAccess) MapView.CoreWebView2?.PostWebMessageAsJson(json);
        else DispatcherQueue.TryEnqueue(() => MapView.CoreWebView2?.PostWebMessageAsJson(json));
    }

    /// <summary>Evaluates a JS expression (awaiting a promise) through the DevTools protocol; returns the string value or null.</summary>
    public async Task<string?> EvaluateAsync(string expression)
    {
        if (MapView.CoreWebView2 is not { } core) return null;
        var request = JsonSerializer.Serialize(new EvalRequest(expression, true, true), MapPaneJson.Default.EvalRequest);
        var reply = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", request);
        using var doc = JsonDocument.Parse(reply);
        if (!doc.RootElement.TryGetProperty("result", out var r) || !r.TryGetProperty("value", out var v)) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText();
    }

    public void ShowUnavailable(string reason)
    {
        IsUnavailable = true;
        UnavailableReason.Text = reason;
        UnavailablePanel.Visibility = Visibility.Visible;
        MapView.Visibility = Visibility.Collapsed;
    }

    private void HideUnavailable()
    {
        IsUnavailable = false;
        UnavailablePanel.Visibility = Visibility.Collapsed;
        MapView.Visibility = Visibility.Visible;
    }

    private void OnOpenInBrowser(object sender, RoutedEventArgs e)
    {
        var uri = OpenInBrowserUri?.Invoke() ?? new Uri("https://www.openstreetmap.org/");
        _services?.Platform.Shell.OpenHttps(uri);
    }

    public void Close()
    {
        _readyTimer?.Stop();
        MapView.Close();
    }
}

public sealed record EvalRequest(string Expression, bool AwaitPromise, bool ReturnByValue);

[System.Text.Json.Serialization.JsonSourceGenerationOptions(PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase)]
[System.Text.Json.Serialization.JsonSerializable(typeof(EvalRequest))]
public partial class MapPaneJson : System.Text.Json.Serialization.JsonSerializerContext;
