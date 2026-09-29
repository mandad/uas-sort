// tests/UasSort.Testing/CleanupPlanFixtures.cs
namespace UasSort.Testing;

/// <summary>Minimal CleanupPlan / ConfirmedCleanupPlan for eraser and view-model tests (Parts 09, 10), built through Core's
/// internal constructors (InternalsVisibleTo UasSort.Testing, Part 02). The files under a named set folder form that set's
/// unit; every other file is its own Evidence unit. No Confirm checks run, so a Platform test may use any card root.</summary>
public static class CleanupPlanFixtures
{
    private static readonly DateTime T = new(2026, 7, 26, 7, 50, 0, DateTimeKind.Utc);
    private const long FileSize = 1_000;

    public static CleanupPlan Plan(string cardRoot, CardIdentity card, IReadOnlyList<string> fileRelPaths,
                                   IReadOnlyList<string> setFolderRelDirs)
    {
        ArgumentNullException.ThrowIfNull(cardRoot);
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(fileRelPaths);
        ArgumentNullException.ThrowIfNull(setFolderRelDirs);
        var files = fileRelPaths.Select(CleanupPaths.Rel).ToList();
        var sets = setFolderRelDirs.Select(CleanupPaths.Rel).ToList();
        var delete = new List<CleanupCandidate>();
        foreach (var set in sets)
            delete.Add(Candidate(set, [.. files.Where(f => CleanupPaths.IsUnder(f, set) && !f.Equals(set, StringComparison.OrdinalIgnoreCase))], set));
        foreach (var f in files.Where(f => !sets.Exists(set => CleanupPaths.IsUnder(f, set))))
            delete.Add(Candidate(f, [f], null));

        var space = FakeLayout.CardSpace;
        var request = new CleanupRequest(CleanupMode.BeforeDate, new DateOnly(2026, 7, 26), null, false);
        var allocated = delete.Sum(c => c.AllocatedBytes);
        return new CleanupPlan("fixture-plan", card, cardRoot, "0000000000000000", null, request, space, [.. delete], [],
            new CleanupRows([], []), [], [], new CleanupCutoff(new DateOnly(2026, 7, 26), null, null, null, 0, 0, null),
            delete.Sum(c => c.Files.Length), allocated, Math.Min(space.TotalBytes, space.FreeBytes + allocated), null,
            CleanupFingerprint.Compute(request, space, delete));
    }

    public static ConfirmedCleanupPlan Confirmed(string cardRoot, CardIdentity card, IReadOnlyList<string> fileRelPaths,
                                                 IReadOnlyList<string> setFolderRelDirs, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return new ConfirmedCleanupPlan(Plan(cardRoot, card, fileRelPaths, setFolderRelDirs), Guid.NewGuid(),
                                        clock.GetUtcNow().UtcDateTime);
    }

    private static CleanupCandidate Candidate(string unitRelPath, IReadOnlyList<string> fileRelPaths, string? setFolder)
    {
        var cls = setFolder is null ? EntryClass.Video : EntryClass.SetMember;
        var files = fileRelPaths.Select(p => new CardEntry(p, FileSize, T, T, T, 0x20, cls, null)).ToImmutableArray();
        return new CleanupCandidate(new ItemId(unitRelPath), files,
            files.Length * CleanupPaths.Allocated(FileSize, FakeLayout.CardSpace.ClusterBytes), T, new DateOnly(2026, 7, 25),
            "America/Anchorage", CleanupEligibility.Evidence, AuditCategory.InLedger, "in the history, verified", null, null, null,
            setFolder is null ? ItemKind.Video : ItemKind.Set, T.AddHours(-8), null, setFolder, null, [], EvidenceSource.Listed,
            false, []);
    }
}
