// src/UasSort.Core/Cleanup/Photos/PhotoCleanupRules.cs
using System.Text.RegularExpressions;

namespace UasSort.Core.Cleanup;

/// <summary>Fixed rules and texts of Picture Offload cleanup (spec 2026-10-04 §3, §4).</summary>
public static partial class PhotoCleanupRules
{
    public const uint PlaceholderBits =
        IoGuardPolicy.FileAttributeOffline | IoGuardPolicy.FileAttributeRecallOnOpen | IoGuardPolicy.FileAttributeRecallOnDataAccess;

    public const string DateUnknownCloudOnly = "date unknown (cloud-only)";
    public const string CloudOnlyCantCheck = "not on this PC (cloud-only) — couldn't check";
    public const string DateModeText = "not verified (date mode)";

    /// <summary>The still extensions of Ref §5's media extensions (videos are never in Picture Offload's scope).</summary>
    public static readonly ImmutableHashSet<string> PhotoExtensions =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, ".dng", ".jpg", ".jpeg", ".heic", ".heif", ".tif", ".tiff");

    public static bool IsCloudOnly(uint attributes) => (attributes & PlaceholderBits) != 0;

    public static bool IsPhotoName(string name) => PhotoExtensions.Contains(Path.GetExtension(name));

    public static bool IsJpg(string name)
        => Path.GetExtension(name) is var e && (e.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || e.Equals(".jpeg", StringComparison.OrdinalIgnoreCase));

    public static bool IsDng(string name) => Path.GetExtension(name).Equals(".dng", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^(?<set>\d{3}_\d{4})(?: \d{4}-\d{2}-\d{2})?(?: \(\d+\))?$", RegexOptions.CultureInvariant)]
    private static partial Regex SetFolderPattern();

    /// <summary>A set folder the offload creates (Ref §8.8): the card set name, then an optional " yyyy-MM-dd" and " (n)".</summary>
    public static bool LooksLikeSetFolder(string folderName) => SetFolderPattern().IsMatch(folderName);

    public static string SetNameOf(string folderName)
        => SetFolderPattern().Match(folderName) is { Success: true } m ? m.Groups["set"].Value : folderName;

    /// <summary>A stitched panorama's shape (resolved ambiguity 6): the longer side at least this many times the shorter one.</summary>
    public const double PanoramaAspect = 2.0;

    public static (int Width, int Height)? PixelsOf(StillInfo? info)
        => info is { PixelWidth: { } w, PixelHeight: { } h } && w > 0 && h > 0 ? (w, h) : null;

    /// <summary>Branch-2 ruling: the Recycle Bin is the safety net, so a run is refused before any move when Windows would remove files at
    /// once (NukeOnDelete), or when the bytes chosen plus what the bin already holds exceed its maximum size (Windows would then purge
    /// its oldest items, this run's first photos among them). Null: the bin can hold the run.</summary>
    public static string? RecycleBinRefusal(RecycleBinCapacity bin, long neededBytes)
    {
        ArgumentNullException.ThrowIfNull(bin);
        if (bin.NukeOnDelete)
            return $"The Recycle Bin on {bin.Volume} is set to remove files immediately (\"Don't move files to the Recycle Bin\"); this cleanup "
                   + "needs it to keep them — change that in the Recycle Bin's properties first. Nothing was moved.";
        if (neededBytes + bin.UsedBytes <= bin.MaxBytes) return null;
        return $"The Recycle Bin on {bin.Volume} holds {CleanupFormat.Gb(bin.MaxBytes)} ({CleanupFormat.Gb(bin.UsedBytes)} already in it); "
               + $"this cleanup needs {CleanupFormat.Gb(neededBytes)} — choose an earlier cutoff or empty the Recycle Bin first. Nothing was moved.";
    }

    public static bool LooksLikePanorama((int Width, int Height)? pixels)
        => pixels is { } p && p.Width > 0 && p.Height > 0 && Math.Max(p.Width, p.Height) >= PanoramaAspect * Math.Min(p.Width, p.Height);
}
