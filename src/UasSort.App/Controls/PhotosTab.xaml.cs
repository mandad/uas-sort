// src/UasSort.App/Controls/PhotosTab.xaml.cs
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace UasSort.App.Controls;

public sealed partial class PhotosTab : UserControl
{
    private ReviewVm? _review;
    private bool _syncing;

    public PhotosTab() => InitializeComponent();

    public PhotosTabVm? Vm { get; private set; }

    public void Attach(ReviewVm review)
    {
        if (Vm is not null) Vm.PropertyChanged -= OnVmChanged;
        _review = review;
        Vm = review.Photos;
        Bindings.Update();
        DayList.ItemsSource = Vm.Days;
        Wall.ItemsSource = Vm.Tiles;
        Vm.PropertyChanged += OnVmChanged;
        SyncDay();
    }

    public ItemContainer? ContainerFor(PhotoTileVm tile) =>
        VisualTree.FindDescendant<ItemContainer>(Wall, c => ReferenceEquals(c.DataContext, tile));

    public PhotoTileVm? FocusedTile() =>
        VisualTree.FindAncestor<ItemContainer>(FocusManager.GetFocusedElement(XamlRoot) as DependencyObject)?.DataContext as PhotoTileVm;

    private void OnDaySelectionChanged(ItemsView sender, ItemsViewSelectionChangedEventArgs args)
    {
        if (!_syncing && Vm is not null) Vm.SelectedDay = sender.SelectedItem as PhotoDayVm;
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PhotosTabVm.SelectedDay)) SyncDay();
    }

    private void SyncDay()
    {
        if (Vm is null) return;
        _syncing = true;
        try
        {
            int i = Vm.SelectedDay is { } d ? Vm.Days.IndexOf(d) : -1;
            if (i < 0) DayList.DeselectAll();
            else if (!ReferenceEquals(DayList.SelectedItem, Vm.SelectedDay)) DayList.Select(i);
        }
        finally { _syncing = false; }
    }
}
