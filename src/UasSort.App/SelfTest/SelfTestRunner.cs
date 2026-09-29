// src/UasSort.App/SelfTest/SelfTestRunner.cs — Ref §13 UI smoke test: exits 0/1, writes the result JSON, 60 s budget.
// The records SelfTestCheck/SelfTestResult/SelfTestJsonContext are Part 01's (src/UasSort.App/SelfTest/SelfTestResult.cs).
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
    private static int _finished;

    public static async Task RunAsync(SelfTestContext ctx)
    {
        var checks = new List<SelfTestCheck>();
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
        CloseWebViews(ctx, checks);
        lock (checks) Finish(ctx, checks);
    }

    /// <summary>On the UI thread, before exit: closes the map WebView2s so the run log has no Chromium teardown line.
    /// The watchdog path (a timer thread) skips this; a timed-out run may still print the line.</summary>
    private static void CloseWebViews(SelfTestContext ctx, List<SelfTestCheck> checks)
    {
        try { SelfTestChecks.CloseMapPanes(ctx); }
#pragma warning disable CA1031 // selftest: a close failure is recorded, and the result file is still written
        catch (Exception ex) { lock (checks) checks.Add(SelfTestCheck.Fail("closeWebViews", ex.GetType().Name + ": " + ex.Message)); }
#pragma warning restore CA1031
    }

    private static void Finish(SelfTestContext ctx, List<SelfTestCheck> checks)
    {
        if (Interlocked.Exchange(ref _finished, 1) != 0) return;         // the watchdog and the normal end never both write
        bool ok = checks.Count > 0 && checks.TrueForAll(c => c.Status != "fail");
        var json = JsonSerializer.SerializeToUtf8Bytes(new SelfTestResult(ok, Math.Round(ctx.FirstFrameMs), checks.ToArray()),
                                                       SelfTestJsonContext.Default.SelfTestResult);
        try { SelfTestSandbox.WriteResult(ctx.Options.ResultPath, json); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { ok = false; }
        ctx.Services.Sandbox?.Dispose();
        Environment.Exit(ok ? 0 : 1);
    }
}
