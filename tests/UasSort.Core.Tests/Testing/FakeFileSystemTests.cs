using System.Text;

namespace UasSort.Core.Tests.Testing;

public class FakeFileSystemTests
{
    private static readonly DateTime T = new(2026, 9, 27, 18, 8, 1, DateTimeKind.Utc);
    private const string V = FakeLayout.VideoRoot;
    private static readonly string L = LedgerPaths.For(V);
    private const string LibraryFile = V + @"\2026\2026-07\2026-07-25 Council Road\DJI_20260725232655_0117_D.MP4";

    private static FakeFileSystem Library()
    {
        var fs = FakeLayout.NewFileSystem();
        fs.AddFile(LibraryFile, 1_000, T, FakeFileSystem.CloudOnlyPlaceholder);
        fs.AddFile(V + @"\2026\2026-07\2026-07-25 Council Road\DJI_20260725232655_0117_D.LRF", 10, T, 0x20);
        fs.AddFile(L + @"\ledger-A.jsonl", Encoding.UTF8.GetBytes("{}\n"), T);
        fs.AddFile(L + @"\notes.txt", Encoding.UTF8.GetBytes("x"), T);
        fs.AddFile(L + @"\sub\ledger-C.jsonl", Encoding.UTF8.GetBytes("{}\n"), T);
        return fs;
    }

