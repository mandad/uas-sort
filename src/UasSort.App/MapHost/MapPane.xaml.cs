// src/UasSort.App/MapHost/MapPane.xaml.cs — Ref §9.6 Host (WebView2 fenced to the virtual host)
using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;
using Windows.Networking.Connectivity;

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
            RaiseUiEvents(type, doc.RootElement.Clone());
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
        var uri = OpenInBrowserUri?.Invoke() ?? OsmUri(null);
        _services?.Platform.Shell.OpenHttps(uri);
    }

    public void Close()
    {
        Detach();
        if (_networkHooked)
        {
            NetworkInformation.NetworkStatusChanged -= OnNetworkStatusChanged;
            _networkHooked = false;
        }
        _readyTimer?.Stop();
        MapView.Close();
    }

    private MapBridge? _bridge;
    private bool _networkHooked;

    /// <summary>The raw contextMenu message as a point in this pane (CSS px = DIPs at WebView2 zoom 1; zoom control is off).
    /// The Review page shows its menu from ReviewVm.MapContextMenuRequested; this event serves hosts without a ReviewVm.</summary>
    public event Action<IReadOnlyList<string>, Point>? ContextMenuRequested;

    /// <summary>Ref §9.6: after a map click (click or clickEmpty) the host moves focus back to the clip list (UNVERIFIED with WebView2 focus).</summary>
    public event Action? FocusReturnRequested;

    /// <summary>Connectivity changed; the owner re-sends MapProjection.Init with the new online flag (Ref §9.6 offline).</summary>
    public event Action<bool>? OnlineChanged;

    public string ThemeName => ActualTheme == ElementTheme.Dark ? "dark" : "light";

    public static bool IsOnline() =>
        NetworkInformation.GetInternetConnectionProfile()?.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess;

    /// <summary>Feeds every map→host message to this bridge (MapBridge.Dispatch parses it and raises Received).</summary>
    public void Attach(MapBridge bridge)
    {
        Detach();
        _bridge = bridge;
        MessageReceived += bridge.Dispatch;
        ActualThemeChanged += OnThemeChanged;
        if (!_networkHooked)
        {
            NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged;
            _networkHooked = true;
        }
    }

    public void Detach()
    {
        if (_bridge is not { } b) return;
        MessageReceived -= b.Dispatch;
        ActualThemeChanged -= OnThemeChanged;
        _bridge = null;
    }

    /// <summary>The "Open in browser" address for a centre point (OpenStreetMap at zoom 12; world view without one).</summary>
    public static Uri OsmUri(GeoPoint? center) => center is { } c
        ? new Uri(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                                $"https://www.openstreetmap.org/?mlat={c.Lat:F5}&mlon={c.Lon:F5}#map=12/{c.Lat:F5}/{c.Lon:F5}"))
        : new Uri("https://www.openstreetmap.org/");

    private void OnThemeChanged(FrameworkElement sender, object args)
    {
        if (MapView.CoreWebView2 is { } core)
            core.Profile.PreferredColorScheme = ThemeName == "dark" ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
        _bridge?.Send(new MapSetTheme(ThemeName));
    }

    private void OnNetworkStatusChanged(object sender) =>
        DispatcherQueue.TryEnqueue(() => OnlineChanged?.Invoke(IsOnline()));

    /// <summary>UI events for the click and contextMenu messages (called from OnWebMessageReceived, on the UI thread).</summary>
    private void RaiseUiEvents(string? type, JsonElement root)
    {
        if (type is "click" or "clickEmpty") FocusReturnRequested?.Invoke();
        if (type == "contextMenu" && root.TryGetProperty("itemIds", out var ids) && root.TryGetProperty("x", out var x)
            && root.TryGetProperty("y", out var y))
        {
            var list = ids.EnumerateArray().Select(i => i.GetString() ?? "").ToList();
            ContextMenuRequested?.Invoke(list, new Point(x.GetDouble(), y.GetDouble()));
        }
    }
}

public sealed record EvalRequest(string Expression, bool AwaitPromise, bool ReturnByValue);

[System.Text.Json.Serialization.JsonSourceGenerationOptions(PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase)]
[System.Text.Json.Serialization.JsonSerializable(typeof(EvalRequest))]
public partial class MapPaneJson : System.Text.Json.Serialization.JsonSerializerContext;
