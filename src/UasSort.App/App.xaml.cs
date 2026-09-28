using System.Diagnostics;
using Microsoft.UI.Xaml;

namespace UasSort.App;

public partial class App : Application
{
    private readonly LaunchOptions _options;
    private readonly SingleInstanceGate? _gate;

    internal App(LaunchOptions options, SingleInstanceGate? gate)
    {
        _options = options;
        _gate = gate;
        WebView2Folder = Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "uas-sort", "WebView2");
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

        window.Activate();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e) =>
        Trace.WriteLine("UNHANDLED " + e.Exception);
}
