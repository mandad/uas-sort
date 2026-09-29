// src/UasSort.App/SelfTest/SelfTestRunner.cs — Ref §13 UI smoke test: exits 0/1, writes the result JSON, 60 s budget.
// The records SelfTestCheck/SelfTestResult/SelfTestJsonContext are Part 01's (src/UasSort.App/SelfTest/SelfTestResult.cs).
using System.Diagnostics;
using System.Text.Json;

namespace UasSort.App.SelfTest;

internal sealed class SelfTestContext(MainWindow window, LaunchOptions options)
{
    public MainWindow Window { get; } = window;
    public LaunchOptions Options { get; } = options;
    public double FirstFrameMs { get; set; } = -1;
    public AppServices Services => Window.Services;
    public SelfTestSandbox Sandbox => Window.Services.Sandbox!;
    public Dictionary<string, object> Shared { get; } = [];              // state handed from one check to the next
}

internal static class SelfTestRunner
{
    public static readonly TimeSpan PerCheck = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan Watchdog = TimeSpan.FromSeconds(55);  // deploy.ps1 / selftest.ps1 kill at 60 s
    private static readonly TimeSpan BrowserExit = TimeSpan.FromSeconds(5);
    private static int _finished;
    private static IDisposable? _cardPause;                               // held until exit (ReleaseCard)
    private static SelfTestContext? _ctx;                                 // the running selftest, for FailUnhandled
    private static List<SelfTestCheck>? _checks;

    public static async Task RunAsync(SelfTestContext ctx)
    {
        var checks = new List<SelfTestCheck>();
        _checks = checks;
        _ctx = ctx;
        using var watchdog = new Timer(_ =>
        {
            lock (checks) { checks.Add(SelfTestCheck.Fail("watchdog", "selftest did not finish within 55 s")); Finish(ctx, checks); }
        }, null, Watchdog, Timeout.InfiniteTimeSpan);

        try { ctx.FirstFrameMs = await ctx.Window.FirstFrameMs.WaitAsync(PerCheck); }
        catch (TimeoutException) { lock (checks) checks.Add(SelfTestCheck.Fail("firstFrame", "no frame rendered within 20 s")); }

        var wanted = SelfTestChecks.All
            .Where(c => ctx.Options.Only is null ? !c.Name.StartsWith("debug.", StringComparison.Ordinal) : ctx.Options.Only.Contains(c.Name))
            .ToList();
        if (ctx.Options.Only is not null)
            foreach (var name in ctx.Options.Only.Where(n => SelfTestChecks.All.All(c => c.Name != n)))
                lock (checks) checks.Add(SelfTestCheck.Fail(name, "unknown check"));

        foreach (var (name, run) in wanted)
        {
            SelfTestCheck result;
            try
            {
                result = await run(ctx).WaitAsync(PerCheck);
            }
            catch (TimeoutException) { result = SelfTestCheck.Fail(name, $"timed out after {PerCheck.TotalSeconds:0} s"); }
#pragma warning disable CA1031 // selftest: every failure becomes a failed check in the result file
            catch (Exception ex) { result = SelfTestCheck.Fail(name, ex.GetType().Name + ": " + ex.Message); }
#pragma warning restore CA1031
            lock (checks) checks.Add(result);
        }
        ReleaseCard(ctx, checks);
        await CloseWebViewsAsync(ctx, checks);
        lock (checks) Finish(ctx, checks);
    }

    /// <summary>Closes the card file the thumbnail reader keeps open, so Finish can delete the sandbox. The pause is never
    /// resumed: the process exits next. The watchdog path skips this too.</summary>
    private static void ReleaseCard(SelfTestContext ctx, List<SelfTestCheck> checks)
    {
        try { _cardPause = ctx.Services.Thumbnails.PauseSource(); }
#pragma warning disable CA1031 // selftest: a release failure is recorded, and the result file is still written
        catch (Exception ex) { lock (checks) checks.Add(SelfTestCheck.Fail("releaseCard", ex.GetType().Name + ": " + ex.Message)); }
#pragma warning restore CA1031
    }

