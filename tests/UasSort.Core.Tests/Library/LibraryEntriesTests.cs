using UasSort.Core.Library;
using static UasSort.Core.Tests.Library.LibraryFixture;

namespace UasSort.Core.Tests.Library;

public sealed class LibraryEntriesTests
{
    private const string V = VideoRoot;

    [Fact]
    public void LibraryEntries_DropTheLedgerSubtreeEvenIfListed()
    {
        var listings = new LibraryFixture()
            .WithFile(V + @"\.uas-sort\ledger-A.jsonl", 10, T)
            .WithFile(V + @"\.uas-sort\sub\DJI_20261001120000_0200_D.MP4", 5_000, T)
            .WithFile(V + @"\.uas-sort2\DJI_20261002120000_0300_D.MP4", 6_000, T)
            .Build();
        CollectedLibrary c = LibraryEntries.Collect(listings);
        Assert.Equal(new[] { V + @"\.uas-sort2\DJI_20261002120000_0300_D.MP4" }, c.Files.Select(f => f.Entry.FullPath));
        Assert.DoesNotContain(c.Directories, d => PathRules.IsSameOrUnder(d.Entry.FullPath, V + @"\.uas-sort"));
        Assert.Contains(c.Directories, d => PathRules.Equal(d.Entry.FullPath, V + @"\.uas-sort2"));
    }

    [Fact]
    public void LibraryEntries_PhotoRootInsideVideoRoot_IsListedOnce()
    {
        var listings = new LibraryFixture().WithFile(PhotoRoot + @"\DJI_20260815200000_0119_D.DNG", 27_000_000, T).Build();
        Assert.Single(LibraryEntries.Collect(listings).Files);
    }

    [Fact]
    public void LibraryEntries_UnavailableRoots_AreReportedAndSkipped()
    {
        var listings = new LibraryFixture()
            .WithPrevious(@"D:\Old Offload").WithUnavailable(@"D:\Old Offload")
            .WithFile(@"D:\Old Offload\DJI_20250101120000_0001_D.DNG", 1, T)
            .Build();
        CollectedLibrary c = LibraryEntries.Collect(listings);
        Assert.Equal(@"D:\Old Offload", Assert.Single(c.UnavailableRoots));
        Assert.Empty(c.Files);
    }

    [Fact]
    public void LibraryEntries_ListingErrors_AreCarriedExceptUnderTheLedgerFolder()
    {
        var listings = new LibraryFixture()
            .WithDir(V + @"\2026\locked").WithError(V + @"\2026\locked", 5)
            .WithDir(V + @"\.uas-sort\sub").WithError(V + @"\.uas-sort\sub", 5)
            .Build();
        Assert.Equal(new[] { (V + @"\2026\locked", 5) }, LibraryEntries.Collect(listings).Errors.Select(e => (e.Path, e.Win32Error)));
    }
}
