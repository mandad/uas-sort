// src/UasSort.App/SelfTest/SelfTestChecks.Pages.cs
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml.Controls;

namespace UasSort.App.SelfTest;

internal static partial class SelfTestChecks
{
    public static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var until = TimeProvider.System.GetTimestamp() + (long)(timeout.TotalSeconds * TimeProvider.System.TimestampFrequency);
        while (!condition())
        {
            if (TimeProvider.System.GetTimestamp() > until) return false;
            await Task.Delay(50);
        }
        return true;
    }

    public static T? CurrentPage<T>(SelfTestContext ctx) where T : Page => ctx.Window.Shell?.StageFrame.Content as T;

    private static async Task<SelfTestCheck> PageSettings(SelfTestContext ctx)
    {
        var shell = ctx.Services.Shell;
        var before = shell.Stage;
        shell.SettingsCommand.Execute(null);
        if (!await WaitUntilAsync(() => CurrentPage<SettingsPage>(ctx) is { IsLoaded: true }, TimeSpan.FromSeconds(5)))
            return SelfTestCheck.Fail("page.settings", "SettingsPage did not open");
        var page = CurrentPage<SettingsPage>(ctx)!;
        var video = VisualTree.FindDescendant<SettingsCard>(page, c => (c.Header as string) == "Video folder");
        var rootShown = VisualTree.FindDescendant<TextBlock>(page, t => t.Text == ctx.Sandbox.VideoRoot) is not null;
        var about = VisualTree.FindDescendant<TextBlock>(page, t => t.Text.Contains("GeoNames", StringComparison.Ordinal)) is not null;
        shell.CloseSettings();
        await WaitUntilAsync(() => shell.Stage == before, TimeSpan.FromSeconds(5));
        return video is not null && rootShown && about
            ? SelfTestCheck.Pass("page.settings", "Video folder card shows the sandbox root; About credits GeoNames")
            : SelfTestCheck.Fail("page.settings", $"card={video is not null} root={rootShown} about={about}");
    }

    private static async Task<SelfTestCheck> PageCard(SelfTestContext ctx)
    {
        // Under --selftest the Card stage lists no volumes (NoVolumes, Task 11.4), so it opens empty.
        if (!await WaitUntilAsync(() => CurrentPage<CardPage>(ctx) is { IsLoaded: true }, TimeSpan.FromSeconds(10)))
            return SelfTestCheck.Fail("page.card", $"Card stage not shown (stage {ctx.Services.Shell.Stage})");
        var page = CurrentPage<CardPage>(ctx)!;
        var browse = VisualTree.FindDescendant<Button>(page, b => (b.Content as string) == "Browse to folder…");
        var empty = page.Vm.Rows.Count == 0
                    && VisualTree.FindDescendant<TextBlock>(page, t => t.Text == page.Vm.StatusText && t.Text.Length > 0) is not null;
        return browse is { IsEnabled: true } && empty
            ? SelfTestCheck.Pass("page.card", "empty Card stage with Browse to folder…: " + page.Vm.StatusText)
            : SelfTestCheck.Fail("page.card", $"browse={browse?.IsEnabled} empty={empty}");
    }

    /// <summary>Every stage page must load its XAML (x:Bind paths are compile-checked; markup errors only show at load).</summary>
    public static readonly List<Func<Page>> PageFactories =
    [
        () => new SetupPage(), () => new CardPage(), () => new ScanPage(), () => new SettingsPage(),
        () => new PreflightPage(), () => new CopyPage(), () => new VerdictPage(),
        () => new CleanupPage(),
    ];

    private static Task<SelfTestCheck> PagesConstruct(SelfTestContext ctx)
    {
        var built = new List<string>();
        foreach (var make in PageFactories)
        {
            var page = make();                                   // InitializeComponent parses the page's XAML
            built.Add(page.GetType().Name);
        }
        return Task.FromResult(SelfTestCheck.Pass("pages.construct", "loaded " + string.Join(", ", built)));
    }

    /// <summary>Ref §10.6: the selftest card came from Browse to folder, so [Clean up card…] must be disabled with that reason.</summary>
    private static async Task<SelfTestCheck> CleanupEntry(SelfTestContext ctx)
    {
        await ReviewPageAsync(ctx);
        var shell = ctx.Services.Shell;
        const string reason = "Cleanup works only on a detected card. A browsed folder could be a backup copy.";
        var button = VisualTree.FindDescendant<Button>(ctx.Window.Shell!, b => b.Name == "CleanUpButton");
        bool ok = !shell.CleanupEnabled && shell.CleanupTooltip == reason && button is { IsEnabled: false }
                  && (ToolTipService.GetToolTip(button) as string) == reason;
        return ok ? SelfTestCheck.Pass("cleanup.entry", "title-bar [Clean up card…] disabled: " + reason)
                  : SelfTestCheck.Fail("cleanup.entry", $"enabled {shell.CleanupEnabled}, tooltip '{shell.CleanupTooltip}', button enabled {button?.IsEnabled}");
    }
}
