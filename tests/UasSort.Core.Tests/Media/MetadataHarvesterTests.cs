using UasSort.Core.Media;
using UasSort.Testing;
using static UasSort.Core.Tests.Media.ProbeAssert;

namespace UasSort.Core.Tests.Media;

public sealed class MetadataHarvesterTests
{
    private static readonly DateTime T = new(2026, 9, 27, 18, 8, 0, DateTimeKind.Utc);
    private const string VideoPath = "DCIM/DJI_001/DJI_20260927140627_0128_D.MP4";
    private const string DngPath = "DCIM/DJI_001/DJI_20260927141000_0129_D.DNG";
    private const string JpgPath = "DCIM/DJI_001/DJI_20260927141000_0129_D.JPG";
    private const string Pano1 = "DCIM/PANORAMA/001_0087/PANO_0001.DNG";
    private const string Pano2 = "DCIM/PANORAMA/001_0087/PANO_0002.DNG";

    private sealed class ScanProgressLog : IProgress<ScanProgress>
    {
        public List<ScanProgress> Reports { get; } = [];
        public void Report(ScanProgress value) => Reports.Add(value);
    }

    private static CardEntry Entry(string rel, long size, EntryClass cls) => new(rel, size, T, T, T, 0x20, cls, null);

    internal static (CardInventory Inventory, MemoryCardReader Reader) Card()
    {
        byte[] mp4 = new SyntheticMp4Builder().Build();
        byte[] dng = new SyntheticDngBuilder { Dto = new DateTime(2026, 9, 27, 14, 10, 0) }.Build();
        byte[] pano1 = new SyntheticDngBuilder().Build();
        byte[] pano2 = new SyntheticDngBuilder { Dto = new DateTime(2026, 5, 25, 9, 30, 31) }.Build();
        var reader = new MemoryCardReader(new CardIdentity(0x1234ABCD, "SD", "exFAT", 128_000_000_000))
            .Add(VideoPath, mp4).Add(DngPath, dng).Add(JpgPath, new byte[500]).Add(Pano1, pano1).Add(Pano2, pano2);
        var video = new VideoUnit(new ItemId(VideoPath), Entry(VideoPath, mp4.Length, EntryClass.Video), false);
        var photo = new PhotoUnit(new ItemId(DngPath), Entry(DngPath, dng.Length, EntryClass.Photo), Entry(JpgPath, 500, EntryClass.PhotoTwin));
        var set = new SetUnit(new ItemId("DCIM/PANORAMA/001_0087"), SetKind.Panorama, "001_0087",
                              [Entry(Pano2, pano2.Length, EntryClass.SetMember), Entry(Pano1, pano1.Length, EntryClass.SetMember)]);
        var inventory = new CardInventory(new CardSource("E:\\", null, true, false), T, "0000000000000000", [],
                                          [photo, set, video], null, []);
        return (inventory, reader);
    }

    private static async Task<List<RawItem>> Harvest(CardInventory inventory, MemoryCardReader reader, ScanProgressLog progress)
    {
        var items = new List<RawItem>();
        await foreach (RawItem item in MetadataHarvester.HarvestAsync(inventory, reader, progress, TestContext.Current.CancellationToken))
            items.Add(item);
        return items;
    }

    [Theory]
    [InlineData("DJI_20260927140627_0128_D.MP4")]
    [InlineData("DCIM/DJI_001/DJI_20260927140627_0128_D.MP4")]
    [InlineData("DCIM\\DJI_001\\DJI_20260927140627_0128_D.MP4")]
    public void DroneStampParser_ReadsTheFileNameStamp(string name)
    {
        DateTime? stamp = DroneStampParser.FromFileName(name);

        Assert.Equal(new DateTime(2026, 9, 27, 14, 6, 27), stamp);
        Assert.Equal(DateTimeKind.Unspecified, stamp!.Value.Kind);
    }

    [Theory]
    [InlineData("PANO_0001.DNG")]
    [InlineData("DJI_20261399999999_0001_D.MP4")]
    [InlineData("MAX_0061.MP4")]
    public void DroneStampParser_NoStamp_IsNull(string name) => Assert.Null(DroneStampParser.FromFileName(name));

