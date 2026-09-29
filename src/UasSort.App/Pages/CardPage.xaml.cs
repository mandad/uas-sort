// src/UasSort.App/Pages/CardPage.xaml.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace UasSort.App.Pages;

public sealed partial class CardPage : Page
{
    private MainWindow _window = null!;
    public CardPage() => InitializeComponent();
    public CardStageVm Vm { get; private set; } = null!;
    public ShellVm Shell { get; private set; } = null!;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var args = (StageArgs)e.Parameter;
        Vm = (CardStageVm)args.Vm;                               // ShellVm.Current (= ShellVm.Card) on the Card stage
        _window = args.Window;
        Shell = _window.Services.Shell;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "XAML event handler: the generated Connect code wires it as this.OnVolumeInvoked, so it must be an instance method.")]
    private void OnVolumeInvoked(ItemsView sender, ItemsViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is CardRowVm row && row.UseCommand.CanExecute(null)) row.UseCommand.Execute(null);
    }

    private void OnRescanAccelerator(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        if (Vm.RescanCommand.CanExecute(null)) Vm.RescanCommand.Execute(null);
        args.Handled = true;
    }

    private async void OnBrowse(object sender, RoutedEventArgs e)
    {
        if (await FolderPickerService.PickFolderAsync(_window, null) is { } path) Vm.Browse(path);
    }
}
