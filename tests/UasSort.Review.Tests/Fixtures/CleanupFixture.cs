// tests/UasSort.Review.Tests/Fixtures/CleanupFixture.cs  (Task 10.21 part; Task 10.22 appends the inputs builder)
namespace UasSort.Review.Tests;

internal static partial class CleanupFixture
{
    public static CleanupCandidate Candidate(string relPath, ItemKind kind, long size, TimeSpan? duration, DateTime localTime, DateTime captureUtc,
                                             NotInLibraryReason? reason, string reasonText, string? place = null, GeoPoint? location = null,
                                             bool ticked = false)
    {
        var entry = new CardEntry(relPath, size, captureUtc, captureUtc, captureUtc, 0x20, kind == ItemKind.Video ? EntryClass.Video : EntryClass.Photo, null);
        return new CleanupCandidate(new ItemId(relPath), [entry], size, captureUtc, DateOnly.FromDateTime(localTime), TestPlans.Anchorage,
            reason is null ? CleanupEligibility.Evidence : CleanupEligibility.NotInLibrary, AuditCategory.Unaccounted, reasonText, duration,
            location, place, kind, localTime, reason, null, null, [], null, ticked, []);
    }

    /// <summary>A CleanupResult over a real ConfirmedCleanupPlan from Part 08's CleanupPlanFixtures (built through Core's internal
    /// constructors), covering the files the outcomes name.</summary>
    public static CleanupResult Result(ImmutableArray<CleanupOutcome> outcomes, CleanupStop? stop, long freeAfter, ImmutableArray<string> stillListed,
                                       string? closingReadError = null)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 28, 3, 0, 0, TimeSpan.Zero));
        string[] files = [.. outcomes.Select(o => o.Unit.CardRelPath).Where(p => p.EndsWith(".MP4", StringComparison.OrdinalIgnoreCase))];
        string[] setFolders = [.. outcomes.Select(o => o.Unit.CardRelPath).Where(p => !p.EndsWith(".MP4", StringComparison.OrdinalIgnoreCase))];
        var confirmed = CleanupPlanFixtures.Confirmed(@"E:\", TestPlans.Card, files, setFolders, clock);
        return new("cleanup01", confirmed, outcomes, stop, new CardSpace(freeAfter, 256_060_514_304, 131_072), stillListed,
                   TestPlans.Utc(2026, 9, 28, 3, 0), TestPlans.Utc(2026, 9, 28, 3, 2), closingReadError);
    }
}
