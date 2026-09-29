using System.Runtime.InteropServices;

namespace UasSort.Platform.Win32;

internal static partial class Shell32
{
    [LibraryImport("shell32.dll", EntryPoint = "SHGetKnownFolderPath")]
    public static partial int SHGetKnownFolderPath(in Guid rfid, uint dwFlags, nint hToken, out nint ppszPath);
}
