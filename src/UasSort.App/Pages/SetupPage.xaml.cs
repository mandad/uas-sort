// src/UasSort.App/Pages/SetupPage.xaml.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace UasSort.App.Pages;

public sealed partial class SetupPage : Page
{
    private MainWindow _window = null!;
    public SetupPage() => InitializeComponent();
    public SetupVm Vm { get; private set; } = null!;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var args = (StageArgs)e.Parameter;
        Vm = (SetupVm)args.Vm;                                   // ShellVm.Current on the Setup stage
        _window = args.Window;
    }

    private async void OnPickVideoRoot(object sender, RoutedEventArgs e)
    {
        if (await FolderPickerService.PickFolderAsync(_window, Vm.VideoRoot) is { } path) Vm.SetVideoRoot(path);
    }

    private async void OnPickPhotoRoot(object sender, RoutedEventArgs e)
    {
        if (await FolderPickerService.PickFolderAsync(_window, Vm.PhotoRoot) is { } path) Vm.SetPhotoRoot(path);
    }
}
