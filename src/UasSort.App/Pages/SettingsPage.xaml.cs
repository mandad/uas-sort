// src/UasSort.App/Pages/SettingsPage.xaml.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace UasSort.App.Pages;

public sealed partial class SettingsPage : Page
{
    private MainWindow _window = null!;
    public SettingsPage() => InitializeComponent();
    public SettingsPageVm Vm { get; private set; } = null!;

    /// <summary>The fixed-zone choices: DroneClock.UsZones, plus the saved zone when it is not one of them.
    /// A List because it is an ItemsSource (ruling P11-C6).</summary>
    public List<string> ZoneIds { get; private set; } = [];

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var args = (StageArgs)e.Parameter;
        Vm = (SettingsPageVm)args.Vm;                            // ShellVm.Current on the Settings stage
        _window = args.Window;
        ZoneIds = DroneClock.UsZones.Contains(Vm.ClockZone) ? [.. DroneClock.UsZones] : [.. DroneClock.UsZones, Vm.ClockZone];
        Bindings.Update();
        ZoneBox.SelectedItem = Vm.ClockZone;
        PresetMenu.Items.Clear();
        foreach (var (name, url) in Vm.SatellitePresets)
        {
            var item = new MenuFlyoutItem { Text = name };
            item.Click += (_, _) => Vm.SatelliteUrl = url;
            PresetMenu.Items.Add(item);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => _window.Services.Shell.CloseSettings();

    private async void OnPickVideoRoot(object sender, RoutedEventArgs e)
    {
        if (await FolderPickerService.PickFolderAsync(_window, Vm.VideoRoot) is { } path) await Vm.ChangeVideoRootAsync(path);
    }

    private async void OnPickPhotoRoot(object sender, RoutedEventArgs e)
    {
        if (await FolderPickerService.PickFolderAsync(_window, Vm.PhotoRoot) is { } path) Vm.ChangePhotoRoot(path);
    }

    private void OnGapChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (Vm is not null && !double.IsNaN(args.NewValue)) Vm.GapDays = (int)Math.Round(args.NewValue);
    }

    private void OnClockModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Vm is not null && ClockModeButtons.SelectedIndex >= 0) Vm.IsSiteLocal = ClockModeButtons.SelectedIndex == 0;
    }

    private void OnZoneChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Vm is not null && ZoneBox.SelectedItem is string zone) Vm.ClockZone = zone;
    }

    private void OnBaseChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Vm is not null && BaseButtons.SelectedIndex >= 0) Vm.MapBase = UiFormat.BaseAt(BaseButtons.SelectedIndex);
    }
}
