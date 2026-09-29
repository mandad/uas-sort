// src/UasSort.App/Controls/TimelineView.xaml.cs
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace UasSort.App.Controls;

public sealed partial class TimelineView : UserControl
{
    private ReviewVm? _review;
    private MainWindow? _window;
    private bool _syncing;

    public TimelineView() => InitializeComponent();

    public void Attach(ReviewVm review, MainWindow window)
    {
        if (_review is not null) _review.Videos.PropertyChanged -= OnVideosChanged;
        _review = review;
        _window = window;
        Items.ItemsSource = review.Videos.Timeline;       // ObservableCollection<TimelineEntryVm> (Ref §2.7 #4)
        review.Videos.PropertyChanged += OnVideosChanged;
        SyncSelectionFromVm();
    }

    public ItemContainer? ContainerFor(TimelineEntryVm vm) =>
        VisualTree.FindDescendant<ItemContainer>(Items, c => ReferenceEquals(c.DataContext, vm));

    private void OnSelectionChanged(ItemsView sender, ItemsViewSelectionChangedEventArgs args)
    {
        if (_syncing || _review is null) return;
        _review.Videos.SelectedEntry = sender.SelectedItem as TimelineEntryVm;
    }

    private void OnVideosChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VideosTabVm.SelectedEntry)) SyncSelectionFromVm();
    }

    private void SyncSelectionFromVm()
    {
        if (_review is null) return;
        var videos = _review.Videos;
        _syncing = true;
        try
        {
            int index = videos.SelectedEntry is { } s ? videos.Timeline.IndexOf(s) : -1;
            if (index < 0) Items.DeselectAll();
            else if (!ReferenceEquals(Items.SelectedItem, videos.SelectedEntry))
            {
                Items.Select(index);
                Items.StartBringItemIntoView(index, new BringIntoViewOptions());
            }
        }
        finally { _syncing = false; }
    }

    /// <summary>F2 (Ref §9.4, §9.12): focus the selected card's rename box.</summary>
    public bool FocusRenameBox() => _review?.Videos.SelectedCard is { } card && FocusRenameBox(card);

    /// <summary>ReviewVm.FocusRenameRequested ("Name it" quick fix, F2): focus this card's rename box.</summary>
    public bool FocusRenameBox(GroupCardVm card)
    {
        if (ContainerFor(card) is not { } c) return false;
        var box = VisualTree.FindDescendant<AutoSuggestBox>(c, b => b.Name == "DescriptionBox" && b.Visibility == Visibility.Visible);
        return box?.Focus(FocusState.Keyboard) ?? false;
    }

    // ── rename (Ref §9.4): a chosen suggestion only fills the box (UpdateTextOnSelect=False, never ToString of a record);
    //    QuerySubmitted (Enter or a clicked suggestion) and LostFocus commit: set Description, then CommitDescriptionCommand ──
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "XAML event handler: the generated Connect code wires it as this.OnSuggestionChosen, so it must be an instance method.")]
    private void OnSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is SuggestionVm s) sender.Text = s.Text;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "XAML event handler: the generated Connect code wires it as this.OnQuerySubmitted, so it must be an instance method.")]
    private async void OnQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (sender.DataContext is GroupCardVm card)
            await CommitAsync(card, args.ChosenSuggestion is SuggestionVm s ? s.Text : args.QueryText);
    }

    private async void OnDescriptionLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is AutoSuggestBox box && box.DataContext is GroupCardVm card && box.Text != card.Description)
            await CommitAsync(card, box.Text);
    }

    private static Task CommitAsync(GroupCardVm card, string text)
    {
        card.Description = text;
        return card.CommitDescriptionCommand.ExecuteAsync(null);    // ReviewVm.RenameAsync(card, Description)
    }

    private void OnShowPhotos(object sender, RoutedEventArgs e)
    {
        if (_review is not null) _review.SelectedTab = 1;
    }

    private void OnToggleFold(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FoldedRunVm run) _review?.Videos.ToggleFold(run);
    }

    // ── retarget menu, built in code-behind on Opening from GroupCardVm.RetargetOptions() (Ref §9.4 item 4) ──
    private void OnTargetMenuOpening(object sender, object e)
    {
        var flyout = (MenuFlyout)sender;
        if ((flyout.Target as FrameworkElement)?.DataContext is GroupCardVm card) BuildTargetMenu(flyout, card);
    }

    /// <summary>Auto and New folder, then the append candidates (Planner.AppendCandidates), then Browse existing… and Skip,
    /// with a separator between the three sections; a click calls ReviewVm.RetargetAsync(card, option).</summary>
    public void BuildTargetMenu(MenuFlyout flyout, GroupCardVm card)
    {
        flyout.Items.Clear();
        int? section = null;
        foreach (var option in card.RetargetOptions())
        {
            var next = option.Kind switch { RetargetKind.Auto or RetargetKind.NewFolder => 0, RetargetKind.Append => 1, _ => 2 };
            if (section is { } s && s != next) flyout.Items.Add(new MenuFlyoutSeparator());
            section = next;
            var item = new MenuFlyoutItem { Text = string.IsNullOrEmpty(option.Detail) ? option.Label : option.Label + "  ·  " + option.Detail };
            if (option.Kind == RetargetKind.Browse) item.Click += async (_, _) => await BrowseExistingAsync(card);
            else item.Click += async (_, _) => { if (_review is not null) await _review.RetargetAsync(card, option); };
            flyout.Items.Add(item);
        }
    }

    /// <summary>Browse existing…: a FolderPicker starting in the video root; ReviewVm.BrowseRetargetAsync refuses a folder outside
    /// it, in .uas-sort, or in a photo root (RetargetIntoReservedFolder, Ref §8.9) and says why in LastError.</summary>
    private async Task BrowseExistingAsync(GroupCardVm card)
    {
        if (_window is null || _review is null) return;
        if (await FolderPickerService.PickFolderAsync(_window, _review.Index.VideoRoot) is { } path)
            await _review.BrowseRetargetAsync(card, path);
    }
}
