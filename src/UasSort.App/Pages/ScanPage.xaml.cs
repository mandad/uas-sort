// src/UasSort.App/Pages/ScanPage.xaml.cs
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace UasSort.App.Pages;

public sealed partial class ScanPage : Page
{
    public ScanPage() => InitializeComponent();
    public ScanStageVm Vm { get; private set; } = null!;
    public ShellVm Shell { get; private set; } = null!;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var args = (StageArgs)e.Parameter;
        Vm = (ScanStageVm)args.Vm;                               // ShellVm.Current on the Scan stage
        Shell = args.Window.Services.Shell;
    }
}
