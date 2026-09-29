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

    private static List<string> Snapshot(List<string> list) { lock (list) return [.. list]; }

    private static bool IsPong1(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("type", out var t) && t.GetString() == "pong"
               && doc.RootElement.TryGetProperty("n", out var n) && n.GetInt32() == 1;
    }
}
