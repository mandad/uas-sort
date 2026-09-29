// src/UasSort.App/Pages/VerdictPage.xaml.cs
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace UasSort.App.Pages;

public sealed partial class VerdictPage : Page
{
    public VerdictPage() => InitializeComponent();
    public VerdictVm Vm { get; private set; } = null!;

    // ShellVm.Current (= ShellVm.Verdict) on the Verdict stage
    protected override void OnNavigatedTo(NavigationEventArgs e) => Vm = (VerdictVm)((StageArgs)e.Parameter).Vm;
}
