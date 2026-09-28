using UasSort.Core.Library;

namespace UasSort.Core.Tests.Library;

public sealed class EventFolderNameTests
{
    [Theory]
    [InlineData("2026-09-27 Zachar Bay", 2026, 9, 27, "Zachar Bay")]
    [InlineData("2026-09-27", 2026, 9, 27, "")]
    [InlineData("2022-03-27 Makaha Valley", 2022, 3, 27, "Makaha Valley")]
    [InlineData("2026-07-25  Council  Road ", 2026, 7, 25, "Council  Road")]
    [InlineData("2024-02-29 Leap", 2024, 2, 29, "Leap")]
    public void EventFolderName_Matches(string name, int y, int m, int d, string description)
    {
        Assert.True(EventFolderName.TryParse(name, out DateOnly date, out string desc));
        Assert.Equal(new DateOnly(y, m, d), date);
        Assert.Equal(description, desc);
    }

    [Theory]
    [InlineData("Picture Offload")]
    [InlineData("Exports")]
    [InlineData("2026")]
    [InlineData("2026-07")]
    [InlineData("2026-13-01 Bad month")]
    [InlineData("2026-02-30 Bad day")]
    [InlineData("0000-01-01 Year zero")]
    [InlineData("2026-09-27Zachar")]
    [InlineData("x 2026-09-27")]
    [InlineData("２０２６-09-27 Fullwidth")]
    [InlineData(".uas-sort")]
    public void EventFolderName_Rejects(string name)
        => Assert.False(EventFolderName.TryParse(name, out _, out _));
}
