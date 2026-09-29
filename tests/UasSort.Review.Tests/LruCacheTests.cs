// tests/UasSort.Review.Tests/LruCacheTests.cs  (UasSort.Review comes from GlobalUsings.cs, Xunit from the csproj)
namespace UasSort.Review.Tests;

public sealed class LruCacheTests
{
    [Fact]
    public void Set_BeyondCapacity_EvictsLeastRecentlyUsed()
    {
        var c = new LruCache<string, int>(2);
        c.Set("a", 1); c.Set("b", 2);
        Assert.True(c.TryGet("a", out _));   // a becomes most recent
        c.Set("c", 3);                       // evicts b
        Assert.False(c.TryGet("b", out _));
        Assert.True(c.TryGet("a", out var a)); Assert.Equal(1, a);
        Assert.True(c.TryGet("c", out var cc)); Assert.Equal(3, cc);
        Assert.Equal(2, c.Count);
    }

    [Fact]
    public void Set_ExistingKey_ReplacesValueWithoutGrowing()
    {
        var c = new LruCache<string, int>(2);
        c.Set("a", 1); c.Set("a", 5);
        Assert.Equal(1, c.Count);
        Assert.True(c.TryGet("a", out var v)); Assert.Equal(5, v);
    }

    [Fact]
    public void Capacity400_HoldsExactly400()
    {
        var c = new LruCache<int, int>(400);
        for (int i = 0; i < 1000; i++) c.Set(i, i);
        Assert.Equal(400, c.Count);
        Assert.False(c.TryGet(599, out _));
        Assert.True(c.TryGet(600, out _));
        Assert.True(c.TryGet(999, out _));
    }

    [Fact]
    public void Clear_EmptiesTheCache()
    {
        var c = new LruCache<int, int>(3);
        c.Set(1, 1); c.Clear();
        Assert.Equal(0, c.Count);
        Assert.False(c.TryGet(1, out _));
    }

    [Fact]
    public void Ctor_RejectsNonPositiveCapacity() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new LruCache<int, int>(0));
}
