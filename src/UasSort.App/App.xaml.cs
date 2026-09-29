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
            e.SetObserved();
            // Ref §12: log and dialog. Under --selftest only the log line: this fires at GC time on the finalizer thread, and the
            // gate must not depend on GC timing (debug.throwPosted proves the reporter itself).
            if (_options.SelfTest) _window?.Services.Platform.Log.Error("unobserved task exception", e.Exception);
            else Report(e.Exception, "unobserved task exception");
        };
    }

    /// <summary>The App-level reporter of Ref §12 "log, dialog, keep running" for faults no XAML event carries to
    /// App.UnhandledException: raw DispatcherQueue callbacks, timer ticks and observed fire-and-forget tasks (through Observed).</summary>
    internal static void ReportFault(Exception e)
    {
        if (Current is App app) app.Report(e, "unhandled exception");
        else Trace.WriteLine("UNHANDLED " + e);
    }

    /// <summary>Runs a raw UI callback (DispatcherQueue.TryEnqueue lambda, DispatcherQueueTimer.Tick) through the one helper.</summary>
    internal static void Guarded(Action callback) => Observed.Run(callback, ReportFault);

    internal static MainWindow? MainWindow { get; private set; }

    internal static string WebView2Folder { get; private set; } = "";

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new MainWindow(_options, _gate?.Mechanism ?? "skipped (selftest)", _sandbox);
        MainWindow = window;
        _window = window;
        if (_gate is not null)
        {
            _gate.Activated += () => window.DispatcherQueue.TryEnqueue(() => Guarded(window.BringToFront));
        }

        window.Start();
    }

    // Ref §12: log, keep running, tell the user; drafts survive and copied files are in the ledger.
    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        if (Report(e.Exception, "unhandled exception", e.Message)) e.Handled = true;
    }

    /// <summary>Logs, then (selftest) fails the run with an "unhandled" check, or shows the dialog listing what this run copied
    /// (Ref §12). False only before the window exists (nothing to show a dialog on).</summary>
    private bool Report(Exception ex, string what, string? message = null)
    {
        Trace.WriteLine("UNHANDLED " + ex);
        _window?.Services.Platform.Log.Error(what, ex);
        if (_options.SelfTest)
        {
            // The selftest writes an "unhandled" failed check to its result file, deletes its sandbox and exits 1.
            SelfTestRunner.FailUnhandled(_options, _sandbox, ex);
            return true;
        }
        if (_window is null) return false;
        var body = UnhandledText.Body(message ?? ex.Message, CopiedThisRun(_window.Services));
        Observed.Forget(_window.Services.Dialogs.ShowAsync(new DialogRequest(UnhandledText.Title, body, "OK", null, "Close")),
                        e => Trace.WriteLine("UNHANDLED dialog failed " + e));
        return true;
    }

    /// <summary>The files the ledger records for the run in progress (or the last one on the Verdict page); null when there is none
    /// or the ledger can't be read now (the ledger stays authoritative: the next scan shows them as imported).</summary>
    private static IReadOnlyList<string>? CopiedThisRun(AppServices services)
    {
        var shell = services.Shell;
        if (shell.RunId is not { } run) return null;
        try
        {
            return UnhandledText.CopiedThisRun(services.Platform.LedgerFor(shell.Settings.VideoRoot).Load(), run);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or UnsafeIoException)
        {
            return null;
        }
    }
}
