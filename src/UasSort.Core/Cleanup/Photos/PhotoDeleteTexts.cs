// src/UasSort.Core/Cleanup/Photos/PhotoDeleteTexts.cs
using UasSort.Core.Planning;

namespace UasSort.Core.Cleanup;

/// <summary>What a later card shows for a photo moved out of Picture Offload (spec 2026-10-04 §6).</summary>
public static class PhotoDeleteTexts
{
    public static string Newness(LedgerPhotoDelete d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return $"removed from Picture Offload on {PlanText.ShortDate(DateOnly.FromDateTime(d.AtUtc))}";
    }

    public static string Evidence(LedgerPhotoDelete d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return d.Evidence switch
        {
            PhotoDeleteRecords.EvidenceLightroom or PhotoDeleteRecords.EvidencePanoramaStitch => "removed from Picture Offload after Lightroom import",
            PhotoDeleteRecords.EvidenceHyperlapseResult => "removed from Picture Offload; its hyperlapse video is in the library",
            PhotoDeleteRecords.EvidenceDateOnly => "removed from Picture Offload by date",
            _ => "removed from Picture Offload (not confirmed in Lightroom)",
        };
    }
}
