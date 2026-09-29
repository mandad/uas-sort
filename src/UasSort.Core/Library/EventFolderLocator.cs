using UasSort.Core;

namespace UasSort.Core.Library;

/// <summary>Ref §7.1 Event folders: the nearest YYYY-MM-DD ancestor under the video root; photo roots and .uas-sort excluded.</summary>
public static class EventFolderLocator
{
    public static LibraryFolderRef? For(string path, string videoRoot, IReadOnlyList<string> photoRoots, bool includeSelf)
    {
        ArgumentNullException.ThrowIfNull(photoRoots);
        if (!PathRules.IsSameOrUnder(path, videoRoot) || PathRules.Equal(path, videoRoot)) return null;
        if (PathRules.IsSameOrUnder(path, LedgerPaths.For(videoRoot))) return null;
        foreach (string photoRoot in photoRoots)
        {
            if (PathRules.IsSameOrUnder(path, photoRoot)) return null;
        }

        for (string? dir = includeSelf ? PathRules.Normalize(path) : PathRules.Parent(path);
             dir is not null && PathRules.IsSameOrUnder(dir, videoRoot) && !PathRules.Equal(dir, videoRoot);
             dir = PathRules.Parent(dir))
        {
            if (EventFolderName.TryParse(PathRules.FileName(dir), out DateOnly date, out string description))
                return new LibraryFolderRef(PathRules.Normalize(dir), date, description);
        }
        return null;
    }
}
