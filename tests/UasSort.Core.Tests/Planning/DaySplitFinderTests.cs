// tests/UasSort.Core.Tests/Planning/DaySplitFinderTests.cs
using UasSort.Core.Planning;
using UasSort.Testing.Planning;

namespace UasSort.Core.Tests.Planning;

public sealed class DaySplitFinderTests
{
    [Fact]
    public void CouncilAnvil_OneEmphasisedSplitBeforeFirstAnvilClip()
    {
        var items = new[]
        {
            ItemFactory.Of(Clip.Vid("20260725232655", 117, Sites.Council)),
            ItemFactory.Of(Clip.Vid("20260726022937", 118, Sites.Council)),
            ItemFactory.Of(Clip.Vid("20260726235645", 1, Sites.Anvil)),
            ItemFactory.Of(Clip.Vid("20260727000012", 2, Sites.Anvil)),
        };
        var s = Assert.Single(DaySplitFinder.Find(items));
        Assert.Equal(items[2].Raw.Unit.Id, s.FirstOfDay);
        Assert.Equal((new DateOnly(2026, 7, 25), new DateOnly(2026, 7, 26)), (s.From, s.To));
        Assert.Equal(33.9, s.Apart!.Value.Miles, 1);
        Assert.True(s.Emphasised);
        Assert.Equal(new TimeSpan(21, 27, 8), s.Gap);
    }

    [Fact]
    public void KodiakMultiDay_SplitsAreNotEmphasised()
    {
        var items = new[]
        {
            ItemFactory.Of(Clip.Vid("20260523015251", 40, Sites.KodiakTown)),
            ItemFactory.Of(Clip.Vid("20260523201928", 52, Sites.KodiakTown)),
            ItemFactory.Of(Clip.Vid("20260524190521", 64, new GeoPoint(57.75, -152.50))),
            ItemFactory.Of(Clip.Vid("20260525092718", 85, Sites.KodiakTown)),
        };
        var splits = DaySplitFinder.Find(items);
        Assert.Equal(3, splits.Length);
        Assert.All(splits, s => Assert.False(s.Emphasised));
    }

    [Fact]
    public void DayWithoutGps_HasNoDistance()
    {
        var items = new[]
        {
            ItemFactory.Of(Clip.Vid("20260725232655", 1)),
            ItemFactory.Of(Clip.Vid("20260726235645", 2, Sites.Anvil)),
        };
        var s = Assert.Single(DaySplitFinder.Find(items));
        Assert.Null(s.Apart);
        Assert.False(s.Emphasised);
    }
}
