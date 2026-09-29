using System.ComponentModel;
using System.Runtime.InteropServices;

namespace UasSort.Platform.Shell;

/// <summary>Keeps the PC awake during Commit and Card cleanup (Ref §10.3 Invariants, §10.6 step 1).</summary>
public sealed class PowerRequest : IPowerRequest
{
    public IDisposable KeepSystemAwake(string reason) => new Held(reason);

    private sealed class Held : IDisposable
    {
        private nint _handle;
        private nint _reason;

        public Held(string reason)
        {
            _reason = Marshal.StringToHGlobalUni(reason);
            var context = new ReasonContext { Version = 0, Flags = 1, SimpleReasonString = _reason };
            _handle = Kernel32.PowerCreateRequest(in context);
            if (_handle == -1 || !Kernel32.PowerSetRequest(_handle, Kernel32.PowerRequestSystemRequired))
            {
                var error = Marshal.GetLastPInvokeError();
                Dispose();
                throw new Win32Exception(error, "PowerCreateRequest/PowerSetRequest failed");
            }
        }

        public void Dispose()
        {
            if (_handle is not (0 or -1))
            {
                Kernel32.PowerClearRequest(_handle, Kernel32.PowerRequestSystemRequired);
                Kernel32.CloseHandle(_handle);
            }
            _handle = 0;
            if (_reason != 0) Marshal.FreeHGlobal(_reason);
            _reason = 0;
        }
    }
}
