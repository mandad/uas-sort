// src/UasSort.Review/Cleanup/CleanupAvailability.cs
namespace UasSort.Review;

public sealed record CleanupContext(bool CommitRunning, bool ScanRunning, CardSource? Source, bool CardPresent, string? VolumeRefusal);

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
        if (c.VolumeRefusal is not null) return (false, "This doesn't look like a drone card (it may be a backup drive)");
        return (true, null);
    }
}
