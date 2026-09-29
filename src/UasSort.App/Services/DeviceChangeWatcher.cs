// src/UasSort.App/Services/DeviceChangeWatcher.cs — Ref §4.2 App, §14 step 11 (UNVERIFIED): refresh on card arrival/removal
using Microsoft.UI.Dispatching;

namespace UasSort.App.Services;

public sealed partial class DeviceChangeWatcher : IDisposable   // partial: CsWinRT1028 (IDisposable projects as IClosable)
{
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(750);   // a card arrival sends several broadcasts

    private readonly ShellVm _shell;
    private readonly DispatcherQueueTimer _timer;
    private readonly IDisposable _hook;

    public DeviceChangeWatcher(nint hwnd, DispatcherQueue ui, ShellVm shell)
    {
        _shell = shell;
        _timer = ui.CreateTimer();
        _timer.Interval = Debounce;
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => App.Guarded(Fire);
        _hook = WindowMessageHook.Attach(hwnd, WindowMessageHook.WM_DEVICECHANGE, (wParam, _) =>
        {
            if (wParam != WindowMessageHook.DBT_DEVICEARRIVAL && wParam != WindowMessageHook.DBT_DEVICEREMOVECOMPLETE) return;
            Notifications++;
            _timer.Stop();                                      // the subclass proc runs on the UI thread: restart the debounce
            _timer.Start();
        });
    }

    public int Notifications { get; private set; }
    public int Refreshes { get; private set; }
    public int Suppressed { get; private set; }

    /// <summary>Commit (Preflight, Copy) and Cleanup never see a device refresh (Ref §9.1).</summary>
    public static bool IsSuppressed(Stage stage) => stage is Stage.Preflight or Stage.Copy or Stage.Cleanup;

    private void Fire()
    {
        if (IsSuppressed(_shell.Stage)) { Suppressed++; return; }
        Refreshes++;
        _shell.DeviceChanged();                                 // Card stage: re-detect; elsewhere: cleanup availability
    }

    public void Dispose()
    {
        _timer.Stop();
        _hook.Dispose();
    }
}
