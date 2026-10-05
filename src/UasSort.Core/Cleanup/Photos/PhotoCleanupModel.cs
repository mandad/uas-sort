// src/UasSort.Core/Cleanup/Photos/PhotoCleanupModel.cs
namespace UasSort.Core.Cleanup;

public enum PhotoItemKind { Photo, Set }

public enum PhotoSetKind { Unknown, Panorama, Hyperlapse }

public enum CaptureSource { None, Ledger, Exif }

public enum PhotoEligibility { Eligible, AfterCutoff, StraddlesCutoff, DateUnknown }

/// <summary>One file of a Picture Offload item. RelPath is relative to the photo root with '\': "X.DNG", or "001_0087\PANO_0001.DNG".
/// LocalDate is null when the date is unknown (DateProblem says why); Stamp is null until EXIF was read.</summary>
public sealed record PhotoMember(string RelPath, long Size, DateTime MtimeUtc, uint Attributes, DateTime? CaptureUtc, DateOnly? LocalDate,
                                 CaptureSource Source, ExifStamp? Stamp, string? DateProblem, LedgerFile? Ledger)
{
    public string Name => PathRules.FileName(RelPath);
    public FileKey Key => FileKey.Of(Name, Size);
    public bool IsCloudOnly => PhotoCleanupRules.IsCloudOnly(Attributes);

    /// <summary>The image size from EXIF, when it was read (the stitched-panorama shape test); null otherwise.</summary>
    public (int Width, int Height)? Pixels { get; init; }
}

/// <summary>A direct child of the photo root: a photo unit (the primary file, then its JPG twin) or a set folder (members by name).</summary>
public sealed record PhotoItem(string RelPath, PhotoItemKind Kind, PhotoSetKind SetKind, string? SetName, ImmutableArray<PhotoMember> Members)
{
    public PhotoMember Primary => Members[0];
    public long Bytes => Members.Sum(m => m.Size);
    public bool DateKnown => Members.All(m => m.LocalDate is not null);
    public DateOnly? FirstDate => DateKnown ? Members.Min(m => m.LocalDate!.Value) : null;
    public DateOnly? LastDate => DateKnown ? Members.Max(m => m.LocalDate!.Value) : null;
}

public sealed record PhotoVerification(bool Verified, PhotoEvidence Evidence, string Text)
{
    public static readonly PhotoVerification DateMode = new(false, PhotoEvidence.DateOnly, PhotoCleanupRules.DateModeText);
}

public sealed record PhotoRow(PhotoItem Item, PhotoEligibility Eligibility, PhotoVerification Verification, string? Why /* not eligible: why */)
{
    public string Key => Item.RelPath;
}

public sealed record PhotoSurvey(string PhotoRoot, ImmutableArray<PhotoItem> Items, ImmutableArray<string> NotTouched, LedgerSnapshot Ledger);

public sealed record PhotoCleanupRequest(PhotoCleanupMode Mode, DateOnly Cutoff /* "shot on or before", local date */, string? LightroomFolder);

public sealed record PhotoCleanupAck(string PlanFingerprint, ImmutableHashSet<string> Delete /* row keys */, bool MoveToRecycleBin,
                                     bool UnverifiedIncluded /* the second acknowledgement */);

public sealed record PhotoTotals(int Photos, int Sets, int Files, long Bytes, int Unverified);

public sealed record PhotoScanProgress(string Phase, int Done, int Total);
