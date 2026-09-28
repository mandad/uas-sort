using System.Diagnostics;
using Microsoft.UI.Xaml;
using UasSort.App.SelfTest;
using UasSort.Platform.Stores;

namespace UasSort.App;

public partial class App : Application
{
    private readonly LaunchOptions _options;
    private readonly SingleInstanceGate? _gate;
    private readonly SelfTestSandbox? _sandbox;

    internal App(LaunchOptions options, SingleInstanceGate? gate)
    {
        _options = options;
        _gate = gate;
        _sandbox = options.SelfTest ? SelfTestSandbox.Create(Path.GetTempPath()) : null;
        WebView2Folder = _sandbox?.WebView2Folder
            ?? Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "uas-sort", "WebView2");
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    internal static MainWindow? MainWindow { get; private set; }

    internal static string WebView2Folder { get; private set; } = "";

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new MainWindow(_options.ProcessStartUtc, _gate?.Mechanism ?? "skipped (selftest)");
        MainWindow = window;
        if (_gate?.Key is { } key)
        {
            key.Activated += (_, _) => window.DispatcherQueue.TryEnqueue(window.BringToFront);
        }

        if (_options.SelfTest)
        {
            window.ShowOffScreen();
            _ = MinimalSelfTest.RunAndExitAsync(window, _options, _sandbox!);
        }
        else
        {
            window.Activate();
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Trace.WriteLine("UNHANDLED " + e.Exception);
        if (_options.SelfTest)
        {
            e.Handled = true;
            MinimalSelfTest.FailAndExit(_options, _sandbox, e.Exception);
        }
    }
}
