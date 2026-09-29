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
}
