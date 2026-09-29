// src/UasSort.Core/Cleanup/CleanupVolumeCheck.cs
using System.Text.RegularExpressions;

namespace UasSort.Core.Cleanup;

/// <summary>Ref §10.6 cleanup volume check, for the [Clean up card…] button. The eraser factory repeats it from Win32.</summary>
public static partial class CleanupVolumeCheck
{
    public const string NotACard = "This doesn't look like a drone card (it may be a backup drive)";
    public const string WriteProtected = "The card is write-protected (lock switch)";

    [GeneratedRegex(@"^[A-Za-z]:\\$", RegexOptions.CultureInvariant)]
    private static partial Regex DriveRoot();

    [GeneratedRegex(@"^MISC/FC[^/]*\.db$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex MiscDb();

    public static string? Refusal(VolumeInfo volume, ListingResult card, Settings settings, string appDataDir)
    {
        ArgumentNullException.ThrowIfNull(volume);
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(appDataDir);

        if (volume.IsReadOnlyVolume) return WriteProtected;

        // 2. a volume root, and the card listing was taken at that root
        var root = volume.Root.Replace('/', '\\');
        if (!root.EndsWith('\\')) root += "\\";
        if (!DriveRoot().IsMatch(root)) return NotACard;
        foreach (var e in card.Entries)
        {
            var listedRoot = e.FullPath.Length >= e.RelPath.Length ? e.FullPath[..^e.RelPath.Length] : "";
            if (!listedRoot.Replace('/', '\\').Equals(root, StringComparison.OrdinalIgnoreCase)) return NotACard;
        }

        // 3. exFAT or FAT32
        var fs = volume.Identity.FileSystem;
        if (!fs.Equals("exFAT", StringComparison.OrdinalIgnoreCase) && !fs.Equals("FAT32", StringComparison.OrdinalIgnoreCase))
            return NotACard;

        // 4. SD/MMC bus, or USB with removable media
        var bus = volume.BusType;
        var busOk = bus.Equals("Sd", StringComparison.OrdinalIgnoreCase)
                 || bus.Equals("Mmc", StringComparison.OrdinalIgnoreCase)
                 || (bus.Equals("Usb", StringComparison.OrdinalIgnoreCase) && volume.RemovableMedia);
        if (!busOk) return NotACard;

        // 5. not system/boot/paging; no configured root, previous photo root or app data on it
        if (volume.IsSystemBootOrPaging) return NotACard;
        var configured = new List<string> { settings.VideoRoot, settings.PhotoRoot, appDataDir };
        configured.AddRange(settings.PreviousPhotoRoots);
        foreach (var p in configured)
        {
            var q = p.Replace('/', '\\');
            if (q.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                || (q + "\\").Equals(root, StringComparison.OrdinalIgnoreCase)) return NotACard;
        }

        // 6. the drone-written index: MISC\FC*.db or a MISC\IDX folder
        var hasIndex = false;
        foreach (var e in card.Entries)
        {
            var rel = CleanupPaths.Rel(e.RelPath);
            if ((!e.IsDirectory && MiscDb().IsMatch(rel))
                || (e.IsDirectory && rel.Equals("MISC/IDX", StringComparison.OrdinalIgnoreCase)))
            { hasIndex = true; break; }
        }
        return hasIndex ? null : NotACard;
    }
}
