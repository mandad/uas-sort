// src/UasSort.App/Pages/ShellPage.xaml.cs — the ShellVm stage machine drives the Frame (Ref §9.1)
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace UasSort.App.Pages;

public sealed partial class ShellPage : Page
{
    /// <summary>Stage → page type. Tasks 11.5–11.16 each add their line.</summary>
    public static readonly Dictionary<Stage, Type> StagePages = new()
    {
        [Stage.Setup] = typeof(SetupPage),
        [Stage.Settings] = typeof(SettingsPage),
        [Stage.Card] = typeof(CardPage),
        [Stage.Scan] = typeof(ScanPage),
        [Stage.Review] = typeof(ReviewPage),
        [Stage.Preflight] = typeof(PreflightPage),
        [Stage.Copy] = typeof(CopyPage),
        [Stage.Verdict] = typeof(VerdictPage),
        [Stage.Cleanup] = typeof(CleanupPage),
    };

    private MainWindow _window = null!;
    private object? _shown;
    private ReviewVm? _review;

    public ShellPage() => InitializeComponent();

    public ShellVm Vm { get; private set; } = null!;

    public MainWindow Window => _window;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _window = (MainWindow)e.Parameter;
        Vm = _window.Services.Shell;
        Vm.PropertyChanged += OnShellChanged;
        Loaded += (_, _) => _window.SetTitleBar(AppTitleBar);
        Show();
    }

    // ShellVm.Go sets Stage first and Current second, so the frame follows Current (the pair is consistent then).
    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellVm.Current)) Show();
        else if (e.PropertyName == nameof(ShellVm.CanUndoRedo)) UpdateUndoButtons();
    }

    /// <summary>Shows ShellVm.Current in the page registered for ShellVm.Stage; the page casts StageArgs.Vm.</summary>
    private void Show()
    {
        WatchReview(Vm.Review);
        UpdateUndoButtons();
        if (Vm.Current is not { } vm || ReferenceEquals(vm, _shown)) return;
        if (!StagePages.TryGetValue(Vm.Stage, out var page)) return;
        _shown = vm;
        StageFrame.Navigate(page, new StageArgs(vm, _window));
    }

    private void WatchReview(ReviewVm? review)
    {
        if (ReferenceEquals(review, _review)) return;
        if (_review is not null) _review.PropertyChanged -= OnReviewChanged;
        _review = review;
        if (review is null) return;
        review.PropertyChanged += OnReviewChanged;
        _window.Services.Thumbnails.Clear();                    // a new scan: ItemIds may repeat with other bytes
    }

    private void OnReviewChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ReviewVm.CanUndo) or nameof(ReviewVm.CanRedo) or nameof(ReviewVm.IsReadOnly))
            UpdateUndoButtons();
    }

    private void UpdateUndoButtons()
    {
        var review = Vm.CanUndoRedo ? Vm.Review : null;
        UndoButton.IsEnabled = review is { CanUndo: true, IsReadOnly: false };
        RedoButton.IsEnabled = review is { CanRedo: true, IsReadOnly: false };
    }

    private void OnUndo(object sender, RoutedEventArgs e)
    {
        if (Vm.CanUndoRedo && Vm.Review is { } review) Observed.Forget(review.UndoCommand.ExecuteAsync(null), App.ReportFault);
    }

    private void OnRedo(object sender, RoutedEventArgs e)
    {
        if (Vm.CanUndoRedo && Vm.Review is { } review) Observed.Forget(review.RedoCommand.ExecuteAsync(null), App.ReportFault);
    }
}

/// <summary>What every stage page receives (defined here): ShellVm.Current and the window.</summary>
public sealed record StageArgs(object Vm, MainWindow Window);
