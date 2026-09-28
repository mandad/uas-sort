namespace UasSort.Platform.Win32;

/// <summary>Single-instance identifiers (Ref §4.4 step 1).</summary>
public static class SingleInstance
{
    public const string AppInstanceKey = "uas-sort";
    public const string MutexName = @"Local\uas-sort";
}
