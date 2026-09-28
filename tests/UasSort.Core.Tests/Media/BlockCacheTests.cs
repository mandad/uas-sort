using UasSort.Core.Media;
using UasSort.Testing;

namespace UasSort.Core.Tests.Media;

public sealed class BlockCacheTests
{
    private static byte[] Pattern(int n) => [.. Enumerable.Range(0, n).Select(i => (byte)(i % 251))];

    [Fact]
    public void ReadAt_ReturnsExactBytesAcrossABlockBoundary()
    {
        byte[] data = Pattern(10_000);
        var cache = new BlockCache(new MemoryStream(data));

        byte[] got = cache.ReadAt(4090, 20);

        Assert.Equal(data[4090..4110], got);
        Assert.Equal(2, cache.BlockReads);
    }

    [Fact]
    public void ReadAt_ReadsEachAlignedBlockOnce()
    {
        var counting = new CountingStream(new MemoryStream(Pattern(10_000)));
        var cache = new BlockCache(counting);

        cache.ReadAt(100, 8);
        cache.ReadAt(200, 16);
        cache.ReadAt(4090, 20);
        cache.ReadAt(5000, 4);

        (long, int)[] expected = [(0, 4096), (4096, 4096)];
        Assert.Equal(expected, counting.Reads);
    }

    [Fact]
    public void ReadAt_ClampsAtEndOfStream()
    {
        byte[] data = Pattern(5_000);
        var cache = new BlockCache(new MemoryStream(data));

        Assert.Equal(data[4990..], cache.ReadAt(4990, 100));
        Assert.Empty(cache.ReadAt(6000, 4));
        Assert.Equal(5_000, cache.Length);
    }
}
