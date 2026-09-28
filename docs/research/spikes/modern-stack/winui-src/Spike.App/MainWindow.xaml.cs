using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Spike.Core;
namespace Spike.App;
public sealed partial class MainWindow : Window
{
    public MainVm Vm { get; } = new();
    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
    }
    public void Start()
    {
        var probe = Environment.GetEnvironmentVariable("SPIKE_PROBE_OUT");
        if (string.IsNullOrEmpty(probe)) { Activate(); return; }
        App.Log("start");
        AppWindow.Move(new Windows.Graphics.PointInt32(-6000, -6000));
        long loadedMs = -1;
        Root.Loaded += (_, _) => loadedMs = Elapsed();
        EventHandler<object>? h = null;
        h = (_, _) =>
        {
            CompositionTarget.Rendering -= h;
            var frame = Elapsed();
            App.Log("frame " + frame);
            bool bound = false;
            var t = DispatcherQueue.CreateTimer(); t.Interval = TimeSpan.FromMilliseconds(700); t.IsRepeating = false;
            t.Tick += (_, _) =>
            {
                App.Log("tick " + Elapsed());
                if (!bound) { bound = true; Bindings.Update(); App.Log("bound " + Elapsed()); t.Start(); return; } // Window x:Bind normally initializes on Activated; probe avoids Activate()
                if (Environment.GetEnvironmentVariable("SPIKE_TABLE_CODE") == "1" && Table.ItemsSource is null)
                {
                    var rows = Environment.GetEnvironmentVariable("SPIKE_TABLE_N") is { } nstr ? Vm.Rows.Take(int.Parse(nstr)).ToList() : Vm.Rows.ToList();
                    App.Log("set table source n=" + rows.Count); Table.ItemsSource = Environment.GetEnvironmentVariable("SPIKE_TABLE_OC") == "1" ? Vm.Rows : rows; App.Log("set done " + Elapsed()); t.Start(); return;
                }
                File.WriteAllText(probe, $"loaded={loadedMs};firstFrame={frame};tree={Tree.RootNodes.Count + Vm.Groups.Count};table={Table.Columns.Count};tableCells={Count(Table, P)};thumbs={Count(Thumbs, P)};checks={CountChecks(Table)};theme={Application.Current.RequestedTheme};ws={Environment.WorkingSet / 1048576}MB;root={Root.ActualWidth}x{Root.ActualHeight};tableH={Table.ActualHeight};thumbsH={Thumbs.ActualHeight};vis={AppWindow.IsVisible};pos={AppWindow.Position.X},{AppWindow.Position.Y};size={AppWindow.Size.Width}x{AppWindow.Size.Height};tb={Count(Root, "")}");
                Application.Current.Exit();
            };
            t.Start();
        };
        CompositionTarget.Rendering += h;
        AppWindow.Show(false);
    }
    const string P = "DJI_";
    static int Count(DependencyObject root, string prefix)
    {
        int n = 0;
        if (root is Microsoft.UI.Xaml.Controls.TextBlock tb && tb.Text.StartsWith(prefix)) n++;
        if (root is Microsoft.UI.Xaml.Controls.TextBox tx && tx.Text.StartsWith(prefix)) n++;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) n += Count(VisualTreeHelper.GetChild(root, i), prefix);
        return n;
    }
    static int CountChecks(DependencyObject root)
    {
        int n = 0;
        if (root is Microsoft.UI.Xaml.Controls.CheckBox cb && cb.IsChecked == true) n++;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) n += CountChecks(VisualTreeHelper.GetChild(root, i));
        return n;
    }
    static long Elapsed() => (long)(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
}
