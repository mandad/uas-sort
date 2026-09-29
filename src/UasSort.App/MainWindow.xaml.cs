// src/UasSort.App/MainWindow.xaml.cs — Ref §9.2 chrome; FirstFrameMs, ShowOffScreen and BringToFront are Part 01's
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace UasSort.App;

public sealed partial class MainWindow : Window
{
    public const int MinWidth = 1100, MinHeight = 700;
    private readonly LaunchOptions _options;
    private readonly TaskCompletionSource<double> _firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _sized;

    internal MainWindow(LaunchOptions options, string singleInstanceMechanism, SelfTestSandbox? sandbox)
    {
        _options = options;
        InitializeComponent();
        Title = "uas-sort";
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        AppWindow.SetIcon(Path.Join(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        Services = CompositionRoot.Build(DispatcherQueue, () => RootFrame.XamlRoot, sandbox);
        Services.Platform.Log.Info("start; single instance: " + singleInstanceMechanism);
        Thumb.Cache = Services.Thumbnails;
        CompositionTarget.Rendering += OnFirstRendering;
        RootFrame.Loaded += (_, _) => { ApplyMinimumSize(); RootFrame.XamlRoot.Changed += (_, _) => ApplyMinimumSize(); };
        RootFrame.Navigate(typeof(ShellPage), this);
        Closed += (_, _) => Services.Sandbox?.Dispose();
    }

    public AppServices Services { get; }
    public ShellPage? Shell => RootFrame.Content as ShellPage;
    public nint Hwnd => Win32Interop.GetWindowFromWindowId(AppWindow.Id);
    public DeviceChangeWatcher? DeviceWatcher { get; private set; }

    /// <summary>Milliseconds from process start to the first CompositionTarget.Rendering (Ref §13 firstFrameMs).</summary>
    internal Task<double> FirstFrameMs => _firstFrame.Task;

    public void Start()
    {
        var watcher = new DeviceChangeWatcher(Hwnd, DispatcherQueue, Services.Shell);
        DeviceWatcher = watcher;
        Closed += (_, _) => watcher.Dispose();
        if (_options.SelfTest)
        {
            ShowOffScreen();
            _ = SelfTestRunner.RunAsync(new SelfTestContext(this, _options));
        }
        else
        {
            Activate();
        }
        _ = Services.Shell.StartAsync();
    }

    /// <summary>--selftest launches off-screen and never activates the window (Ref §13 Isolation).</summary>
    internal void ShowOffScreen()
    {
        AppWindow.Move(new PointInt32(-6000, -6000));
        AppWindow.Show(false);
    }

    internal void BringToFront() => ForegroundWindow.BringToFront(Win32Interop.GetWindowFromWindowId(AppWindow.Id));

    /// <summary>Ref §2.7 #5: AppWindow sizes are physical pixels, so scale by the XAML rasterization scale.</summary>
    public void ApplyMinimumSize()
    {
        var scale = RootFrame.XamlRoot?.RasterizationScale ?? 1.0;
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.PreferredMinimumWidth = (int)Math.Ceiling(MinWidth * scale);
            p.PreferredMinimumHeight = (int)Math.Ceiling(MinHeight * scale);
        }
        if (!_sized)
        {
            _sized = true;
            AppWindow.Resize(new SizeInt32((int)Math.Ceiling(1280 * scale), (int)Math.Ceiling(800 * scale)));
        }
    }

    private void OnFirstRendering(object? sender, object e)
    {
        CompositionTarget.Rendering -= OnFirstRendering;
        _firstFrame.TrySetResult((TimeProvider.System.GetUtcNow().UtcDateTime - _options.ProcessStartUtc).TotalMilliseconds);
    }
}