    /// <summary>On the UI thread, before exit: closes the map WebView2s so the run log has no Chromium teardown line, then
    /// waits (up to 5 s, the UI thread still pumping) for their browser process to exit, so it no longer holds the sandbox's
    /// WebView2 folder when Finish deletes it. The watchdog path (a timer thread) skips this; a timed-out run may still
    /// print the line and leave the folder.</summary>
    private static async Task CloseWebViewsAsync(SelfTestContext ctx, List<SelfTestCheck> checks)
    {
        var browsers = new List<Process>();
        try
        {
            List<MapPane> panes = ctx.Window.Shell is { } shell ? [.. VisualTree.FindAll<MapPane>(shell)] : [];
            if (ctx.Shared.TryGetValue("mapPane", out var standalone)) panes.Add((MapPane)standalone);
            // every live pane, also the cached Review page's that is not in the visual tree now (F19)
            var pids = panes.Select(p => p.Core?.BrowserProcessId).OfType<uint>().Concat(MapPane.LiveBrowserProcessIds()).Distinct();
            foreach (var pid in pids)
            {
                try { browsers.Add(Process.GetProcessById((int)pid)); }
                catch (ArgumentException) { }                             // already exited
            }
            SelfTestChecks.CloseMapPanes(ctx);
            using var timeout = new CancellationTokenSource(BrowserExit);
            foreach (var browser in browsers) await browser.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) { }                            // still running: Finish's delete retries for 3 s
#pragma warning disable CA1031 // selftest: a close failure is recorded, and the result file is still written
        catch (Exception ex) { lock (checks) checks.Add(SelfTestCheck.Fail("closeWebViews", ex.GetType().Name + ": " + ex.Message)); }
#pragma warning restore CA1031
        finally
        {
            foreach (var browser in browsers) browser.Dispose();
        }
    }

    /// <summary>App.UnhandledException under --selftest (Ref §13 Result): adds an "unhandled" failed check and finishes (result
    /// file, sandbox deleted, exit 1). An exception before RunAsync started (MainWindow ctor, OnLaunched) writes a result
    /// holding only that check.</summary>
    internal static void FailUnhandled(LaunchOptions options, SelfTestSandbox? sandbox, Exception ex)
    {
        var failed = SelfTestCheck.Fail("unhandled", ex.GetType().Name + ": " + ex.Message);
        if (_ctx is { } ctx && _checks is { } checks)
        {
            lock (checks)
            {
                checks.Add(failed);
                Finish(ctx, checks);
            }
            return;
        }
        if (Interlocked.Exchange(ref _finished, 1) != 0) return;
        var json = JsonSerializer.SerializeToUtf8Bytes(new SelfTestResult(false, -1, (SelfTestCheck[])[failed]),   // explicit array: CsWinRT1032
                                                   SelfTestJsonContext.Default.SelfTestResult);
        try { SelfTestSandbox.WriteResult(options.ResultPath, json); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
        sandbox?.Dispose();
        Environment.Exit(1);
    }

    private static void Finish(SelfTestContext ctx, List<SelfTestCheck> checks)
    {
        if (Interlocked.Exchange(ref _finished, 1) != 0) return;         // the watchdog and the normal end never both write
        // F19: the sandbox (with the WebView2 user-data folder) goes before the result is written, only once no msedgewebview2.exe
        // of this run is alive; the check records that nothing was left behind (never a folder pulled from under Edge).
        if (ctx.Services.Sandbox is { } sandbox)
            checks.Add(sandbox.TryDelete()
                ? SelfTestCheck.Pass("sandbox.cleanup", "no WebView2 process of this run left; sandbox deleted")
                : SelfTestCheck.Fail("sandbox.cleanup", sandbox.CleanupProblem ?? "the sandbox was left in place"));
        bool ok = checks.Count > 0 && checks.TrueForAll(c => c.Status != "fail");
        var json = JsonSerializer.SerializeToUtf8Bytes(new SelfTestResult(ok, Math.Round(ctx.FirstFrameMs), checks.ToArray()),
                                                       SelfTestJsonContext.Default.SelfTestResult);
        try { SelfTestSandbox.WriteResult(ctx.Options.ResultPath, json); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { ok = false; }
        Environment.Exit(ok ? 0 : 1);
    }
}
