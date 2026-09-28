namespace UasSort.Core.Tests.Support;

/// <summary>Builds CleanupPlan / ConfirmedCleanupPlan through Core's internal constructors (InternalsVisibleTo Core.Tests).</summary>
internal static class TestCleanupPlans
{
    public static readonly CardIdentity Card = new(0x1A2B3C4D, null, "exFAT", 256_060_514_304);
    public static readonly DateTime T = new(2026, 7, 26, 7, 50, 0, DateTimeKind.Utc);
    public const string InventoryHash = "9f3c0a6d12e4b7a1";
    public const string CameraModel = "FC9113";

    public static CleanupCandidate Candidate(string unitRelPath, IReadOnlyList<string> fileRelPaths,
        CleanupEligibility eligibility = CleanupEligibility.Evidence, string? setFolder = null)
    {
        var cls = setFolder is null ? EntryClass.Video : EntryClass.SetMember;
        var files = fileRelPaths.Select(p => new CardEntry(p, 1000, T, T, T, 0x20, cls, null)).ToImmutableArray();
        return new CleanupCandidate(new ItemId(unitRelPath), files, 131_072L * files.Length, T, new DateOnly(2026, 7, 25),
            "America/Anchorage", eligibility, AuditCategory.InLedger, "in the history, verified", null, null, null,
            setFolder is null ? ItemKind.Video : ItemKind.Set, T.AddHours(-8),
            eligibility == CleanupEligibility.NotInLibrary ? NotInLibraryReason.New : null, setFolder, null, [],
            eligibility == CleanupEligibility.Evidence ? EvidenceSource.Listed : null, false, []);
    }

    public static CleanupPlan Plan(string cardRoot, params CleanupCandidate[] delete)
    {
        var files = delete.Sum(d => d.Files.Length);
        var bytes = delete.Sum(d => d.AllocatedBytes);
        var space = new CardSpace(12_400_000_000, 256_060_514_304, 131_072);
        return new CleanupPlan("plan-1", Card, cardRoot, InventoryHash, CameraModel,
            new CleanupRequest(CleanupMode.BeforeDate, new DateOnly(2026, 7, 26), null,
                delete.Any(d => d.Eligibility == CleanupEligibility.NotInLibrary)),
            space, [.. delete], [], new CleanupRows([], []), [], [],
            new CleanupCutoff(new DateOnly(2026, 7, 26), null, null, null, 0, 0, null), files, bytes, space.FreeBytes + bytes, null,
            "0000000000000000");
    }

    public static ConfirmedCleanupPlan Confirmed(string cardRoot, params CleanupCandidate[] delete)
        => new(Plan(cardRoot, delete), Guid.NewGuid(), T);
}
