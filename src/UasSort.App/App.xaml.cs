// src/UasSort.App/App.xaml.cs — replaces Part 01's; the constructor signature App(LaunchOptions, SingleInstanceGate?) is Part 01's
using System.Diagnostics;
using Microsoft.UI.Xaml;

namespace UasSort.App;

public partial class App : Application
{
    private readonly LaunchOptions _options;
    private readonly SingleInstanceGate? _gate;
    private readonly SelfTestSandbox? _sandbox;
    private MainWindow? _window;

    internal App(LaunchOptions options, SingleInstanceGate? gate)
    {
        _options = options;
        _gate = gate;
        _sandbox = options.SelfTest ? SelfTestSandbox.Create() : null;   // %TEMP%\uas-sort-selftest-<guid>\ with its four roots
        WebView2Folder = _sandbox?.WebView2Folder ?? Path.Join(KnownFolders.AppDataDir(), "WebView2");
        InitializeComponent();
        UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            _window?.Services.Platform.Log.Error("unobserved task exception", e.Exception);
            e.SetObserved();
        };
    }

    internal static MainWindow? MainWindow { get; private set; }

    internal static string WebView2Folder { get; private set; } = "";

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new MainWindow(_options, _gate?.Mechanism ?? "skipped (selftest)", _sandbox);
        MainWindow = window;
        _window = window;
        if (_gate is not null)
        {
            _gate.Activated += () => window.DispatcherQueue.TryEnqueue(window.BringToFront);
        }

        window.Start();
    }

    // Ref §12: log, keep running, tell the user; drafts survive and copied files are in the ledger.
    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Trace.WriteLine("UNHANDLED " + e.Exception);
        _window?.Services.Platform.Log.Error("unhandled exception", e.Exception);
        if (_options.SelfTest || _window is null) return;           // the selftest records it as a failed check instead
        e.Handled = true;
        _ = _window.Services.Dialogs.ShowAsync(new DialogRequest(
            "Something went wrong",
            "uas-sort hit an unexpected error: " + e.Message + "\n\nYour edits are kept as a draft, and every file this run " +
            "copied is recorded in the history. Restart uas-sort to continue.",
            "OK", null, "Close"));
    }
}
