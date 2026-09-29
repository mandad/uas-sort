using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UasSort.Platform.Win32;

/// <summary>comctl32 window subclassing (SetWindowSubclass) so the App can watch one message on its top-level window
/// (Ref §14 step 11: device-arrival refresh via WM_DEVICECHANGE; UNVERIFIED). The callback is an UnmanagedCallersOnly
/// function pointer, so this is AOT-safe.</summary>
public static unsafe partial class WindowMessageHook
{
#pragma warning disable CA1707 // the Win32 names, as the registry spells them (dbt.h / winuser.h)
    public const uint WM_DEVICECHANGE = 0x0219;
    public const nint DBT_DEVICEARRIVAL = 0x8000;
    public const nint DBT_DEVICEREMOVECOMPLETE = 0x8004;
#pragma warning restore CA1707
    private const uint WM_NCDESTROY = 0x0082;

    private static readonly ConcurrentDictionary<nuint, (nint Hwnd, uint Message, Action<nint, nint> Handler)> Hooks = new();
    private static long _nextId;

    public static IDisposable Attach(nint hwnd, uint message, Action<nint, nint> handler)
    {
        var id = (nuint)Interlocked.Increment(ref _nextId);
        Hooks[id] = (hwnd, message, handler);
        if (!SetWindowSubclass(hwnd, &SubclassProc, id, 0))
        {
            Hooks.TryRemove(id, out _);
            throw new InvalidOperationException("SetWindowSubclass failed");
        }
        return new Subscription(hwnd, id);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint SubclassProc(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nuint refData)
    {
        if (Hooks.TryGetValue(id, out var h))
        {
            if (msg == h.Message)
            {
#pragma warning disable CA1031 // a handler bug must not tear down the window proc (unmanaged callback)
                try { h.Handler(wParam, lParam); } catch (Exception) { }
#pragma warning restore CA1031
            }
            else if (msg == WM_NCDESTROY)
            {
                RemoveWindowSubclass(hwnd, &SubclassProc, id);
                Hooks.TryRemove(id, out _);
            }
        }
        return DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    private sealed class Subscription(nint hwnd, nuint id) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            RemoveWindowSubclass(hwnd, &SubclassProc, id);
            Hooks.TryRemove(id, out _);
        }
    }

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowSubclass(nint hWnd, delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nuint, nuint, nint> pfn,
                                                  nuint uIdSubclass, nuint dwRefData);

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RemoveWindowSubclass(nint hWnd, delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nuint, nuint, nint> pfn,
                                                     nuint uIdSubclass);

    [LibraryImport("comctl32.dll")]
    private static partial nint DefSubclassProc(nint hWnd, uint uMsg, nint wParam, nint lParam);
}
