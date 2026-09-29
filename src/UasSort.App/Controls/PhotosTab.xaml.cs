// src/UasSort.App/Controls/PhotosTab.xaml.cs
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

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

    private void OnWallPreviewKeyDown(object sender, KeyRoutedEventArgs e) =>
        e.Handled = HandleKey(e.Key, KeyRouting.Modifiers(), FocusManager.GetFocusedElement(XamlRoot));

    /// <summary>Space on a photo tile toggles it through ReviewVm.HandleKey (ReviewVm.FocusedTile). A focused CheckBox keeps Space.</summary>
    public bool HandleKey(VirtualKey key, KeyMods mods, object? focused)
    {
        if (_review is null || key != VirtualKey.Space || KeyRouting.IsCheckBox(focused)) return false;
        var focus = KeyRouting.FocusOf(focused);
        if (focus == KeyFocus.PhotoItem && VisualTree.FindAncestor<ItemContainer>(focused as DependencyObject)?.DataContext is PhotoTileVm tile)
            _review.FocusedTile = tile;
        return _review.HandleKey(ReviewKey.Space, mods, focus);
    }

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
