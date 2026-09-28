using Microsoft.UI.Xaml;

namespace UasSpike.App;

public partial class App : Application
{
    public static Window Window { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => SpikeLog.Write("UNHANDLED " + e.Exception);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        SpikeLog.Write($"OnLaunched: .NET {Environment.Version}, OS {Environment.OSVersion.Version}, WinAppSDK self-contained");
        Window = new MainWindow();
        Window.Activate();
        SpikeLog.Write("Window activated");
    }
}
