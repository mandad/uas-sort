// src/UasSort.App/Pages/ReviewPage.xaml.cs
using System.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace UasSort.App.Pages;

public sealed partial class ReviewPage : Page
{
    private static readonly TimeSpan LayoutSaveDelay = TimeSpan.FromMilliseconds(500);

    private MainWindow _window = null!;
    private bool _layoutApplied;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _layoutTimer;   // qualified: Windows.System has one too
    private MapBridge? _bridge;
    private ReviewVm? _bridgeFor;

    public ReviewPage()
    {
        InitializeComponent();
        RegisterAccelerators();
        Map.FocusReturnRequested += () => ClipListSlot.FocusList();
        Map.Ready += ConnectMap;
        Map.OnlineChanged += _ => ResendMapInit();              // Ref §9.6 offline: re-send init with the new online flag
    }

    public ReviewVm Vm { get; private set; } = null!;
    public MainWindow Window => _window;
    private ShellVm Shell => _window.Services.Shell;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        var args = (StageArgs)e.Parameter;
        _window = args.Window;
        var previous = Vm;
        if (previous is not null) previous.PropertyChanged -= OnVmChanged;
        Vm = (ReviewVm)args.Vm;                                  // ShellVm.Current (= ShellVm.Review) on the Review stage
        Vm.PropertyChanged += OnVmChanged;
        Bindings.Update();                                       // a cached page gets a new ReviewVm after each rescan
        ReviewInfoBars.Items = Vm.InfoBars;
        ReviewInfoBars.Close = Vm.CloseInfoBar;
        Tuning.Review = Vm;
        Footer.Review = Vm;
        OnReviewAttached(previous);
        ShowTab(Vm.SelectedTab);
        ApplyLayout();
        Map.OpenInBrowserUri = () => MapPane.OsmUri(Vm.Videos.SelectedCard?.Group?.Centroid);
        await Map.InitializeAsync(_window.Services);             // WebView2 is created when Review opens (Ref §9.6)
        ConnectMap();                                            // no-op until the page says ready (then Map.Ready calls it)
    }

    /// <summary>Hands the new ReviewVm to the child controls (previous = the ReviewVm shown before, or null).
    /// Tasks 11.11–11.14 each add their lines here.</summary>
    private void OnReviewAttached(ReviewVm? previous)
    {
        TimelineSlot.Attach(Vm, _window);
        ClipListSlot.Attach(Vm);
        PhotosSlot.Attach(Vm);
        OtherSlot.Vm = Vm.Other;
        if (previous is not null) previous.MapContextMenuRequested -= OnMapContextMenu;
        Vm.MapContextMenuRequested += OnMapContextMenu;
        TimelineSlot.ClipList = ClipListSlot;                   // so Tab moves into the clip list
        if (previous is not null) previous.FocusRenameRequested -= OnFocusRenameRequested;
        Vm.FocusRenameRequested += OnFocusRenameRequested;
    }

    /// <summary>Ref §9.12 page-level accelerators (modified keys only; F5 is a function key).</summary>
    public static readonly IReadOnlyList<(string Action, VirtualKey Key, VirtualKeyModifiers Modifiers)> AcceleratorTable =
    ((string Action, VirtualKey Key, VirtualKeyModifiers Modifiers)[])[   // explicit array: CsWinRT1032 (AOT) rejects a collection expression typed as IReadOnlyList
        ("undo", VirtualKey.Z, VirtualKeyModifiers.Control),
        ("redo", VirtualKey.Y, VirtualKeyModifiers.Control),
        ("redo", VirtualKey.Z, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift),
        ("mergeNext", VirtualKey.M, VirtualKeyModifiers.Control),
        ("moveToNewGroup", VirtualKey.N, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift),
        ("tab1", VirtualKey.Number1, VirtualKeyModifiers.Control),
        ("tab2", VirtualKey.Number2, VirtualKeyModifiers.Control),
        ("tab3", VirtualKey.Number3, VirtualKeyModifiers.Control),
        ("rescan", VirtualKey.F5, VirtualKeyModifiers.None),
        ("offload", VirtualKey.Enter, VirtualKeyModifiers.Control),
    ];

    private void RegisterAccelerators()
    {
        KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        foreach (var (_, key, mods) in AcceleratorTable)
        {
            var accelerator = new KeyboardAccelerator { Key = key, Modifiers = mods };
            accelerator.Invoked += (_, e) => e.Handled = HandleReviewKey(key, mods, FocusManager.GetFocusedElement(XamlRoot));
            KeyboardAccelerators.Add(accelerator);
        }
    }

    /// <summary>Runs one accelerator of the table; false lets the key through (Ctrl+Z/Y in a text box are the box's own undo).</summary>
    public bool TryHandleAccelerator(string action, object? focused)
    {
        var (_, key, mods) = AcceleratorTable.First(a => string.Equals(a.Action, action, StringComparison.Ordinal));
        return HandleReviewKey(key, mods, focused);
    }

    /// <summary>ReviewVm.HandleKey decides (Part 10): undo/redo, merge, move, tabs, F5 rescan (RescanRequested), Ctrl+Enter offload.</summary>
    private bool HandleReviewKey(VirtualKey key, VirtualKeyModifiers mods, object? focused) =>
        Vm is not null && KeyRouting.ToReviewKey(key) is { } k && Vm.HandleKey(k, KeyRouting.ToMods(mods), KeyRouting.FocusOf(focused));

    /// <summary>ReviewVm.FocusRenameRequested (F2 on a card, or the "Name it" quick fix): select the card, then focus its rename box.</summary>
    private void OnFocusRenameRequested(ItemId anchor)
    {
        if (Vm?.Videos.CardFor(anchor) is not { } card) return;
        Vm.SelectedTab = 0;
        Vm.Videos.SelectedEntry = card;
        DispatcherQueue.TryEnqueue(() => App.Guarded(() => TimelineSlot.FocusRenameBox(card)));   // after the container is brought into view
    }

    // Ref §9.6: the map's contextMenu (through ReviewVm.MapContextMenuRequested) → a WinUI MenuFlyout at the pointer
    // (CSS px = DIPs at WebView2 zoom 1). The card holding the clicked dots is selected and the dots become the clip selection
    // first (the clip list follows through ClipSelectionChanged); no menu opens when no card holds them.
    private void OnMapContextMenu(MapContextMenu menu)
    {
        if (Vm is null) return;
        var ids = Vm.Videos.OnMapContextMenu(menu);
        if (ids.Count == 0) return;
        var row = Vm.Videos.Clips.FirstOrDefault(r => ids.Contains(r.Id));
        ClipMenu.Build(Vm, row).ShowAt(Map, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
        {
            Position = new Windows.Foundation.Point(menu.X, menu.Y),
        });
    }

    /// <summary>Registry Part 11 item 14: one MapBridge per ReviewVm; init once the pane is ready, then review.Map = bridge,
    /// which sends setData (Ref §9.6 Sync). Selection, radius and base messages then come from the ReviewVm itself.</summary>
    private void ConnectMap()
    {
        if (!Map.IsReady || Vm is null || ReferenceEquals(_bridgeFor, Vm)) return;
        if (_bridgeFor is not null && ReferenceEquals(_bridgeFor.Map, _bridge)) _bridgeFor.Map = null;
        _bridge?.Dispose();
        var services = _window.Services;
        var bridge = new MapBridge(json => Map.PostJson(json), new FileReviewLog(services.Platform.Log), services.Platform.Clock);
        Map.Attach(bridge);
        _bridge = bridge;
        _bridgeFor = Vm;
        SendMapInit();
        Vm.Map = bridge;
    }

    /// <summary>init starts a new map session (map.js clears its data), so a re-init on the same bridge sends the plan again.</summary>
    private void ResendMapInit()
    {
        SendMapInit();
        if (_bridge is not null && Vm is not null && ReferenceEquals(_bridgeFor, Vm)) _bridge.SendData(Vm.Plan);
    }

    private void SendMapInit()
    {
        if (_bridge is null || Vm is null) return;
        var map = Shell.Settings.Map with { Base = Vm.MapBase };
        _bridge.Send(MapProjection.Init(map, Vm.Tuning.RadiusMiles, MapPane.IsOnline(), Map.ThemeName == "dark"));
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReviewVm.SelectedTab)) ShowTab(Vm.SelectedTab);
    }

    private void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        int index = sender.Items.IndexOf(sender.SelectedItem);
        if (index >= 0 && Vm is not null && Vm.SelectedTab != index) Vm.SelectedTab = index;
        ShowTab(index);
    }

    public void ShowTab(int index)
    {
        if (index < 0 || index > 2) return;
        if (Tabs.SelectedItem != Tabs.Items[index]) Tabs.SelectedItem = Tabs.Items[index];
        VideosContent.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        PhotosSlot.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        OtherSlot.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    // Ref §9.3: pane sizes come from Settings.Layout and are saved through ShellVm.UpdateLayout once the splitters rest.
    private void ApplyLayout()
    {
        var layout = Shell.Settings.Layout;
        TimelineColumn.Width = new GridLength(Math.Max(280, layout.TimelineWidth));
        var ratio = Math.Clamp(layout.MapHeightRatio, 0.15, 0.85);
        MapRow.Height = new GridLength(ratio, GridUnitType.Star);
        ClipRow.Height = new GridLength(1 - ratio, GridUnitType.Star);
        _layoutApplied = true;
    }

    private void OnPaneSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_layoutApplied || Vm is null) return;
        if (_layoutTimer is null)
        {
            _layoutTimer = DispatcherQueue.CreateTimer();
            _layoutTimer.Interval = LayoutSaveDelay;
            _layoutTimer.IsRepeating = false;
            _layoutTimer.Tick += (_, _) => App.Guarded(SaveLayout);
        }
        _layoutTimer.Stop();
        _layoutTimer.Start();
    }

    private void SaveLayout()
    {
        double mapH = Map.ActualHeight, clipH = ClipListSlot.ActualHeight, width = Math.Round(TimelineSlot.ActualWidth);
        if (mapH + clipH <= 0 || width <= 0) return;
        var ratio = Math.Round(mapH / (mapH + clipH), 3);
        var current = Shell.Settings.Layout;
        if (Math.Abs(current.TimelineWidth - width) < 1 && Math.Abs(current.MapHeightRatio - ratio) < 0.005) return;
        Shell.UpdateLayout(width, ratio);                        // saves Settings with { Layout = new(width, ratio) }
    }
}
