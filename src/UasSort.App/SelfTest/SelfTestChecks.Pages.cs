// src/UasSort.App/SelfTest/SelfTestChecks.Pages.cs
using System.Globalization;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Foundation;

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
        // Clearing the radius box makes its Value NaN: it must never reach the settings.
        var radiusBefore = page.Vm.Current.RadiusMiles;
        var radius = VisualTree.FindDescendant<NumberBox>(page, n => n.Name == "RadiusBox");
        if (radius is not null) radius.Value = double.NaN;
        bool nanIgnored = radius is not null && !double.IsNaN(page.Vm.RadiusMiles) && page.Vm.Current.RadiusMiles == radiusBefore;
        shell.CloseSettings();
        await WaitUntilAsync(() => shell.Stage == before, TimeSpan.FromSeconds(5));
        return video is not null && rootShown && about && nanIgnored
            ? SelfTestCheck.Pass("page.settings", "Video folder card shows the sandbox root; About credits GeoNames; a cleared radius box is ignored")
            : SelfTestCheck.Fail("page.settings", $"card={video is not null} root={rootShown} about={about} nanIgnored={nanIgnored} (vm {page.Vm.RadiusMiles}, saved {page.Vm.Current.RadiusMiles})");
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
        () => new CleanupPage(), () => new PhotoCleanupPage(),
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

    /// <summary>Ref §10.6: the selftest card came from Browse to folder, so [Clean up card…] must be disabled with that reason, shown
    /// as the tooltip and as visible text beside the button (Task U4: a disabled button shows no tooltip).</summary>
    private static async Task<SelfTestCheck> CleanupEntry(SelfTestContext ctx)
    {
        await ReviewPageAsync(ctx);
        var shell = ctx.Services.Shell;
        const string reason = "Cleanup works only on a detected card. A browsed folder could be a backup copy.";
        var button = VisualTree.FindDescendant<Button>(ctx.Window.Shell!, b => b.Name == "CleanUpButton");
        var text = VisualTree.FindDescendant<TextBlock>(ctx.Window.Shell!, t => t.Name == "CleanupReasonText");
        bool ok = !shell.CleanupEnabled && shell.CleanupTooltip == reason && button is { IsEnabled: false }
                  && (ToolTipService.GetToolTip(button) as string) == reason
                  && shell.CleanupUnavailableText == reason && text is { Visibility: Microsoft.UI.Xaml.Visibility.Visible } && text.Text == reason;
        return ok ? SelfTestCheck.Pass("cleanup.entry", "title-bar [Clean up card…] disabled, reason shown: " + reason)
                  : SelfTestCheck.Fail("cleanup.entry", $"enabled {shell.CleanupEnabled}, tooltip '{shell.CleanupTooltip}', button enabled {button?.IsEnabled}, "
                                                        + $"visible text '{text?.Text}' ({text?.Visibility})");
    }

    private static async Task<SelfTestCheck> DeviceHook(SelfTestContext ctx)
    {
        var watcher = ctx.Window.DeviceWatcher;
        if (watcher is null) return SelfTestCheck.Fail("device.hook", "no DeviceChangeWatcher on the main window");
        int n0 = watcher.Notifications, r0 = watcher.Refreshes, s0 = watcher.Suppressed;
        bool enabled = !DeviceChangeWatcher.IsSuppressed(ctx.Services.Shell.Stage);
        // Two arrivals in quick succession → one debounced refresh (or one suppression while Commit/Cleanup runs).
        WindowInterop.Send(ctx.Window.Hwnd, WindowMessageHook.WM_DEVICECHANGE, WindowMessageHook.DBT_DEVICEARRIVAL, 0);
        WindowInterop.Send(ctx.Window.Hwnd, WindowMessageHook.WM_DEVICECHANGE, WindowMessageHook.DBT_DEVICEARRIVAL, 0);
        await Task.Delay(DeviceChangeWatcher.Debounce + TimeSpan.FromMilliseconds(500));
        bool ok = watcher.Notifications == n0 + 2
                  && (enabled ? watcher.Refreshes == r0 + 1 && watcher.Suppressed == s0 : watcher.Refreshes == r0 && watcher.Suppressed == s0 + 1);
        return ok ? SelfTestCheck.Pass("device.hook", $"2 notifications → {(enabled ? "1 refresh" : "suppressed")}")
                  : SelfTestCheck.Fail("device.hook", $"notifications {watcher.Notifications - n0}, refreshes {watcher.Refreshes - r0}, suppressed {watcher.Suppressed - s0}");
    }

    /// <summary>An undecided cleanup row for clip A of the synthetic card (Decision stays Undecided: Update is Review-internal).</summary>
    public static CleanupRowVm SelfTestCleanupRow(Action<CleanupRowVm, RowDecision> set)
    {
        var t = new DateTime(2026, 7, 26, 3, 50, 0, DateTimeKind.Utc);
        var candidate = new CleanupCandidate(new ItemId(SelfTestFixture.ClipA), [], 0, t, new DateOnly(2026, 7, 25), "America/Anchorage",
            CleanupEligibility.NotInLibrary, AuditCategory.Unaccounted, "selftest row", null, null, null, ItemKind.Video, t.AddHours(-8),
            NotInLibraryReason.New, null, null, [], null, false, []);
        return new CleanupRowVm(candidate, set);
    }

    /// <summary>The Cleanup page's Keep/Delete ToggleButtons: a click whose command leaves the decision as it was (SetRow re-sets
    /// the same decision) must not leave the button in the state the click flipped it to. Uses the page's own row template.</summary>
    private static async Task<SelfTestCheck> CleanupToggleResync(SelfTestContext ctx)
    {
        await EnsureReviewAsync(ctx);                                   // the card files exist, so the row's thumbnail can load
        var decisions = new List<RowDecision>();
        var row = SelfTestCleanupRow((_, d) => decisions.Add(d));       // the decision never changes: Undecided stays
        var owner = new CleanupPage();
        var view = new ItemsView { Height = 200, Width = 600, ItemTemplate = owner.ReviewList.ItemTemplate, ItemsSource = new List<CleanupRowVm> { row } };
        var popup = new Popup { XamlRoot = ctx.Window.Content.XamlRoot, Child = view, IsOpen = true };
        try
        {
            ToggleButton? Find(string tag) => VisualTree.FindDescendant<ToggleButton>(view, b => (b.Tag as string) == tag);
            if (!await WaitUntilAsync(() => Find("Delete") is not null && Find("Keep") is not null, TimeSpan.FromSeconds(5)))
                return SelfTestCheck.Fail("cleanup.toggleResync", "row buttons not realised");
            var delete = Find("Delete")!;
            var keep = Find("Keep")!;
            new ToggleButtonAutomationPeer(delete).Toggle();            // OnClick: flips IsChecked, raises Click, runs DeleteCommand
            new ToggleButtonAutomationPeer(keep).Toggle();
            await Task.Delay(100);
            bool ok = decisions.SequenceEqual((RowDecision[])[RowDecision.Delete, RowDecision.Keep]) && row.Decision == RowDecision.Undecided
                      && delete.IsChecked == false && keep.IsChecked == false;
            return ok ? SelfTestCheck.Pass("cleanup.toggleResync", "clicks that left the row undecided left both buttons off")
                      : SelfTestCheck.Fail("cleanup.toggleResync", $"commands [{string.Join(",", decisions)}], decision {row.Decision}, delete {delete.IsChecked}, keep {keep.IsChecked}");
        }
        finally { popup.IsOpen = false; }
    }

    /// <summary>Task U2 (user-reported): with many days of not-copied photos, the Verdict page's per-day buttons must wrap onto
    /// several lines and stay inside the page at the minimum window size, and [Select all photos and sets] / [Clear selection]
    /// must work through the real buttons. The verdict is synthetic, built on the scanned plan: <see cref="ManyDays"/> photo days
    /// cloned from the fixture DNG, plus clip A as a failed video. It is shown in its own VerdictPage at 1100×700. Nothing is
    /// decided or written.</summary>
    private static async Task<SelfTestCheck> VerdictDaySelection(SelfTestContext ctx)
    {
        const string name = "verdict.daySelection";
        var review = await EnsureReviewAsync(ctx);
        var (plan, verdict, video) = ManyPhotoDays(review.Plan, ManyDays);
        var p = ctx.Services.Platform;
        var vm = new VerdictVm(verdict, plan, null, null, new VerdictPorts(p.LedgerFor(ctx.Sandbox.VideoRoot), p.Machine, p.Clock,
                                                                          ctx.Services.Dialogs, p.Shell, p.Eject, () => verdict));
        var frame = new Frame { Width = MainWindow.MinWidth, Height = MainWindow.MinHeight };
        var popup = new Popup { XamlRoot = ctx.Window.Content.XamlRoot, Child = frame, IsOpen = true };
        try
        {
            frame.Navigate(typeof(VerdictPage), new StageArgs(vm, ctx.Window));
            var texts = vm.NotCopiedDays.Select(d => d.Text).ToHashSet(StringComparer.Ordinal);
            List<Button> DayButtons(VerdictPage page) => VisualTree.FindAll<Button>(page.DayList, b => b.Content is string t && texts.Contains(t));
            if (!await WaitUntilAsync(() => frame.Content is VerdictPage { IsLoaded: true } pg
                                            && DayButtons(pg) is { Count: ManyDays } bs && bs.All(b => b.ActualWidth > 0), TimeSpan.FromSeconds(5)))
                return SelfTestCheck.Fail(name, $"{ManyDays} day buttons not laid out (days {vm.NotCopiedDays.Count})");
            var page = (VerdictPage)frame.Content;

            var lines = new HashSet<long>();
            double right = 0;
            foreach (var b in DayButtons(page))
            {
                var at = b.TransformToVisual(page).TransformPoint(new Point(0, 0));
                lines.Add((long)Math.Round(at.Y));
                right = Math.Max(right, at.X + b.ActualWidth);
            }
            if (lines.Count < 2 || right > page.ActualWidth + 0.5)
                return SelfTestCheck.Fail(name, $"day buttons on {lines.Count} line(s), rightmost edge {right:0} vs page width {page.ActualWidth:0}");

            int Checked() => VisualTree.FindAll<CheckBox>(page.NotCopiedList, c => c.IsChecked == true).Count;
            bool startedEmpty = Checked() == 0 && vm.SelectionText is null && page.SelectAllButton.IsEnabled && !page.ClearSelectionButton.IsEnabled;
            new ButtonAutomationPeer(page.SelectAllButton).Invoke();
            bool all = await WaitUntilAsync(() => Checked() == ManyDays && !page.SelectAllButton.IsEnabled && page.ClearSelectionButton.IsEnabled,
                                            TimeSpan.FromSeconds(3));
            var selected = vm.NotCopied.Where(r => r.IsSelected).ToList();
            var selectionText = vm.SelectionText;
            bool onlyPhotos = selected.Count == ManyDays && selected.All(r => r.Kind == NotCopiedKind.Photo)
                              && !vm.NotCopied.Single(r => r.Unit == video).IsSelected
                              && selectionText?.StartsWith($"{ManyDays} photos · ", StringComparison.Ordinal) == true;
            new ButtonAutomationPeer(page.ClearSelectionButton).Invoke();
            bool cleared = await WaitUntilAsync(() => Checked() == 0 && vm.SelectionText is null && !page.ClearSelectionButton.IsEnabled
                                                      && page.SelectAllButton.IsEnabled, TimeSpan.FromSeconds(3));
            return startedEmpty && all && onlyPhotos && cleared
                ? SelfTestCheck.Pass(name, $"{ManyDays} day buttons on {lines.Count} lines, rightmost edge {right:0} within page {page.ActualWidth:0}; "
                                           + $"Select all checked {ManyDays} photos ('{selectionText}'), not the video; Clear unchecked all")
                : SelfTestCheck.Fail(name, $"startedEmpty {startedEmpty}, selectAll {all} (selected {selected.Count}, onlyPhotos {onlyPhotos}, "
                                           + $"'{selectionText}'), cleared {cleared}");
        }
        finally { popup.IsOpen = false; }
    }

    private const int ManyDays = 40;

    /// <summary>The scanned plan plus <paramref name="days"/> photos, one per local day from 2026-06-01, cloned from the fixture DNG;
    /// the verdict has each clone as AssumedByRule and the first video (clip A) as Unaccounted.</summary>
    private static (Plan Plan, FormatVerdict Verdict, ItemId Video) ManyPhotoDays(Plan plan, int days)
    {
        var dng = plan.Base.Items.First(i => i.Raw.Unit is PhotoUnit);
        var unit = (PhotoUnit)dng.Raw.Unit;
        var video = plan.Base.Items.First(i => i.Raw.Unit is VideoUnit).Raw.Unit.Id;
        var first = new DateOnly(2026, 6, 1);
        List<Item> clones = [.. Enumerable.Range(0, days).Select(k =>
        {
            var id = new ItemId(string.Create(CultureInfo.InvariantCulture, $"DCIM/DJI_002/DJI_SELFTEST_{k:000}_D.DNG"));
            var u = unit with { Id = id, Primary = unit.Primary with { RelPath = id.CardRelPath }, JpgTwin = null };
            return dng with { Raw = dng.Raw with { Unit = u, Name = Path.GetFileName(id.CardRelPath) }, Time = dng.Time with { LocalDate = first.AddDays(k) } };
        })];
        var inventory = plan.Base.Scan.Inventory;
        var withClones = plan with
        {
            Base = plan.Base with
            {
                Scan = plan.Base.Scan with { Inventory = inventory with { Units = [.. inventory.Units, .. clones.Select(c => c.Raw.Unit)] } },
                Items = [.. plan.Base.Items, .. clones],
            },
        };
        static UnitAudit Audit(ItemId id, AuditCategory c, long size, string detail) => new(id, c, [new AuditLine(id.CardRelPath, size, c, detail)]);
        ImmutableArray<UnitAudit> units =
        [
            Audit(video, AuditCategory.Unaccounted, 1_000_000, "failed: verify"),
            .. clones.Select(c => Audit(c.Raw.Unit.Id, AuditCategory.AssumedByRule, unit.Primary.Size, "probably imported")),
        ];
        var verdict = new FormatVerdict(VerdictLevel.NotSafe, new CardIdentity(0, "SELFTEST", "exFAT", 0), "selftest verdict",
                                        ImmutableDictionary<AuditCategory, int>.Empty, 0, 0, units, [], null);
        return (withClones, verdict, video);
    }
}
