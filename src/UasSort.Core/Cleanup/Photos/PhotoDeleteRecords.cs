// src/UasSort.Core/Cleanup/Photos/PhotoDeleteRecords.cs
namespace UasSort.Core.Cleanup;

public enum PhotoCleanupMode { BeforeDate, Verify }

public enum PhotoEvidence { Lightroom, HyperlapseResult, PanoramaStitch, DateOnly, UnverifiedConfirmed }

/// <summary>The photoDelete record's field values (spec 2026-10-04 §6).</summary>
public static class PhotoDeleteRecords
{
    public const string EvidenceLightroom = "lightroom";
    public const string EvidenceHyperlapseResult = "hyperlapseResult";
    public const string EvidencePanoramaStitch = "panoramaStitch";
    public const string EvidenceDateOnly = "dateOnly";
    public const string EvidenceUnverifiedConfirmed = "unverifiedConfirmed";
    public const string ModeBeforeDate = "beforeDate";
    public const string ModeVerify = "verify";

    public static string Evidence(PhotoEvidence e) => e switch
    {
        PhotoEvidence.Lightroom => EvidenceLightroom,
        PhotoEvidence.HyperlapseResult => EvidenceHyperlapseResult,
        PhotoEvidence.PanoramaStitch => EvidencePanoramaStitch,
        PhotoEvidence.DateOnly => EvidenceDateOnly,
        PhotoEvidence.UnverifiedConfirmed => EvidenceUnverifiedConfirmed,
        _ => throw new ArgumentOutOfRangeException(nameof(e)),
    };

    public static string Mode(PhotoCleanupMode m) => m == PhotoCleanupMode.Verify ? ModeVerify : ModeBeforeDate;

    public static bool IsEvidence(string? s)
        => s is EvidenceLightroom or EvidenceHyperlapseResult or EvidencePanoramaStitch or EvidenceDateOnly or EvidenceUnverifiedConfirmed;

    public static bool IsMode(string? s) => s is ModeBeforeDate or ModeVerify;
}
