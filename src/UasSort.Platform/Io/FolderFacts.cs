namespace UasSort.Platform.Io;

/// <summary>Whether a folder exists, from its attributes only (never opened).</summary>
public static class FolderFacts
{
    public static bool Exists(string path)
        => !string.IsNullOrWhiteSpace(path)
           && Kernel32.TryGetAttributes(Path.GetFullPath(path), out _) is uint a && (a & Kernel32.FILE_ATTRIBUTE_DIRECTORY) != 0;
}
