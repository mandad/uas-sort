using Microsoft.UI.Xaml;
namespace Spike.App;
public partial class App : Application
{
    public static void Log(string s)
    {
        var p = Environment.GetEnvironmentVariable("SPIKE_PROBE_OUT");
        if (!string.IsNullOrEmpty(p)) File.AppendAllText(p + ".log", s + Environment.NewLine);
    }
    public App()
    {
        UnhandledException += (_, e) => { Log("UNHANDLED: " + e.Exception + " | " + e.Message); };
        InitializeComponent();
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try { new MainWindow().Start(); }
        catch (Exception ex) { Log("LAUNCH: " + ex); Exit(); }
    }
}
