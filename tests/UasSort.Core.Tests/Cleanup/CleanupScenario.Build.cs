// tests/UasSort.Core.Tests/Cleanup/CleanupScenario.Build.cs
namespace UasSort.Core.Tests.Cleanup;

internal sealed partial class CleanupScenario
{
    public static CleanupRows NoRows => new([], []);

    public static CleanupRequest Before(int y, int m, int d, bool include = false)
        => new(CleanupMode.BeforeDate, new DateOnly(y, m, d), null, include);

    public static CleanupRequest HaveFree(long bytes, bool include = false)
        => new(CleanupMode.FreeSpace, null, new FreeSpaceGoal(FreeSpaceKind.HaveFree, bytes), include);

    public static CleanupRequest FreeUp(long bytes, bool include = false)
        => new(CleanupMode.FreeSpace, null, new FreeSpaceGoal(FreeSpaceKind.FreeUp, bytes), include);

    /// <summary>firstShown defaults to every candidate: the review list "as first shown".</summary>
    public CleanupPlan Build(CleanupRequest request, CleanupRows? rows = null, IReadOnlySet<ItemId>? firstShown = null)
    {
        var inputs = Inputs();
        var candidates = CleanupPlanner.Candidates(inputs);
        return CleanupPlanner.Build(inputs, candidates, request, rows ?? NoRows,
                                    firstShown ?? candidates.Select(c => c.Unit).ToHashSet());
    }
}
