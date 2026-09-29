using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Windows.AppLifecycle;
using UasSort.Platform.Win32;

namespace UasSort.App;

/// <summary>
/// Ref §4.4 step 1.2: AppInstance keyed "uas-sort"; a second launch lets the owner take the foreground and redirects
/// its activation on a worker thread while Main waits. If AppInstance misbehaves in the lean self-contained build
/// (UNVERIFIED), or --single-instance-mutex is given, the named mutex Local\uas-sort decides instead; a second
/// instance then simply exits.
/// </summary>
internal sealed partial class SingleInstanceGate : IDisposable
{
    private readonly NamedMutexLock? _mutex;

    private SingleInstanceGate(bool isMain, string mechanism, AppInstance? key, NamedMutexLock? mutex)
    {
        IsMain = isMain;
        Mechanism = mechanism;
        Key = key;
        _mutex = mutex;
        if (isMain && key is not null)
        {
            key.Activated += (_, _) => Activated?.Invoke();
        }
    }

    /// <summary>Raised on a thread-pool thread in the main instance when a second launch redirects its activation here.</summary>
    public event Action? Activated;

    public bool IsMain { get; }

    public string Mechanism { get; }

    public AppInstance? Key { get; }

    public static SingleInstanceGate Claim(bool forceMutex)
    {
        if (!forceMutex)
        {
            try
            {
                var key = AppInstance.FindOrRegisterForKey(SingleInstance.AppInstanceKey);
                if (key.IsCurrent)
                {
                    return new SingleInstanceGate(true, "AppInstance", key, null);
                }

                RedirectTo(key);
                return new SingleInstanceGate(false, "AppInstance", key, null);
            }
            catch (Exception ex) when (ex is COMException or TypeInitializationException or DllNotFoundException
                                           or EntryPointNotFoundException or InvalidOperationException)
            {
                Trace.WriteLine("AppInstance unavailable; using the named-mutex fallback: " + ex.Message);
            }
        }

        var mutex = NamedMutexLock.TryAcquire(SingleInstance.MutexName);
        return new SingleInstanceGate(mutex is not null, "Mutex", null, mutex);
    }

    public void Dispose() => _mutex?.Dispose();

    private static void RedirectTo(AppInstance owner)
    {
        ForegroundWindow.AllowSetForeground(owner.ProcessId);
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        var redirect = Task.Run(async () => await owner.RedirectActivationToAsync(activation));
        try
        {
            redirect.Wait(TimeSpan.FromSeconds(10));
        }
        catch (AggregateException ex)
        {
            Trace.WriteLine("Activation redirect failed: " + ex.InnerException?.Message);
        }
    }
}
