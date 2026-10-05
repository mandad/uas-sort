// src/UasSort.Review/PhotoCleanup/PhotoCleanupAvailability.cs
namespace UasSort.Review;

public sealed record PhotoCleanupContext(bool CommitRunning, bool ScanRunning, bool CardCleanupOpen, bool RootsConfirmed, string PhotoRoot,
                                         bool PhotoRootAvailable);

/// <summary>When [Clean up Picture Offload…] is enabled (spec 2026-10-04 §2): no offload, card cleanup or scan running, and the photo root
/// set and available; no card is needed. The reason is shown as visible text, first matching row wins (as Task U4 for card cleanup).</summary>
public static class PhotoCleanupAvailability
{
    public static string? Reason(PhotoCleanupContext c)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (c.CommitRunning) return "Wait until the offload finishes";
        if (c.CardCleanupOpen) return "Wait until the card cleanup finishes";
        if (c.ScanRunning) return "Wait until the scan finishes";
        if (!c.RootsConfirmed) return "Set up the photo folder first";
        if (!c.PhotoRootAvailable) return $"The photo folder {c.PhotoRoot} is not available";
        return null;
    }
}
