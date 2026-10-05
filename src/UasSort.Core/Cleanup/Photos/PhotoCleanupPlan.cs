// src/UasSort.Core/Cleanup/Photos/PhotoCleanupPlan.cs
namespace UasSort.Core.Cleanup;

/// <summary>A reviewed Picture Offload plan (spec 2026-10-04 §2). A class with an internal constructor: only PhotoCleanupPlanner.Build
/// (and the test fixtures) create one; Confirm is the only way to a ConfirmedPhotoCleanupPlan.</summary>
public sealed class PhotoCleanupPlan
{
    internal PhotoCleanupPlan(string planId, string photoRoot, PhotoCleanupRequest request, ImmutableArray<PhotoRow> rows,
                              ImmutableArray<PhotoRow> notEligible, ImmutableArray<string> notTouched)
    {
        PlanId = planId;
        PhotoRoot = photoRoot;
        Request = request;
        Rows = rows;
        NotEligible = notEligible;
        NotTouched = notTouched;
        Fingerprint = PhotoCleanupFingerprint.Compute(photoRoot, request, rows);
    }

    public string PlanId { get; }
    public string PhotoRoot { get; }
    public PhotoCleanupRequest Request { get; }
    public ImmutableArray<PhotoRow> Rows { get; }               // eligible, oldest first
    public ImmutableArray<PhotoRow> NotEligible { get; }        // date unknown, or a set straddling the cutoff
    public ImmutableArray<string> NotTouched { get; }           // other files and folders in the photo root
    public string Fingerprint { get; }

    /// <summary>Date mode: every row starts Delete. Verify mode: verified rows start Delete, unverified rows Keep.</summary>
    public ImmutableHashSet<string> DefaultDelete()
        => Rows.Where(r => Request.Mode == PhotoCleanupMode.BeforeDate || r.Verification.Verified)
               .Select(r => r.Key).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);

    public PhotoTotals Totals(IReadOnlySet<string> delete)
    {
        ArgumentNullException.ThrowIfNull(delete);
        var chosen = Rows.Where(r => delete.Contains(r.Key)).ToList();
        return new PhotoTotals(chosen.Count(r => r.Item.Kind == PhotoItemKind.Photo), chosen.Count(r => r.Item.Kind == PhotoItemKind.Set),
                               chosen.Sum(r => r.Item.Members.Length), chosen.Sum(r => r.Item.Bytes),
                               Request.Mode == PhotoCleanupMode.Verify ? chosen.Count(r => !r.Verification.Verified) : 0);
    }

    /// <summary>The only factory of ConfirmedPhotoCleanupPlan (spec 2026-10-04 §5). Recomputes the fingerprint; throws on a VM bug.</summary>
    public ConfirmedPhotoCleanupPlan Confirm(PhotoCleanupAck ack, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(ack);
        ArgumentNullException.ThrowIfNull(clock);
        var recomputed = PhotoCleanupFingerprint.Compute(PhotoRoot, Request, Rows);
        if (!string.Equals(recomputed, Fingerprint, StringComparison.Ordinal) || !string.Equals(recomputed, ack.PlanFingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("The Picture Offload plan changed after it was shown; confirm it again.");
        if (!ack.MoveToRecycleBin) throw new InvalidOperationException("\"Move … to the Recycle Bin\" is not ticked.");
        if (ack.Delete.IsEmpty) throw new InvalidOperationException("Nothing is set to Delete.");
        var delete = new HashSet<string>(ack.Delete, StringComparer.OrdinalIgnoreCase);
        var keys = Rows.Select(r => r.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in delete)
            if (!keys.Contains(key)) throw new InvalidOperationException($"{key} is not an eligible row of this plan.");
        var chosen = Rows.Where(r => delete.Contains(r.Key)).ToImmutableArray();
        var unverified = Request.Mode == PhotoCleanupMode.Verify && chosen.Any(r => !r.Verification.Verified);
        if (unverified != ack.UnverifiedIncluded)
            throw new InvalidOperationException("The second acknowledgement does not match the rows set to Delete.");
        foreach (var r in chosen) _ = PhotoCleanupPaths.Targets(PhotoRoot, r.Item);
        return new ConfirmedPhotoCleanupPlan(this, chosen, [.. Rows.Where(r => !delete.Contains(r.Key))], ack, Guid.NewGuid(),
                                             clock.GetUtcNow().UtcDateTime);
    }
}
