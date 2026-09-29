namespace UasSort.Platform.Tests.Win32;

public sealed class WindowMessageHookTests
{
    [Fact]
    public void Attach_ReceivesDeviceChange_AndDisposeStopsIt()
    {
        var hwnd = WindowInterop.CreateMessageOnlyWindow();
        try
        {
            var seen = new List<(nint W, nint L)>();
            var hook = WindowMessageHook.Attach(hwnd, WindowMessageHook.WM_DEVICECHANGE, (w, l) => seen.Add((w, l)));
            WindowInterop.Send(hwnd, WindowMessageHook.WM_DEVICECHANGE, WindowMessageHook.DBT_DEVICEARRIVAL, 0);
            WindowInterop.Send(hwnd, 0x0400 /* WM_USER: another message, ignored */, 0, 0);
            Assert.Equal([(WindowMessageHook.DBT_DEVICEARRIVAL, (nint)0)], seen);

            hook.Dispose();
            WindowInterop.Send(hwnd, WindowMessageHook.WM_DEVICECHANGE, WindowMessageHook.DBT_DEVICEREMOVECOMPLETE, 0);
            Assert.Single(seen);
        }
        finally { WindowInterop.DestroyWindow(hwnd); }
    }
}
