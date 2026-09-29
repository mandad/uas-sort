using UasSort.Core;

namespace UasSort.Core.Config;

public static class SettingsEdits
{
    /// <summary>Ref §7.1: when the photo root changes, the old one is appended to previousPhotoRoots automatically.</summary>
    public static Settings ChangePhotoRoot(Settings s, string newPhotoRoot)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(newPhotoRoot);
        if (PathRules.Equal(newPhotoRoot, s.PhotoRoot)) return s;
        var previous = s.PreviousPhotoRoots.Where(p => !PathRules.Equal(p, newPhotoRoot)).ToList();
        if (!previous.Any(p => PathRules.Equal(p, s.PhotoRoot))) previous.Add(s.PhotoRoot);
        return s with { PhotoRoot = PathRules.Normalize(newPhotoRoot), PreviousPhotoRoots = [.. previous] };
    }
}
