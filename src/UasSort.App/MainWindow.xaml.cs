using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using UasSort.App.Pages;
using UasSort.Platform.Win32;
using Windows.Graphics;

namespace UasSort.App;

public sealed partial class MainWindow : Window
{
    private readonly DateTime _processStartUtc;
    private readonly TaskCompletionSource<double> _firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal MainWindow(DateTime processStartUtc, string singleInstanceMechanism)
    {
        _processStartUtc = processStartUtc;
        InitializeComponent();
        Title = $"uas-sort (stack proof, single instance: {singleInstanceMechanism})";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // The TitleBar control copies its own Title ("uas-sort") onto the HWND when it loads; restore the full one.
        var hwndTitle = Title;
        AppTitleBar.Loaded += (_, _) => Title = hwndTitle;
        CompositionTarget.Rendering += OnFirstRendering;
        RootFrame.Navigate(typeof(StackProbePage));
    }

    /// <summary>Milliseconds from process start to the first CompositionTarget.Rendering (Ref §13 firstFrameMs).</summary>
    internal Task<double> FirstFrameMs => _firstFrame.Task;

    internal StackProbePage? ProbePage => RootFrame.Content as StackProbePage;

    /// <summary>--selftest launches off-screen and never activates the window (Ref §13 Isolation).</summary>
    internal void ShowOffScreen()
    {
        AppWindow.Move(new PointInt32(-6000, -6000));
        AppWindow.Show(false);
    }

    internal void BringToFront() => ForegroundWindow.BringToFront(Win32Interop.GetWindowFromWindowId(AppWindow.Id));

    private void OnFirstRendering(object? sender, object e)
    {
        CompositionTarget.Rendering -= OnFirstRendering;
        _firstFrame.TrySetResult((TimeProvider.System.GetUtcNow().UtcDateTime - _processStartUtc).TotalMilliseconds);
    }
}
