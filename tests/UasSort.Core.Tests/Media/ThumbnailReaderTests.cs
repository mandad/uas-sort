using UasSort.Core.Media;
using UasSort.Testing;

namespace UasSort.Core.Tests.Media;

public sealed class ThumbnailReaderTests
{
    private static (ThumbnailReader Thumbs, MemoryCardReader Reader, List<RawItem> Items) Setup()
    {
        (CardInventory inventory, MemoryCardReader reader) = MetadataHarvesterTests.Card();
        List<RawItem> items = [.. inventory.Units.Select(u => MetadataHarvester.Harvest(u, reader))];
        return (new ThumbnailReader(reader, items), reader, items);
    }

    private static ItemId Id(List<RawItem> items, ItemKind kind) => items.Single(i => i.Kind == kind).Unit.Id;

    [Theory]
    [InlineData(ItemKind.Video)]
    [InlineData(ItemKind.Photo)]
    [InlineData(ItemKind.Set)]
    public async Task GetAsync_ReturnsTheStoredJpeg(ItemKind kind)
    {
        (ThumbnailReader thumbs, _, List<RawItem> items) = Setup();
        using (thumbs)
        {
            ReadOnlyMemory<byte> jpeg = await thumbs.GetAsync(Id(items, kind), TestContext.Current.CancellationToken);

            Assert.Equal(SyntheticMp4Builder.TinyJpeg, jpeg.ToArray());
        }
    }

    [Fact]
    public async Task GetAsync_SetReadsItsFirstFrame()
    {
        (ThumbnailReader thumbs, MemoryCardReader reader, List<RawItem> items) = Setup();
        using (thumbs)
        {
            await thumbs.GetAsync(Id(items, ItemKind.Set), TestContext.Current.CancellationToken);

            Assert.EndsWith("PANO_0001.DNG", reader.OpenLog[^1]);
        }
    }

    [Fact]
    public async Task GetAsync_UnknownItem_IsEmpty()
    {
        (ThumbnailReader thumbs, _, _) = Setup();
        using (thumbs)
        {
            Assert.True((await thumbs.GetAsync(new ItemId("DCIM/DJI_001/nope.MP4"), TestContext.Current.CancellationToken)).IsEmpty);
        }
    }

    [Fact]
    public async Task GetAsync_SameFileTwice_OpensItOnce()
    {
        (ThumbnailReader thumbs, MemoryCardReader reader, List<RawItem> items) = Setup();
        using (thumbs)
        {
            int before = reader.OpenLog.Count;

            await thumbs.GetAsync(Id(items, ItemKind.Video), TestContext.Current.CancellationToken);
            await thumbs.GetAsync(Id(items, ItemKind.Video), TestContext.Current.CancellationToken);

            Assert.Equal(before + 1, reader.OpenLog.Count);
            Assert.Equal(1, reader.OpenHandles);
        }
    }

    [Fact]
    public async Task Pause_ClosesTheHandleAndReturnsEmptyUntilEveryPauseIsDisposed()
    {
        (ThumbnailReader thumbs, MemoryCardReader reader, List<RawItem> items) = Setup();
        using (thumbs)
        {
            ItemId video = Id(items, ItemKind.Video);
            await thumbs.GetAsync(video, TestContext.Current.CancellationToken);

            IDisposable first = thumbs.Pause();
            IDisposable second = thumbs.Pause();
            Assert.Equal(0, reader.OpenHandles);
            int opens = reader.OpenLog.Count;

            Assert.True((await thumbs.GetAsync(video, TestContext.Current.CancellationToken)).IsEmpty);
            first.Dispose();
            first.Dispose();
            Assert.True((await thumbs.GetAsync(video, TestContext.Current.CancellationToken)).IsEmpty);
            Assert.Equal(opens, reader.OpenLog.Count);

            second.Dispose();
            Assert.Equal(SyntheticMp4Builder.TinyJpeg, (await thumbs.GetAsync(video, TestContext.Current.CancellationToken)).ToArray());
        }
    }

    [Fact]
    public async Task GetAsync_ReadError_IsEmpty()
    {
        (ThumbnailReader thumbs, MemoryCardReader reader, List<RawItem> items) = Setup();
        using (thumbs)
        {
            reader.FailOpen("DCIM/DJI_001/DJI_20260927140627_0128_D.MP4");

            Assert.True((await thumbs.GetAsync(Id(items, ItemKind.Video), TestContext.Current.CancellationToken)).IsEmpty);
        }
    }

    [Fact]
    public async Task Dispose_ClosesTheCachedHandle()
    {
        (ThumbnailReader thumbs, MemoryCardReader reader, List<RawItem> items) = Setup();

        await thumbs.GetAsync(Id(items, ItemKind.Photo), TestContext.Current.CancellationToken);
        thumbs.Dispose();

        Assert.Equal(0, reader.OpenHandles);
    }
}