    [Fact]
    public async Task HarvestAsync_ReadsVideosThenPhotosThenSets()
    {
        (CardInventory inventory, MemoryCardReader reader) = Card();
        var progress = new ScanProgressLog();

        List<RawItem> items = await Harvest(inventory, reader, progress);

        Assert.Equal([ItemKind.Video, ItemKind.Photo, ItemKind.Set], items.Select(i => i.Kind));
        Assert.Equal([VideoPath, DngPath, Pano1], reader.OpenLog);
        Assert.Equal(0, reader.OpenHandles);
        Assert.All(progress.Reports, r => Assert.Equal(ScanPhase.ReadingMetadata, r.Phase));
        Assert.Equal([0, 1, 2, 3], progress.Reports.Select(r => r.Done));
        Assert.Equal(VideoPath, progress.Reports[0].Current);
        Assert.Null(progress.Reports[^1].Current);
    }

    [Fact]
    public async Task HarvestAsync_FillsEachRawItem()
    {
        (CardInventory inventory, MemoryCardReader reader) = Card();

        List<RawItem> items = await Harvest(inventory, reader, new ScanProgressLog());

        RawItem video = items[0];
        Assert.Equal("DJI_20260927140627_0128_D.MP4", video.Name);
        Assert.Equal(new DateTime(2026, 9, 27, 14, 6, 27), video.DroneStamp);
        Assert.Equal(T, video.CardMtimeUtc);
        Assert.Equal(GpsSource.DjmdModelTable, Fix(video.Mp4!.First).Source);
        Assert.Null(video.Still);
        Assert.Null(video.ProbeError);

        RawItem photo = items[1];
        Assert.Equal("DJI_20260927141000_0129_D.DNG", photo.Name);
        Assert.Equal(new DateTime(2026, 9, 27, 14, 10, 0), photo.DroneStamp);
        Assert.Equal(((PhotoUnit)photo.Unit).Primary.Size + 500, photo.Bytes);
        Assert.NotNull(photo.Still);

        RawItem set = items[2];
        Assert.Equal("001_0087", set.Name);
        Assert.Equal(new DateTime(2026, 5, 25, 9, 30, 28), set.DroneStamp);
        Assert.Equal(((SetUnit)set.Unit).Members.Sum(m => m.Size), set.Bytes);
    }

    [Fact]
    public async Task HarvestAsync_ProbeFailuresBecomeProbeError()
    {
        (CardInventory inventory, MemoryCardReader reader) = Card();
        reader.FailOpen(VideoPath).Add(DngPath, [.. "not an image"u8.ToArray(), .. new byte[64]]);

        List<RawItem> items = await Harvest(inventory, reader, new ScanProgressLog());

        Assert.StartsWith("IOException:", items[0].ProbeError);
        Assert.Null(items[0].Mp4);
        Assert.Equal(new DateTime(2026, 9, 27, 14, 6, 27), items[0].DroneStamp);
        Assert.StartsWith("ImageProcessingException:", items[1].ProbeError);
        Assert.Null(items[1].Still);
        Assert.Equal(new DateTime(2026, 9, 27, 14, 10, 0), items[1].DroneStamp);
        Assert.Null(items[2].ProbeError);
        Assert.Equal(0, reader.OpenHandles);
    }

    [Fact]
    public async Task HarvestAsync_Cancelled_Throws()
    {
        (CardInventory inventory, MemoryCardReader reader) = Card();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (RawItem _ in MetadataHarvester.HarvestAsync(inventory, reader, new ScanProgressLog(), cts.Token)) { }
        });
    }

    [Fact]
    public void FirstFrame_IsTheLowestFileName()
    {
        (CardInventory inventory, _) = Card();
        SetUnit set = inventory.Units.OfType<SetUnit>().Single();

        Assert.Equal(Pano1, MetadataHarvester.FirstFrame(set).RelPath);
    }
}
