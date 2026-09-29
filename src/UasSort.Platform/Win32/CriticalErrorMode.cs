namespace UasSort.Platform.Win32;

/// <summary>
/// Keeps the system's critical-error dialogs away from this process: a card pulled mid-scan, mid-Commit or mid-cleanup from a slot
/// that keeps its drive letter must fail the call (and stop the run as CardRemoved), never block a thread on "There is no disk in
/// the drive". Program.Main (App and CLI) calls <see cref="FailQuietly"/> first thing, next to PlaceholderMode.ExposePlaceholders.
/// </summary>
public static class CriticalErrorMode
{
    public const uint SemFailCriticalErrors = 0x0001;
    public const uint SemNoOpenFileErrorBox = 0x8000;

    /// <summary>Adds SEM_FAILCRITICALERRORS | SEM_NOOPENFILEERRORBOX to the process error mode; returns the previous mode.</summary>
    public static uint FailQuietly() => NativeMethods.SetErrorMode(NativeMethods.GetErrorMode() | SemFailCriticalErrors | SemNoOpenFileErrorBox);

    public static uint Current() => NativeMethods.GetErrorMode();
}