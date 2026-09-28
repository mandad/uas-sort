namespace UasSort.Core;

public sealed partial record LibraryFolder
{
    /// <summary>Local days of the members in the folder's ledger zone, else in <paramref name="tzId"/> (Ref §7.1).</summary>
    public ImmutableHashSet<DateOnly> DaysIn(string tzId)
    {
        TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(TzId ?? tzId);
        return MemberStartsUtc
            .Select(u => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(u, DateTimeKind.Utc), zone)))
            .ToImmutableHashSet();
    }
}
