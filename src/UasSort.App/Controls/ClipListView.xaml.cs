// src/UasSort.App/Controls/ClipListView.xaml.cs
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace UasSort.App.Controls;

public sealed partial class ClipListView : UserControl
{
    private ReviewVm? _review;
    private bool _syncing;

    public ClipListView() => InitializeComponent();

    public void Attach(ReviewVm review)
    {
        if (_review is not null) _review.Videos.ClipSelectionChanged -= SyncSelectionFromVm;
        _review = review;
        Items.ItemsSource = review.Videos.Clips;
        review.Videos.ClipSelectionChanged += SyncSelectionFromVm;   // map clicks (Ctrl adds) change the selection
    }

    public ItemContainer? ContainerFor(ClipRowVm row) =>
        VisualTree.FindDescendant<ItemContainer>(Items, c => ReferenceEquals(c.DataContext, row));

    /// <summary>Selects these clips in the VM (which highlights their dots, Ref §9.6 Sync) and in the list.</summary>
    public void SelectRows(IReadOnlyList<ItemId> ids)
    {
        if (_review is null) return;
        _review.Videos.SetSelectedClips(ids);
        SyncSelectionFromVm(ids);
    }

    public void FocusList()
    {
        if (_review is null) return;
        var videos = _review.Videos;
        var target = videos.Clips.FirstOrDefault(r => videos.SelectedClipIds.Contains(r.Id)) ?? videos.Clips.FirstOrDefault();
        if (target is not null && ContainerFor(target) is { } c) c.Focus(FocusState.Programmatic);
        else Items.Focus(FocusState.Programmatic);
    }

    /// <summary>The row whose container (or a descendant that is not a text box) has keyboard focus.</summary>
    public ClipRowVm? FocusedRow()
    {
        var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        return VisualTree.FindAncestor<ItemContainer>(focused)?.DataContext as ClipRowVm;
    }

    private void OnItemsPreviewKeyDown(object sender, KeyRoutedEventArgs e) =>
        e.Handled = HandleKey(e.Key, KeyRouting.Modifiers(), FocusManager.GetFocusedElement(XamlRoot));   // synchronous: Handled counts

    /// <summary>Clip-list keys (Ref §9.12) through ReviewVm.HandleKey: Space toggles the selected clips (the focused row is
    /// selected first), Ctrl+Shift+S splits before ReviewVm.FocusedClip. A focused CheckBox keeps Space; a text box keeps every key.</summary>
    public bool HandleKey(VirtualKey key, KeyMods mods, object? focused)
    {
        if (_review is null || KeyRouting.IsCheckBox(focused)
            || KeyRouting.ToReviewKey(key) is not { } k || k is not (ReviewKey.Space or ReviewKey.S)) return false;
        var focus = KeyRouting.FocusOf(focused);
        if (focus == KeyFocus.ClipItem && VisualTree.FindAncestor<ItemContainer>(focused as DependencyObject)?.DataContext is ClipRowVm row)
        {
            _review.FocusedClip = row;
            if (k == ReviewKey.Space && !_review.Videos.SelectedClipIds.Contains(row.Id)) SelectRows((ItemId[])[row.Id]);   // explicit array: CsWinRT1032
        }
        return _review.HandleKey(k, mods, focus);
    }

    private void OnSelectionChanged(ItemsView sender, ItemsViewSelectionChangedEventArgs args)
    {
        if (_syncing || _review is null) return;
        _review.Videos.SetSelectedClips((ItemId[])[.. sender.SelectedItems.OfType<ClipRowVm>().Select(r => r.Id)]);   // highlights their dots
    }

    /// <summary>Mirrors the VM's selection (after a map click, or SelectRows) in the list.</summary>
    private void SyncSelectionFromVm(IReadOnlyList<ItemId> ids)
    {
        if (_review is null) return;
        var clips = _review.Videos.Clips;
        _syncing = true;
        try
        {
            Items.DeselectAll();
            for (int i = 0; i < clips.Count; i++)
            {
                if (!ids.Contains(clips[i].Id)) continue;
                Items.Select(i);
                Items.StartBringItemIntoView(i, new BringIntoViewOptions());
            }
        }
        finally { _syncing = false; }
    }

    private void OnContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (_review is null) return;
        var row = VisualTree.FindAncestor<ItemContainer>(args.OriginalSource as DependencyObject)?.DataContext as ClipRowVm;
        if (row is not null && !_review.Videos.SelectedClipIds.Contains(row.Id)) SelectRows((ItemId[])[row.Id]);   // explicit array: CsWinRT1032 (AOT) rejects a collection expression typed as IReadOnlyList
        var menu = ClipMenu.Build(_review, row);
        if (args.TryGetPosition(Items, out var point)) menu.ShowAt(Items, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions { Position = point });
        else menu.ShowAt(Items);
        args.Handled = true;
    }

    private void OnRowPointerEntered(object sender, PointerRoutedEventArgs e) => SetSplitButtonOpacity(sender, 1);
    private void OnRowPointerExited(object sender, PointerRoutedEventArgs e) => SetSplitButtonOpacity(sender, 0);

    private static void SetSplitButtonOpacity(object row, double opacity)
    {
        if (row is Grid g && VisualTree.FindDescendant<Button>(g, b => b.Name == "SplitBeforeButton") is { } b) b.Opacity = opacity;
    }
}
