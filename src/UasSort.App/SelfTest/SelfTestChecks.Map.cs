// src/UasSort.App/SelfTest/SelfTestChecks.Map.cs
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace UasSort.App.SelfTest;

internal static partial class SelfTestChecks
{
    /// <summary>A MapPane overlaid on the shell (not the Review one), shared by the map checks, removed by the last of them.</summary>
    public static async Task<MapPane> StandaloneMapPaneAsync(SelfTestContext ctx)
    {
        if (ctx.Shared.TryGetValue("mapPane", out var existing)) return (MapPane)existing;
        var pane = new MapPane { Width = 640, Height = 420, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var messages = new List<string>();
        pane.MessageReceived += m => { lock (messages) messages.Add(m); };
        ctx.Shared["mapMessages"] = messages;
        var root = (Grid)ctx.Window.Shell!.Content;              // ShellPage's two-row grid (TitleBar, stage frame)
        Grid.SetRowSpan(pane, 2);
        root.Children.Add(pane);
        ctx.Shared["mapPane"] = pane;
        await pane.InitializeAsync(ctx.Services);
        return pane;
    }

    public static void RemoveStandaloneMapPane(SelfTestContext ctx)
    {
        if (!ctx.Shared.Remove("mapPane", out var p)) return;
        var pane = (MapPane)p;
        ((Grid)ctx.Window.Shell!.Content).Children.Remove(pane);
        pane.Close();
    }

    /// <summary>Closes every MapPane still open (the standalone one and the Review page's) before the selftest exits.
    /// Environment.Exit with a live WebView2 makes Chromium print "Failed to unregister class Chrome_WidgetWin_0.
    /// Error = 1412" (ERROR_CLASS_HAS_WINDOWS) on stderr, because its host window still exists at teardown.</summary>
    public static void CloseMapPanes(SelfTestContext ctx)
    {
        RemoveStandaloneMapPane(ctx);
        if (ctx.Window.Shell is { } shell)
            foreach (var pane in VisualTree.FindAll<MapPane>(shell)) pane.Close();
        MapPane.CloseAll();                                              // the cached Review page's pane is not in the tree (F19)
    }

    private static async Task<SelfTestCheck> MapMime(SelfTestContext ctx)
    {
        var pane = await StandaloneMapPaneAsync(ctx);
        await WaitUntilAsync(() => pane.IsNavigated, TimeSpan.FromSeconds(10));
        var answer = await pane.EvaluateAsync(
            "fetch('lib/manifest.json').then(r => r.json()).then(m => fetch('lib/' + m.entry))" +
            ".then(r => r.status + ' ' + (r.headers.get('content-type') || '(none)'))");
        bool ok = answer is not null && answer.StartsWith("200 ", StringComparison.Ordinal)
                  && (answer.Contains("text/javascript", StringComparison.OrdinalIgnoreCase)
                      || answer.Contains("application/javascript", StringComparison.OrdinalIgnoreCase));
        return ok ? SelfTestCheck.Pass("map.mime", "entry module served as " + answer)
                  : SelfTestCheck.Fail("map.mime", "entry module served as " + (answer ?? "(no answer)") + "; apply the Task 11.7 fallbacks in order");
    }

    private static async Task<SelfTestCheck> MapReady(SelfTestContext ctx)
    {
        var pane = await StandaloneMapPaneAsync(ctx);
        var messages = (List<string>)ctx.Shared["mapMessages"];
        try
        {
            if (!await WaitUntilAsync(() => pane.IsReady || pane.IsUnavailable, TimeSpan.FromSeconds(15)))
                return SelfTestCheck.Fail("map.ready", "no ready within 15 s; messages: " + string.Join(" | ", Snapshot(messages)));
            if (pane.IsUnavailable)
                return SelfTestCheck.Fail("map.ready", "map unavailable; messages: " + string.Join(" | ", Snapshot(messages)));
            pane.PostJson("""{"v":1,"type":"ping","n":1}""");
            bool pong = await WaitUntilAsync(() => Snapshot(messages).Any(IsPong1), TimeSpan.FromSeconds(5));
            var ready = Snapshot(messages).First(m => m.Contains("\"ready\"", StringComparison.Ordinal));
            return pong ? SelfTestCheck.Pass("map.ready", "ready " + ready + "; pong 1")
                        : SelfTestCheck.Fail("map.ready", "ready but no pong; messages: " + string.Join(" | ", Snapshot(messages)));
        }
        finally { RemoveStandaloneMapPane(ctx); }
    }

    private static async Task<SelfTestCheck> MapDraw(SelfTestContext ctx)
    {
        var pane = await StandaloneMapPaneAsync(ctx);
        try
        {
            if (!await WaitUntilAsync(() => pane.IsReady, TimeSpan.FromSeconds(15)))
                return SelfTestCheck.Fail("map.draw", "no ready");
            // Offline init (no tile traffic in the selftest), then the Ref §9.6 golden setData + select.
            pane.PostJson("""{"v":1,"type":"init","config":{"streetsStyleUrl":"https://tiles.openfreemap.org/styles/liberty","streetsDarkStyleUrl":"https://tiles.openfreemap.org/styles/dark","satelliteUrl":"https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}"},"base":"streets","radiusMiles":50,"online":false,"theme":"dark"}""");
            pane.PostJson("""{"v":1,"type":"setData","rev":7,"items":[{"id":"DCIM/DJI_001/DJI_20260927140627_0128_D.MP4","groupId":"DCIM/DJI_001/DJI_20260927140127_0123_D.MP4","lon":-153.738973,"lat":57.550442,"kind":"video"},{"id":"DCIM/DJI_001/DJI_20260927141000_0129_D.MP4","groupId":"DCIM/DJI_001/DJI_20260927140127_0123_D.MP4","lon":-153.7409,"lat":57.5415,"kind":"videoNoGps"}],"groups":[{"id":"DCIM/DJI_001/DJI_20260927140127_0123_D.MP4","color":"#1F77B4","center":[-153.7409,57.5415],"label":"Sep 27 · 13 clips"}],"jumps":[{"from":[-164.2657,64.6935],"to":[-165.3696,64.5627],"label":"34 mi · 21 h"}]}""");
            pane.PostJson("""{"v":1,"type":"select","groupId":"DCIM/DJI_001/DJI_20260927140127_0123_D.MP4","itemIds":["DCIM/DJI_001/DJI_20260927140627_0128_D.MP4"],"fit":true,"bbox":[-153.76,57.53,-153.72,57.56]}""");
            pane.PostJson("""{"v":1,"type":"setRadius","radiusMiles":25}""");
            var stats = await PollEvaluateAsync(pane, "JSON.stringify(window.__uas.stats())", s =>
                s is not null && s.Contains("\"items\":2", StringComparison.Ordinal) && s.Contains("\"radius\":1", StringComparison.Ordinal)
                && s.Contains("\"jumps\":1", StringComparison.Ordinal) && s.Contains("\"selected\":1", StringComparison.Ordinal)
                && s.Contains("\"base\":\"none\"", StringComparison.Ordinal), TimeSpan.FromSeconds(8));
            bool ok = stats is not null && stats.Contains("\"base\":\"none\"", StringComparison.Ordinal)
                      && stats.Contains("\"items\":2", StringComparison.Ordinal) && stats.Contains("\"radius\":1", StringComparison.Ordinal);
            return ok ? SelfTestCheck.Pass("map.draw", "offline canvas drew " + stats)
                      : SelfTestCheck.Fail("map.draw", "stats " + (stats ?? "(none)"));
        }
        finally { RemoveStandaloneMapPane(ctx); }
    }

    /// <summary>F15 (Ref §9.6 Degradation, §12 "Map fails → fallback panel… Everything else works"): a WebView2 environment that
    /// can't be created, or whose creation hangs, shows the Map unavailable panel and nothing reaches the unhandled path. The failure
    /// is injected through MapPane's factory seam, so no Edge process starts (a real bad user-data folder makes Edge show its own
    /// "can't read and write to its data directory" dialog on the desktop).</summary>
    private static async Task<SelfTestCheck> MapUnavailable(SelfTestContext ctx)
    {
        var failed = await UnavailableAfterAsync(ctx, _ => Task.FromException<CoreWebView2Environment>(
            new UnauthorizedAccessException("Access to the WebView2 user-data folder is denied (selftest)")), MapPane.StartTimeout);
        if (failed.Error is { } e1) return SelfTestCheck.Fail("map.unavailable", "failing start: " + e1);
        var hung = await UnavailableAfterAsync(ctx, _ => new TaskCompletionSource<CoreWebView2Environment>().Task, TimeSpan.FromSeconds(1));
        if (hung.Error is { } e2) return SelfTestCheck.Fail("map.unavailable", "hanging start: " + e2);
        return SelfTestCheck.Pass("map.unavailable", $"failing start → \"{failed.Text}\"; hanging start → \"{hung.Text}\"");
    }

    private static async Task<(string? Text, string? Error)> UnavailableAfterAsync(SelfTestContext ctx,
        Func<string, Task<CoreWebView2Environment>> factory, TimeSpan startTimeout)
    {
        var pane = new MapPane { Width = 320, Height = 200, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var root = (Grid)ctx.Window.Shell!.Content;
        Grid.SetRowSpan(pane, 2);
        root.Children.Add(pane);
        try
        {
            var init = pane.InitializeAsync(ctx.Services, factory, startTimeout);
            bool shown = await WaitUntilAsync(() => pane.IsUnavailable, startTimeout + TimeSpan.FromSeconds(3));
            if (init.IsFaulted) return (null, "InitializeAsync faulted: " + init.Exception!.GetBaseException().Message);
            if (!shown || pane.IsReady) return (null, $"unavailable={pane.IsUnavailable}, ready={pane.IsReady}");
            if (pane.Core is not null) return (null, "a WebView2 was created");
            return (pane.UnavailableText, null);
        }
        finally
        {
            pane.Close();
            root.Children.Remove(pane);
        }
    }

    /// <summary>Polls a JS expression from the UI thread without blocking it.</summary>
    private static async Task<string?> PollEvaluateAsync(MapPane pane, string expression, Func<string?, bool> done, TimeSpan timeout)
    {
        var until = TimeProvider.System.GetTimestamp() + (long)(timeout.TotalSeconds * TimeProvider.System.TimestampFrequency);
        string? last = null;
        while (TimeProvider.System.GetTimestamp() < until)
        {
            last = await pane.EvaluateAsync(expression);
            if (done(last)) return last;
            await Task.Delay(100);
        }
        return last;
    }

    private static List<string> Snapshot(List<string> list) { lock (list) return [.. list]; }

    private static bool IsPong1(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("type", out var t) && t.GetString() == "pong"
               && doc.RootElement.TryGetProperty("n", out var n) && n.GetInt32() == 1;
    }
}