    [Fact]
    public void Lister_ReturnsHiddenAndSystemFiles_AndSkipsExcludedDirectories()
    {
        var fs = Library();
        fs.AddFile(V + @"\2026\2026-07\2026-07-25 Council Road\.DJI_20260727002013_0014_D.MP4.trinf", 5, T, 0x2 | 0x4 | 0x20);
        var listing = fs.Enumerate(V, recurse: true, ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, ".uas-sort"));
        var rel = listing.Entries.Select(e => e.RelPath).ToList();
        Assert.Contains(@"2026\2026-07\2026-07-25 Council Road\.DJI_20260727002013_0014_D.MP4.trinf", rel);
        Assert.Contains(@"2026\2026-07\2026-07-25 Council Road", rel);
        Assert.DoesNotContain(rel, r => r.Contains(".uas-sort", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(listing.Errors);
        Assert.Empty(fs.GuardLog);   // listing opens nothing
    }

    [Fact]
    public void Lister_NonRecursive_ListsOnlyTopLevel()
    {
        var fs = Library();
        var listing = fs.Enumerate(L, recurse: false, ImmutableHashSet<string>.Empty);
        string[] expected = ["ledger-A.jsonl", "notes.txt", "sub"];
        Assert.Equal(expected, listing.Entries.Select(e => e.RelPath).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Lister_RecordsEnumerationErrors_AndDoesNotEnterTheFolder()
    {
        var fs = FakeLayout.NewFileSystem();
        fs.AddFile(@"E:\DCIM\DJI_001\DJI_20260927140627_0128_D.MP4", 100, T);
        fs.AddFile(@"E:\DCIM\DJI_002\DJI_20260927150000_0200_D.MP4", 100, T);
        fs.Faults.EnumerationErrors[@"E:\DCIM\DJI_002"] = 5;
        var listing = fs.Enumerate(@"E:\", true, ImmutableHashSet<string>.Empty);
        Assert.Equal((@"E:\DCIM\DJI_002", 5), Assert.Single(listing.Errors));
        Assert.Contains(listing.Entries, e => e.RelPath == @"DCIM\DJI_002" && e.IsDirectory);
        Assert.DoesNotContain(listing.Entries, e => e.RelPath.StartsWith(@"DCIM\DJI_002\", StringComparison.Ordinal));
    }

    [Fact]
    public void Lister_ReportsAMissingRootAsError3()
        => Assert.Equal(3, Assert.Single(FakeLayout.NewFileSystem().Enumerate(@"F:\", true, ImmutableHashSet<string>.Empty).Errors).Win32Error);

    [Fact]
    public void OpenRead_OfALocalTopLevelLedgerFile_IsAllowed()
    {
        var fs = Library();
        using var s = fs.OpenRead(L + @"\ledger-A.jsonl");
        using var r = new StreamReader(s);
        Assert.Equal("{}\n", r.ReadToEnd());
        fs.AssertNoViolations();
    }

    [Theory]
    [InlineData(LibraryFile)]                                                                     // placeholder bits
    [InlineData(V + @"\2026\2026-07\2026-07-25 Council Road\DJI_20260725232655_0117_D.LRF")]    // local library file
    [InlineData(V + @"\.uas-sort\notes.txt")]
    [InlineData(V + @"\.uas-sort\sub\ledger-C.jsonl")]
    public void OpenRead_OfAnyOtherLibraryFile_FiresTheHydrationTripwire(string path)
    {
        var fs = Library();
        Assert.Throws<HydrationViolation>(() => fs.OpenRead(path));
        Assert.Equal(PathRules.Normalize(path), Assert.Single(fs.HydrationViolations));
        Assert.Throws<InvalidOperationException>(fs.AssertNoViolations);
    }

    [Fact]
    public void OpenRead_OfACloudOnlyLedgerFile_IsAViolationToo()
    {
        var fs = Library();
        fs.SetAttributes(L + @"\ledger-A.jsonl", 0x400020);
        Assert.Throws<HydrationViolation>(() => fs.OpenRead(L + @"\ledger-A.jsonl"));
        Assert.Equal("CloudOnly", fs.GuardLog[^1].Decision);
    }

    [Fact]
    public void OpenAppend_CreatesAndAppendsTheOwnLedgerFileOnly()
    {
        var fs = Library();
        var own = LedgerPaths.OwnFile(V, FakeLayout.Machine);
        using (var s = fs.OpenAppend(own)) s.Write("{\"a\":1}\n"u8);
        using (var s = fs.OpenAppend(own)) s.Write("{\"b\":2}\n"u8);
        Assert.Equal("{\"a\":1}\n{\"b\":2}\n", Encoding.UTF8.GetString(fs.PeekContent(own)));
        Assert.ThrowsAny<UnsafeIoException>(() => fs.OpenAppend(L + @"\ledger-A.jsonl"));
    }

    [Fact]
    public void OpenAppend_WithTheAppendFailsFault_ThrowsIOExceptionOnWrite()
    {
        var fs = Library();
        var own = LedgerPaths.OwnFile(V, FakeLayout.Machine);
        fs.Faults.AppendFails.Add(own);
        using var s = fs.OpenAppend(own);
        Assert.Throws<IOException>(() => s.Write("{}\n"u8));
    }

    [Fact]
    public void CreateDirectory_And_SetPinned_FollowTheLedgerExemption()
    {
        var fs = FakeLayout.NewFileSystem();
        fs.CreateDirectory(L);
        fs.SetPinned(L);
        Assert.Equal(0x80000u, fs.GetAttributes(L)!.Value & 0x80000u);
        Assert.Throws<UnsafeIoException>(() => fs.CreateDirectory(L + @"\sub"));
        Assert.False(fs.Exists(L + @"\sub"));
    }

    [Fact]
    public void PatternContent_IsDeterministicAndOfTheGivenSize()
    {
        var fs = FakeLayout.NewFileSystem();
        fs.AddFile(FakeLayout.AppDataDir + @"\x.bin", 3_000_000, T);
        var a = fs.PeekContent(FakeLayout.AppDataDir + @"\x.bin");
        Assert.Equal(3_000_000, a.Length);
        Assert.Equal(a, fs.PeekContent(FakeLayout.AppDataDir + @"\x.bin"));
        Assert.Equal(3_000_000, fs.Metadata(FakeLayout.AppDataDir + @"\x.bin")!.Size);
    }
}
