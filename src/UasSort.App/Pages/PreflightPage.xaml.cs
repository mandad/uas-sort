// src/UasSort.App/Pages/PreflightPage.xaml.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace UasSort.App.Pages;

public sealed partial class PreflightPage : Page
{
    private MainWindow _window = null!;
    public PreflightPage() => InitializeComponent();
    public PreflightVm Vm { get; private set; } = null!;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var args = (StageArgs)e.Parameter;
        Vm = (PreflightVm)args.Vm;                               // ShellVm.Current (= ShellVm.Preflight) on the Preflight stage
        _window = args.Window;
        BlockingList.Show(Vm.Blocking, InfoBarSeverity.Error);
        WarningList.Show(Vm.Warnings, InfoBarSeverity.Warning);
        InfoList.Show(Vm.Infos, InfoBarSeverity.Informational);
    }

    private async void OnStart(object sender, RoutedEventArgs e) => await _window.Services.Shell.StartCopyAsync();
}
