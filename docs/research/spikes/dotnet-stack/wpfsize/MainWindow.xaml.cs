using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
namespace WpfSize;
public partial class GroupVm : ObservableObject { [ObservableProperty] public partial string Folder { get; set; } = ""; [ObservableProperty] public partial string Description { get; set; } = ""; [ObservableProperty] public partial int Count { get; set; } }
public partial class MainVm : ObservableObject {
  public ObservableCollection<GroupVm> Groups { get; } = [new() { Folder = "2026-09-27", Description = "Zachar Bay", Count = 15 }];
  [ObservableProperty] public partial string Status { get; set; } = "Ready";
  [RelayCommand] async Task Rescan() { Status = "Scanning..."; await Task.Delay(200); Status = $"{GeoTimeZone.TimeZoneLookup.GetTimeZone(57.55, -153.74).Result}"; }
}
public partial class MainWindow : System.Windows.Window { public MainWindow() { InitializeComponent(); DataContext = new MainVm(); } }
