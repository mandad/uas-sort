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
}
