// src/UasSort.App/Pages/CopyPage.xaml.cs
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace UasSort.App.Pages;

public sealed partial class CopyPage : Page
{
    public CopyPage() => InitializeComponent();
    public CopyVm Vm { get; private set; } = null!;
    protected override void OnNavigatedTo(NavigationEventArgs e) => Vm = (CopyVm)((StageArgs)e.Parameter).Vm;
}
