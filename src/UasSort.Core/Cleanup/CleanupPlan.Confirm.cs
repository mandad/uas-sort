// src/UasSort.Core/Cleanup/CleanupPlan.Confirm.cs
namespace UasSort.Core;

public sealed partial class CleanupPlan
{
    /// <summary>The only factory of ConfirmedCleanupPlan (Ref §10.6 Confirm). Recomputes the fingerprint; throws on a VM bug.</summary>
    public ConfirmedCleanupPlan Confirm(CleanupAck ack, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(ack);
        ArgumentNullException.ThrowIfNull(clock);
        var recomputed = CleanupFingerprint.Compute(Request, SpaceBefore, Delete);
        if (!string.Equals(recomputed, Fingerprint, StringComparison.Ordinal)
            || !string.Equals(recomputed, ack.PlanFingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("The cleanup plan changed after it was shown; acknowledge it again.");
        if (!ack.CantBeRecovered) throw new InvalidOperationException("\"Files deleted from a memory card can't be recovered.\" is not ticked.");
        if (Delete.IsEmpty) throw new InvalidOperationException("The cleanup plan deletes nothing.");
        if (!Undecided.IsEmpty) throw new InvalidOperationException("Some review rows are still undecided.");
        if (Delete.Any(c => c.Eligibility == CleanupEligibility.Never))
            throw new InvalidOperationException("The cleanup plan holds a file uas-sort never deletes.");

        var notInLibrary = Delete.Where(c => c.Eligibility == CleanupEligibility.NotInLibrary).Select(c => c.Unit).ToHashSet();
        if (!notInLibrary.SetEquals(ack.NotInLibraryDelete))
            throw new InvalidOperationException("Every file not in the library needs its own Delete row.");
        if (notInLibrary.Count > 0 && !Request.IncludeNotInLibrary)
            throw new InvalidOperationException("Files not in the library are in the plan while the switch is off.");
        if (ack.IncludesNotInLibrary != (notInLibrary.Count > 0))
            throw new InvalidOperationException("\"Includes files not proven to be in your library.\" does not match the plan.");

        foreach (var c in Delete)
        {
            foreach (var f in c.Files) _ = CanonicalFile(CardRoot, f.RelPath);
            if (c.SetFolder is { } folder) _ = CanonicalSetFolder(CardRoot, folder);
        }
        return new ConfirmedCleanupPlan(this, Guid.NewGuid(), clock.GetUtcNow().UtcDateTime);   // Part 02 derives the path sets
    }

    /// <summary>Validates a card file path (plain segments, under DCIM) and returns PathRules.Join(cardRoot, relPath) — the path
    /// ConfirmedCleanupPlan derives for it — or throws.</summary>
    internal static string CanonicalFile(string cardRoot, string relPath)
    {
        if (CleanupPaths.Full(cardRoot, relPath) is null)
            throw new InvalidOperationException($"Not a plain card path: {relPath}");
        var full = PathRules.Join(cardRoot, relPath);
        if (!full.StartsWith(PathRules.Join(cardRoot, "DCIM") + "\\", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Outside the card's DCIM folder: {relPath}");
        return full;
    }

    /// <summary>"&lt;CardRoot&gt;\DCIM\PANORAMA\&lt;set&gt;" or "…\HYPERLAPSE\&lt;set&gt;" (PathRules.Join), or throws.</summary>
    internal static string CanonicalSetFolder(string cardRoot, string relDir)
    {
        var segments = CleanupPaths.Rel(relDir).Split('/');
        if (segments.Length != 3 || !segments[0].Equals("DCIM", StringComparison.OrdinalIgnoreCase)
            || !(segments[1].Equals("PANORAMA", StringComparison.OrdinalIgnoreCase)
                 || segments[1].Equals("HYPERLAPSE", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Not a set folder: {relDir}");
        return CanonicalFile(cardRoot, relDir);
    }
}
