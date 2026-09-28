namespace UasSort.Platform.Win32;

/// <summary>Bringing the first instance forward (Ref §4.4 step 1).</summary>
public static class ForegroundWindow
{
    /// <summary>Called by the second instance before it redirects activation to <paramref name="processId"/>.</summary>
    public static bool AllowSetForeground(uint processId) => NativeMethods.AllowSetForegroundWindow(processId);

    /// <summary>Called by the main instance (on its UI thread) when activation is redirected to it.</summary>
    public static bool BringToFront(nint hwnd)
    {
        if (NativeMethods.IsIconic(hwnd))
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SwRestore);
        }

        return NativeMethods.SetForegroundWindow(hwnd);
    }
}
