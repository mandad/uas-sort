// src/UasSort.App/Controls/InfoBarList.xaml.cs
using Microsoft.UI.Xaml.Controls;

namespace UasSort.App.Controls;

public sealed partial class InfoBarList : UserControl
{
    private ObservableCollection<InfoBarVm>? _items;
    public InfoBarList() => InitializeComponent();
    public ObservableCollection<InfoBarVm>? Items { get => _items; set { _items = value; Bindings.Update(); } }

    /// <summary>Called with InfoBarVm.Key when the user closes a bar (the Review page passes ReviewVm.CloseInfoBar).</summary>
    public Action<string>? Close { get; set; }

    private void OnBarClosed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        if (sender.DataContext is InfoBarVm bar) Close?.Invoke(bar.Key);
    }
}
