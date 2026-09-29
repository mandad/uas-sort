using System.Runtime.InteropServices;

namespace UasSort.Platform.Io;

public static class KnownFolders
{
    private static readonly Guid FolderIdPictures = new("33E28130-4E1E-4676-835A-98395C3BC3BB");

    /// <summary>SHGetKnownFolderPath(FOLDERID_Pictures): follows OneDrive folder backup (Ref §9.1 Setup).</summary>
    public static string Pictures()
    {
        var hr = Shell32.SHGetKnownFolderPath(in FolderIdPictures, 0, 0, out var p);
        try
        {
            if (hr < 0) throw Marshal.GetExceptionForHR(hr)!;
            return Marshal.PtrToStringUni(p)!;
        }
        finally { Marshal.FreeCoTaskMem(p); }
    }

    /// <summary>%LOCALAPPDATA%\uas-sort (Ref §11).</summary>
    public static string AppDataDir()
        => Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "uas-sort");

    /// <summary>The volume holding Windows, e.g. C:\ (GuardContext.SystemVolumeRoot).</summary>
    public static string SystemVolumeRoot() => Path.GetPathRoot(Environment.SystemDirectory)!;
}
