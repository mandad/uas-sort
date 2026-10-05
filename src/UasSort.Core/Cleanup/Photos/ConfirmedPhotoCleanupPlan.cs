// src/UasSort.Core/Cleanup/Photos/ConfirmedPhotoCleanupPlan.cs
namespace UasSort.Core.Cleanup;

/// <summary>Mirrors ConfirmedCleanupPlan: immutable, built only by PhotoCleanupPlan.Confirm. Paths is what the guard allows for
/// IoOp.PhotoRootRecycle: each file of a photo row, the folder of a set row (PathRules.Join, case-insensitive).</summary>
public sealed class ConfirmedPhotoCleanupPlan
{
    internal ConfirmedPhotoCleanupPlan(PhotoCleanupPlan plan, ImmutableArray<PhotoRow> items, ImmutableArray<PhotoRow> kept, PhotoCleanupAck ack,
                                       Guid token, DateTime confirmedUtc)
    {
        ArgumentNullException.ThrowIfNull(plan);
        Plan = plan;
        Items = items;
        Kept = kept;
        Ack = ack;
        Token = token;
        ConfirmedUtc = confirmedUtc;
        Paths = items.SelectMany(r => PhotoCleanupPaths.Targets(plan.PhotoRoot, r.Item)).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public PhotoCleanupPlan Plan { get; }
    public string PhotoRoot => Plan.PhotoRoot;
    public ImmutableArray<PhotoRow> Items { get; }       // the rows set to Delete, oldest first
    public ImmutableArray<PhotoRow> Kept { get; }        // the rows left on Keep
    public PhotoCleanupAck Ack { get; }
    public Guid Token { get; }
    public DateTime ConfirmedUtc { get; }
    public IReadOnlySet<string> Paths { get; }

    /// <summary>The photoDelete evidence of a row (spec 2026-10-04 §6).</summary>
    public PhotoEvidence EvidenceOf(PhotoRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return Plan.Request.Mode == PhotoCleanupMode.BeforeDate ? PhotoEvidence.DateOnly
             : row.Verification.Verified ? row.Verification.Evidence
             : PhotoEvidence.UnverifiedConfirmed;
    }
}
