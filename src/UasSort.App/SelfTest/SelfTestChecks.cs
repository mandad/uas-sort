// src/UasSort.App/SelfTest/SelfTestChecks.cs — the registry; later tasks append entries to All
using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;

namespace UasSort.App.SelfTest;

internal static partial class SelfTestChecks
{
    public static readonly List<(string Name, Func<SelfTestContext, Task<SelfTestCheck>> Run)> All =
    [
        ("shell.render", ShellRender),
        ("window.minSize", WindowMinSize),
        ("probe.timeZone", ProbeTimeZone),
        ("json.planEdit", JsonPlanEdit),
        ("placeholderVisibility", PlaceholderVisibility),
        ("page.settings", PageSettings),
        ("page.card", PageCard),
        ("map.mime", MapMime), ("map.ready", MapReady),
    ];

    private static Task<SelfTestCheck> ShellRender(SelfTestContext ctx)
    {
        var shell = ctx.Window.Shell;
        if (shell is null) return Task.FromResult(SelfTestCheck.Fail("shell.render", "ShellPage is not the window content"));
        var title = VisualTree.FindDescendant<TitleBar>(shell);
        return Task.FromResult(title?.Title == "uas-sort" && shell.StageFrame is not null
            ? SelfTestCheck.Pass("shell.render", $"TitleBar and stage frame rendered; first frame {ctx.FirstFrameMs:0} ms")
            : SelfTestCheck.Fail("shell.render", "TitleBar 'uas-sort' not found"));
    }

    private static Task<SelfTestCheck> WindowMinSize(SelfTestContext ctx)
    {
        var scale = ctx.Window.Shell?.XamlRoot?.RasterizationScale ?? 1.0;
        var p = (OverlappedPresenter)ctx.Window.AppWindow.Presenter;
        int w = (int)Math.Ceiling(MainWindow.MinWidth * scale), h = (int)Math.Ceiling(MainWindow.MinHeight * scale);
        return Task.FromResult(p.PreferredMinimumWidth == w && p.PreferredMinimumHeight == h
            ? SelfTestCheck.Pass("window.minSize", $"{w}x{h} physical at scale {scale}")
            : SelfTestCheck.Fail("window.minSize", $"expected {w}x{h}, got {p.PreferredMinimumWidth}x{p.PreferredMinimumHeight}"));
    }

    private static Task<SelfTestCheck> ProbeTimeZone(SelfTestContext ctx)
    {
        var zone = new GeoTimeZoneResolver().Resolve(new GeoPoint(57.5504, -153.7390));   // Zachar Bay
        var tz = TimeZoneInfo.FindSystemTimeZoneById(zone.IanaId);
        var local = TimeZoneInfo.ConvertTimeFromUtc(new DateTime(2026, 9, 27, 18, 6, 27, DateTimeKind.Utc), tz);
        return Task.FromResult(zone.IanaId == "America/Anchorage" && local == new DateTime(2026, 9, 27, 10, 6, 27)
            ? SelfTestCheck.Pass("probe.timeZone", "Zachar Bay → America/Anchorage, 18:06:27Z → 10:06:27")
            : SelfTestCheck.Fail("probe.timeZone", $"{zone.IanaId}, {local:O}"));
    }

    private static Task<SelfTestCheck> JsonPlanEdit(SelfTestContext ctx)
    {
        PlanEdit[] edits =
        [
            new Merge(new ItemId("DCIM/DJI_001/a.MP4"), new ItemId("DCIM/DJI_001/b.MP4")),
            new Retarget(new ItemId("DCIM/DJI_001/a.MP4"), new AppendTo(@"C:\v\2026\2026-07\2026-07-25 Council Road"), false, []),
            new SetDayIncluded(new DateOnly(2026, 7, 25), true),
        ];
        foreach (var e in edits)
        {
            var json = JsonSerializer.Serialize(e, CoreJsonContext.Default.PlanEdit);
            var back = JsonSerializer.Deserialize(json, CoreJsonContext.Default.PlanEdit);
            if (back is null || back.GetType() != e.GetType() || JsonSerializer.Serialize(back, CoreJsonContext.Default.PlanEdit) != json)
                return Task.FromResult(SelfTestCheck.Fail("json.planEdit", "round-trip changed " + json));
        }
        return Task.FromResult(SelfTestCheck.Pass("json.planEdit", "Merge, Retarget(AppendTo), SetDayIncluded round-trip"));
    }

    /// <summary>Ref §13: list this PC's configured video root (real settings, read-only; listing only, .uas-sort excluded) and
    /// require an entry with 0x400000 or 0x1000; none → notApplicable (deploy then needs -AllowNoPlaceholders).
    /// PlatformServices.Create writes nothing, so the real app-data folder is only read. Gated (ruling P11-C5): during the build
    /// it never runs, so the real video root (which may be under OneDrive) is never listed unless UASSORT_PLACEHOLDER_CHECK=1.</summary>
    private static Task<SelfTestCheck> PlaceholderVisibility(SelfTestContext ctx)
    {
        if (Environment.GetEnvironmentVariable("UASSORT_PLACEHOLDER_CHECK") != "1") return Task.FromResult(SelfTestCheck.NotApplicable("placeholderVisibility", "skipped during the build; set UASSORT_PLACEHOLDER_CHECK=1 to list the real video root"));
        var real = PlatformServices.Create(KnownFolders.AppDataDir(), TimeProvider.System);
        var settings = real.Settings.Load(readOnly: true).Settings;
        var listing = real.Lister.Enumerate(settings.VideoRoot, recurse: true,
                                            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".uas-sort" });
        if (listing.Entries.IsEmpty && !listing.Errors.IsEmpty)
            return Task.FromResult(SelfTestCheck.NotApplicable("placeholderVisibility", $"video root {settings.VideoRoot} not listable"));
        var hit = listing.Entries.FirstOrDefault(e => (e.RawAttributes & (0x400000u | 0x1000u)) != 0);
        return Task.FromResult(hit is not null
            ? SelfTestCheck.Pass("placeholderVisibility", $"0x{hit.RawAttributes:X} on {hit.RelPath}")
            : SelfTestCheck.NotApplicable("placeholderVisibility", $"no cloud-only files under {settings.VideoRoot}"));
    }
}
