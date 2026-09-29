// src/UasSort.App/SelfTest/SelfTestChecks.Map.cs
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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
