using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using UasSort.Platform.Win32;

namespace UasSort.App;

/// <summary>
/// Ref §4.4 step 1 (DISABLE_XAML_GENERATED_MAIN). Command line: none (normal launch), or
/// <c>--selftest --result &lt;path&gt; [--only &lt;check&gt;[,&lt;check&gt;…]]</c>, or <c>--single-instance-mutex</c>;
/// anything else exits 2.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        DateTime processStartUtc;
        using (var self = Process.GetCurrentProcess())
        {
            processStartUtc = self.StartTime.ToUniversalTime();
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        var previousMode = PlaceholderMode.ExposePlaceholders();
        _ = CriticalErrorMode.FailQuietly();               // a pulled card fails the call, never a "no disk" system dialog
        if (previousMode < 0)
        {
            Trace.WriteLine("RtlSetProcessPlaceholderCompatibilityMode failed: " + previousMode);
        }

        // --selftest --result <path> [--only <check>[,<check>…]] | --single-instance-mutex
        LaunchOptions options;
        try
        {
            options = LaunchOptions.Parse(args, processStartUtc);
        }
        catch (ArgumentException ex)
        {
            Trace.WriteLine("uas-sort: " + ex.Message);
            return 2;
        }

        SingleInstanceGate? gate = null;
        if (!options.SelfTest)
        {
            gate = SingleInstanceGate.Claim(options.ForceMutex);
            if (!gate.IsMain)
            {
                gate.Dispose();
                return 0;
            }
        }

        try
        {
            // A lone `_` is a named lambda parameter (not a discard), so the discard below needs another name here.
            Application.Start(startParams =>
            {
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                _ = new App(options, gate);
            });
        }
        finally
        {
            gate?.Dispose();
        }

        return 0;
    }
}
