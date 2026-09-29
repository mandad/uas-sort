namespace UasSort.Core;

public sealed partial record LibraryFolder
{
    /// <summary>Local days of the members in the folder's ledger zone, else in <paramref name="tzId"/> (Ref §7.1). A ledger zone this
    /// PC can't resolve (hand edit, a newer zone database on another PC, empty) falls back to <paramref name="tzId"/>.</summary>
    public ImmutableHashSet<DateOnly> DaysIn(string tzId)
    {
        TimeZoneInfo zone = Zones.TryFind(TzId, out var own) ? own : Zones.Find(tzId);
        return MemberStartsUtc
            .Select(u => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(u, DateTimeKind.Utc), zone)))
            .ToImmutableHashSet();
    }
}
