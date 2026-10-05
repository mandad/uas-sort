// tests/UasSort.Testing/PhotoCleanupFixtures.cs
namespace UasSort.Testing;

/// <summary>Picture Offload cleanup items, rows and plans for Core, Review and Platform tests, built through Core's internal constructors
/// (InternalsVisibleTo UasSort.Testing). Members default to a ledger-dated local file.</summary>
public static class PhotoCleanupFixtures
{
    public const string PhotoRoot = FakeLayout.PhotoRoot;
    public static readonly DateTime Mtime = new(2026, 6, 2, 3, 0, 0, DateTimeKind.Utc);

    public static PhotoMember Member(string relPath, DateOnly date, long size = 25_000_000, uint attributes = 0x20, ExifStamp? stamp = null,
                                     DateTime? mtimeUtc = null, DateTime? captureUtc = null, LedgerFile? ledger = null)
        => new(relPath, size, mtimeUtc ?? Mtime, attributes, captureUtc ?? date.ToDateTime(new TimeOnly(20, 0), DateTimeKind.Utc), date,
               CaptureSource.Ledger, stamp, null, ledger);

    public static PhotoItem Photo(string name, DateOnly date, string? twin = null, long size = 25_000_000, ExifStamp? stamp = null)
        => new(name, PhotoItemKind.Photo, PhotoSetKind.Unknown, null,
               twin is null ? [Member(name, date, size, stamp: stamp)] : [Member(name, date, size, stamp: stamp), Member(twin, date, 8_000_000)]);

    public static PhotoItem Set(string folder, DateOnly date, PhotoSetKind kind, params string[] members)
        => new(folder, PhotoItemKind.Set, kind, PhotoCleanupRules.SetNameOf(folder),
               [.. members.Select((m, i) => Member(folder + "\\" + m, date, 13_000_000 + i))]);

    public static PhotoRow Row(PhotoItem item, bool verified = true, PhotoEvidence evidence = PhotoEvidence.Lightroom)
        => new(item, PhotoEligibility.Eligible,
               verified ? new PhotoVerification(true, evidence, "in Lightroom") : new PhotoVerification(false, PhotoEvidence.Lightroom, "not found in Lightroom"),
               null);

    public static PhotoCleanupPlan Plan(PhotoCleanupMode mode, DateOnly cutoff, IEnumerable<PhotoRow> rows, string photoRoot = PhotoRoot,
                                        IEnumerable<PhotoRow>? notEligible = null, IEnumerable<string>? notTouched = null,
                                        LightroomIndexSummary? lightroom = null)
        => new("fixture-photos", photoRoot, new PhotoCleanupRequest(mode, cutoff, mode == PhotoCleanupMode.Verify ? @"X:\Lightroom" : null),
               [.. rows], [.. notEligible ?? []], [.. notTouched ?? []]) { Lightroom = lightroom };

    /// <summary>Every row set to Delete and confirmed through PhotoCleanupPlan.Confirm (both acknowledgements as needed).</summary>
    public static ConfirmedPhotoCleanupPlan Confirmed(TimeProvider clock, PhotoCleanupMode mode, IEnumerable<PhotoRow> rows, string photoRoot = PhotoRoot)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var plan = Plan(mode, new DateOnly(2026, 6, 30), rows, photoRoot);
        var all = plan.Rows.Select(r => r.Key).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        var unverified = mode == PhotoCleanupMode.Verify && plan.Rows.Any(r => !r.Verification.Verified);
        return plan.Confirm(new PhotoCleanupAck(plan.Fingerprint, all, true, unverified), clock);
    }
}
