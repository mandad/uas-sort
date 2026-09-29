using UasSort.Core.Library;
using UasSort.Core;

namespace UasSort.Core.Tests.Library;

public sealed class EventFolderLocatorTests
{
    private const string V = @"C:\Lib\UAS Videos";
    private static readonly string[] Photos = [V + @"\Picture Offload", @"D:\Old Offload"];
    private const string Council = V + @"\2026\2026-07\2026-07-25 Council Road";

    private static LibraryFolderRef? Locate(string path) => EventFolderLocator.For(path, V, Photos, includeSelf: false);

    [Fact]
    public void EventFolderLocator_FileInDatedFolder_GetsIt()
    {
        LibraryFolderRef? f = Locate(Council + @"\DJI_20260725232655_0117_D.MP4");
        Assert.NotNull(f);
        Assert.Equal(Council, f.FullPath);
        Assert.Equal(new DateOnly(2026, 7, 25), f.NameDate);
        Assert.Equal("Council Road", f.Description);
    }

    [Theory]
    [InlineData(V + @"\2026\DJI_20260801120000_0001_D.MP4")]       // stray MP4 at 2026\
    [InlineData(V + @"\Exports\Council edit.mp4")]                   // non-event folder
    [InlineData(V + @"\desktop.ini")]                                // loose file at the root
    [InlineData(V + @"\2026\2026-07\desktop.ini")]                   // month folder is not an event folder
    [InlineData(V + @"\Picture Offload\2026-05-25 Pano\PANO_0001.DNG")]   // dated folder inside the photo root
    [InlineData(V + @"\Picture Offload\DJI_20260815200000_0119_D.DNG")]
    [InlineData(V + @"\.uas-sort\2026-10-01 Stray\x.MP4")]           // ledger folder
    [InlineData(@"D:\Old Offload\2026-01-01 Old\x.DNG")]             // previous photo root
    [InlineData(@"E:\Elsewhere\2026-01-01 X\x.MP4")]                 // outside the video root
    public void EventFolderLocator_NonEventLocations_GetNone(string path) => Assert.Null(Locate(path));

    [Fact]
    public void EventFolderLocator_AnyDepth_NearestAncestorWins()
    {
        Assert.Equal(V + @"\2022\2022-03-27 Makaha Valley", Locate(V + @"\2022\2022-03-27 Makaha Valley\MAX_0061.MP4")?.FullPath);
        Assert.Equal(Council, Locate(Council + @"\sub\deeper\x.MP4")?.FullPath);
        Assert.Equal(V + @"\2026-07-25 Trip\2026-07-26 Day2", Locate(V + @"\2026-07-25 Trip\2026-07-26 Day2\x.MP4")?.FullPath);
    }

    [Fact]
    public void EventFolderLocator_DirectoryItself_WithIncludeSelf()
    {
        Assert.Equal(Council, EventFolderLocator.For(Council, V, Photos, includeSelf: true)?.FullPath);
        Assert.Null(EventFolderLocator.For(V + @"\Exports", V, Photos, includeSelf: true));
        Assert.Null(EventFolderLocator.For(V, V, Photos, includeSelf: true));
    }
}
