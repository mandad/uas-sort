using UasSort.Core;

namespace UasSort.Core.Tests.Library;

public sealed class LibraryFolderDaysTests
{
    private static readonly LibraryFolderRef Council =
        new(@"C:\Lib\UAS Videos\2026\2026-07\2026-07-25 Council Road", new DateOnly(2026, 7, 25), "Council Road");

    // 06:30Z = Jul 25 22:30 AKDT = Jul 26 02:30 EDT; 20:00Z = Jul 26 in both zones
    private static readonly ImmutableArray<DateTime> Starts =
        [new DateTime(2026, 7, 26, 6, 30, 0, DateTimeKind.Utc), new DateTime(2026, 7, 26, 20, 0, 0, DateTimeKind.Utc)];

    [Fact]
    public void LibraryFolder_DaysIn_UsesTheFolderLedgerZoneFirst()
    {
        var f = new LibraryFolder(Council, Starts, "America/Anchorage", null, LocationSource.Ledger);
        Assert.Equal(new[] { new DateOnly(2026, 7, 25), new DateOnly(2026, 7, 26) }, f.DaysIn("America/New_York").Order());
    }

    [Fact]
    public void LibraryFolder_DaysIn_FallsBackToTheGivenZone()
    {
        var f = new LibraryFolder(Council, Starts, null, null, LocationSource.Unknown);
        Assert.Equal(new[] { new DateOnly(2026, 7, 26) }, f.DaysIn("America/New_York").Order());
        Assert.Equal(new[] { new DateOnly(2026, 7, 25), new DateOnly(2026, 7, 26) }, f.DaysIn("America/Anchorage").Order());
    }

    [Theory] // deferred minor 05.2: a ledger zone this PC can't resolve (hand edit, newer ICU elsewhere, empty) falls back, never throws
    [InlineData("Bogus/Zone")]
    [InlineData("")]
    public void LibraryFolder_DaysIn_UnknownLedgerZone_UsesTheGivenZone(string bad)
    {
        var f = new LibraryFolder(Council, Starts, bad, null, LocationSource.Ledger);
        Assert.Equal(new[] { new DateOnly(2026, 7, 25), new DateOnly(2026, 7, 26) }, f.DaysIn("America/Anchorage").Order());
        Assert.Equal(new[] { new DateOnly(2026, 7, 26) }, f.DaysIn("America/New_York").Order());
    }

    [Fact]
    public void LibraryFolder_DaysIn_NoMembersIsEmpty()
        => Assert.Empty(new LibraryFolder(Council, [], null, null, LocationSource.Unknown).DaysIn("America/Anchorage"));
}
