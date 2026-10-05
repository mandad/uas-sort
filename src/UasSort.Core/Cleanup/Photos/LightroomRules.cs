// src/UasSort.Core/Cleanup/Photos/LightroomRules.cs
namespace UasSort.Core.Cleanup;

/// <summary>The Lightroom library folder (Settings.LightroomFolder) and Lightroom's catalog (spec 2026-10-04 §2, §4, §5). The app only
/// ever reads the library folder, and never lists into or opens the catalog: D:\LR_Catalog, any folder named LR_Catalog, any *.lrcat*
/// entry (the catalog and its -data, -wal, -shm and .lock files) and any *.lrdata folder (Previews, Smart Previews, Helper).</summary>
public static class LightroomRules
{
    public const string CatalogFolder = @"D:\LR_Catalog";
    public const string CatalogFolderName = "LR_Catalog";

    public static bool IsCatalogName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Equals(CatalogFolderName, StringComparison.OrdinalIgnoreCase)
            || name.Contains(".lrcat", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".lrdata", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsCatalogPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var p = PathRules.Normalize(path);
        if (PathRules.IsSameOrUnder(p, CatalogFolder)) return true;
        foreach (var segment in p.Split('\\', StringSplitOptions.RemoveEmptyEntries))
            if (IsCatalogName(segment)) return true;
        return false;
    }

    /// <summary>Why <paramref name="folder"/> can't be the Lightroom library folder, or null: it must be a full path, not the catalog, not
    /// inside the photo root, the video root or a previous photo root, and not an ancestor of the photo root.</summary>
    public static string? FolderRefusal(string folder, Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (string.IsNullOrWhiteSpace(folder)) return "Pick a folder";
        var f = PathRules.Normalize(folder.Trim());
        if (!Path.IsPathFullyQualified(f)) return "Pick a folder on a drive";
        if (IsCatalogPath(f)) return "That is Lightroom's catalog folder; pick the folder that holds the imported photos";
        if (PathRules.IsSameOrUnder(f, settings.PhotoRoot)) return "That folder is inside the photo folder (Picture Offload)";
        if (PathRules.IsStrictlyUnder(settings.PhotoRoot, f)) return "That folder contains the photo folder (Picture Offload)";
        if (PathRules.IsSameOrUnder(f, settings.VideoRoot)) return "That folder is inside the video folder";
        var previousRoots = settings.PreviousPhotoRoots.IsDefault ? ImmutableArray<string>.Empty : settings.PreviousPhotoRoots;
        foreach (var previous in previousRoots)
            if (PathRules.Overlaps(f, previous)) return "That folder overlaps a previous photo folder";
        return null;
    }
}
