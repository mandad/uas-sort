using System.Runtime.InteropServices;

namespace UasSort.Platform.Win32;

/// <summary>Window-level Win32 calls the App needs besides the foreground ones (those are Part 01's ForegroundWindow).</summary>
public static partial class WindowInterop
{
    private static readonly nint HWND_MESSAGE = -3;

    public static nint Send(nint hwnd, uint msg, nint wParam, nint lParam) => SendMessageW(hwnd, msg, wParam, lParam);

    /// <summary>A message-only STATIC window on the calling thread (tests and the selftest's device-hook check).</summary>
    public static nint CreateMessageOnlyWindow()
    {
        var h = CreateWindowExW(0, "STATIC", "uas-sort-msg", 0, 0, 0, 0, 0, HWND_MESSAGE, 0, 0, 0);
        if (h == 0) throw new InvalidOperationException("CreateWindowExW failed: " + Marshal.GetLastPInvokeError());
        return h;
    }

    public static void DestroyWindow(nint hwnd) => DestroyWindowNative(hwnd);

    [LibraryImport("user32.dll")]
    private static partial nint SendMessageW(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowExW(uint exStyle, string className, string windowName, uint style,
                                                int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll", EntryPoint = "DestroyWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindowNative(nint hWnd);
}
