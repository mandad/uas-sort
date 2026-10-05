using System.Runtime.InteropServices;

namespace UasSort.Platform.Win32;

internal static partial class Shell32
{
    [LibraryImport("shell32.dll", EntryPoint = "SHGetKnownFolderPath")]
    public static partial int SHGetKnownFolderPath(in Guid rfid, uint dwFlags, nint hToken, out nint ppszPath);

    /// <summary>The size and item count of a volume's Recycle Bin (read-only; RecycleBinQuery).</summary>
    [LibraryImport("shell32.dll", EntryPoint = "SHQueryRecycleBinW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial int SHQueryRecycleBin(string pszRootPath, ref ShQueryRbInfo pSHQueryRBInfo);
}

/// <summary>SHQUERYRBINFO (shellapi.h; natural alignment on x64).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ShQueryRbInfo
{
    public uint Size;
    public long BinBytes;
    public long Items;
}
