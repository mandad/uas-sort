// src/UasSort.Review/Cleanup/CleanupAvailability.cs
namespace UasSort.Review;

public sealed record CleanupContext(bool CommitRunning, bool ScanRunning, CardSource? Source, bool CardPresent, string? VolumeRefusal,
                                    string? VolumeRefusalDetail = null);

/// <summary>When [Clean up card…] is enabled, and the tooltip when it isn't (Ref §10.6 table, first matching row).</summary>
public static class CleanupAvailability
{
    public static (bool Enabled, string? Tooltip) For(CleanupContext c)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (c.CommitRunning) return (false, "Wait until the offload finishes");
        if (c.ScanRunning) return (false, "Wait until the scan finishes");
        if (c.Source is not { } s || !c.CardPresent) return (false, "Rescan first");
        if (s.IsWriteProtected) return (false, "The card is write-protected (lock switch)");
        if (s.IsBrowsedFolder) return (false, "Cleanup works only on a detected card. A browsed folder could be a backup copy.");
        if (c.VolumeRefusal is not null)
            return (false, string.Equals(c.VolumeRefusal, CleanupVolumeCheck.WriteProtected, StringComparison.Ordinal)
                ? CleanupVolumeCheck.WriteProtected     // a locked card, not "doesn't look like a drone card"
                : CleanupVolumeCheck.NotACard);
        return (true, null);
    }

    /// <summary>The reason as visible text (Task U4: a disabled button shows no tooltip): null when enabled; for a volume that fails
    /// the cleanup volume check, "This doesn't look like a drone card (rule 4: bus Scsi, removable media false)" when the check
    /// named its rule; otherwise the tooltip.</summary>
    public static string? Text(CleanupContext c)
    {
        var (enabled, tooltip) = For(c);
        if (enabled) return null;
        return string.Equals(tooltip, CleanupVolumeCheck.NotACard, StringComparison.Ordinal) && c.VolumeRefusalDetail is { } detail
            ? $"This doesn't look like a drone card ({detail})"
            : tooltip;
    }
}
