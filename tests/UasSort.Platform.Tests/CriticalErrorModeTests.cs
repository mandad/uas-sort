namespace UasSort.Platform.Tests;

public sealed class CriticalErrorModeTests
{
    private const uint SemNoGpFaultErrorBox = 0x0002;

    [Fact] // deferred minor 0910fixP: a card pulled mid-flow never raises the system "There is no disk in the drive" dialog
    public void FailQuietly_SetsFailCriticalErrorsAndNoOpenFileErrorBox_KeepingOtherFlags()
    {
        var saved = NativeMethods.SetErrorMode(SemNoGpFaultErrorBox);   // a known starting mode (the test host may set its own)
        try
        {
            Assert.Equal(SemNoGpFaultErrorBox, CriticalErrorMode.FailQuietly());
            Assert.Equal(CriticalErrorMode.SemFailCriticalErrors | CriticalErrorMode.SemNoOpenFileErrorBox | SemNoGpFaultErrorBox,
                         CriticalErrorMode.Current());
        }
        finally
        {
            _ = NativeMethods.SetErrorMode(saved);
        }
    }
}