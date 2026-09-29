using UasSort.Core;
using UasSort.Core.Offload;

namespace UasSort.Core.Tests.Offload;

public class CardDiffTests
{
    private static readonly DateTime T = new(2026, 9, 27, 18, 0, 0, DateTimeKind.Utc);

    private static CardEntry E(string rel, long size = 10, uint attrs = 0x20)
        => new(rel, size, T, T, T, attrs, EntryClass.Video, null);

    private static FsEntry F(string rel, long size = 10, uint attrs = 0x20, DateTime? mtime = null, DateTime? creation = null,
                             DateTime? access = null, bool dir = false)
        => new(@"E:\" + rel.Replace('/', '\\'), rel.Replace('/', '\\'), dir, size, mtime ?? T, creation ?? T, access ?? T, attrs);

    private static ListingResult L(params FsEntry[] entries) => new([.. entries], []);

    [Fact]
    public void IdenticalListing_IsEmpty()
    {
        var d = CardDiff.Compare([E("DCIM/DJI_001/a.MP4")], L(F("DCIM/DJI_001/a.MP4"), F("DCIM", dir: true)));

        Assert.True(d.IsEmpty);
        Assert.Equal(0, d.LastAccessOnly);
    }

    [Theory]
    [InlineData("size")]
    [InlineData("mtime")]
    [InlineData("creation")]
    [InlineData("attributes")]
    public void AnyComparedFieldChanged_IsAChange(string field)
    {
        var now = field switch
        {
            "size" => F("DCIM/DJI_001/a.MP4", size: 11),
            "mtime" => F("DCIM/DJI_001/a.MP4", mtime: T.AddSeconds(2)),
            "creation" => F("DCIM/DJI_001/a.MP4", creation: T.AddSeconds(2)),
            _ => F("DCIM/DJI_001/a.MP4", attrs: 0x21),
        };

        var d = CardDiff.Compare([E("DCIM/DJI_001/a.MP4")], L(now));

        Assert.Equal("DCIM/DJI_001/a.MP4", Assert.Single(d.Changed).RelPath);
        Assert.Equal(CardDiffResult.ChangedDetail, d.Touched("dcim/dji_001/A.mp4"));
    }

    [Fact]
    public void AddedAndRemovedFiles_AreChanges()
    {
        var d = CardDiff.Compare([E("DCIM/DJI_001/a.MP4"), E("DCIM/DJI_001/gone.MP4")],
                                 L(F("DCIM/DJI_001/a.MP4"), F("DCIM/DJI_001/new.MP4", size: 5)));

        Assert.Equal(new CardDiffEntry("DCIM/DJI_001/new.MP4", 5), Assert.Single(d.Added));
        Assert.Equal(new CardDiffEntry("DCIM/DJI_001/gone.MP4", 10), Assert.Single(d.Removed));
        Assert.Equal(CardDiffResult.RemovedDetail, d.Touched("DCIM/DJI_001/gone.MP4"));
        Assert.Null(d.Touched("DCIM/DJI_001/a.MP4"));
        Assert.StartsWith(CardDiffResult.ChangedDetail, CardDiffResult.AddedDetail, StringComparison.Ordinal);
    }

    [Fact]
    public void LastAccessOnly_IsCountedButNotAChange()
    {
        var d = CardDiff.Compare([E("DCIM/DJI_001/a.MP4")], L(F("DCIM/DJI_001/a.MP4", access: T.AddDays(1))));

        Assert.True(d.IsEmpty);
        Assert.Equal(1, d.LastAccessOnly);
    }

    [Fact]
    public void SystemVolumeInformation_IsIgnored()
    {
        var d = CardDiff.Compare([E("System Volume Information/WPSettings.dat")],
                                 L(F("System Volume Information/IndexerVolumeGuid", size: 76)));

        Assert.True(d.IsEmpty);
    }
}
