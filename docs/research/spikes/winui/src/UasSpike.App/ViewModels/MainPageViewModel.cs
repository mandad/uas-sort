using CommunityToolkit.Mvvm.ComponentModel;

namespace UasSpike.App.ViewModels;

/// <summary>CommunityToolkit.Mvvm 8.4 partial-property style (WinRT/AOT friendly).</summary>
public partial class MainPageViewModel : ObservableObject
{
    [ObservableProperty]
    public partial double RadiusMiles { get; set; } = 50;

    [ObservableProperty]
    public partial string Status { get; set; } = "Starting WebView2...";

    public event Action<double>? RadiusChanged;

    partial void OnRadiusMilesChanged(double value) => RadiusChanged?.Invoke(value);
}
