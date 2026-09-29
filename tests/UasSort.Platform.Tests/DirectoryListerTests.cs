using System.Security.AccessControl;

namespace UasSort.Platform.Tests;

public sealed class DirectoryListerTests
{
    private static readonly IReadOnlySet<string> NoExclusions = new HashSet<string>();
    private readonly WindowsDirectoryLister _lister = new();

    [Fact]
    public void HiddenAndSystemFiles_AreListed_WithRawAttributes()
    {
        using var t = new TempDir();
        t.File(@"DCIM\DJI_001\DJI_20260927140627_0128_D.MP4", attributes: FileAttributes.Hidden);
        t.File(@"DCIM\DJI_001\.DJI_20260927140627_0128_D.MP4.trinf", attributes: FileAttributes.Hidden);
        t.File(@"MISC\FC9113.db", attributes: FileAttributes.System);
        var r = _lister.Enumerate(t.Path, recurse: true, NoExclusions);
        Assert.Empty(r.Errors);
        var mp4 = Assert.Single(r.Entries, e => e.RelPath == @"DCIM\DJI_001\DJI_20260927140627_0128_D.MP4");
        Assert.True((mp4.RawAttributes & (uint)FileAttributes.Hidden) != 0);
        Assert.Equal(16, mp4.Size);
        Assert.Contains(r.Entries, e => e.RelPath == @"DCIM\DJI_001\.DJI_20260927140627_0128_D.MP4.trinf");
        Assert.Contains(r.Entries, e => e.RelPath == @"MISC\FC9113.db" && (e.RawAttributes & (uint)FileAttributes.System) != 0);
        Assert.Contains(r.Entries, e => e.RelPath == "DCIM" && e.IsDirectory);
    }

    [Fact]
    public void AccessDeniedFolder_IsRecordedAsAnError_AndTheRestIsListed()
    {
        using var t = new TempDir();
        t.File(@"ok\a.MP4");
        t.File(@"locked\b.MP4");
        var locked = Path.Join(t.Path, "locked");
        TempDir.Deny(locked, FileSystemRights.ListDirectory);
        var r = _lister.Enumerate(t.Path, recurse: true, NoExclusions);
        Assert.Contains(r.Errors, e => e.Path == locked && e.Win32Error == 5);
        Assert.Contains(r.Entries, e => e.RelPath == @"ok\a.MP4");
        Assert.Contains(r.Entries, e => e.RelPath == "locked" && e.IsDirectory);
        Assert.DoesNotContain(r.Entries, e => e.RelPath == @"locked\b.MP4");
    }

    [Fact]
    public void Listing_OpensNoFile()
    {
        using var t = new TempDir();
        string[] relPaths = [@"DCIM\DJI_001\a.MP4", @"DCIM\DJI_001\b.DNG", @"MISC\FC9113.db", "x.bin"];
        var files = relPaths.Select(p => t.File(p, 4096)).ToList();
        var locks = files.Select(f => new FileStream(f, FileMode.Open, FileAccess.ReadWrite, FileShare.None)).ToList();
        try
        {
            var r = _lister.Enumerate(t.Path, recurse: true, NoExclusions);
            Assert.Empty(r.Errors);
            Assert.Equal(4, r.Entries.Count(e => !e.IsDirectory && e.Size == 4096));
        }
        finally { locks.ForEach(l => l.Dispose()); }
    }

    [Fact]
    public void LibraryListing_ExcludesLedgerFolder_CaseInsensitively()
    {
        using var t = new TempDir();
        t.File(@"video\.uas-sort\ledger-A.jsonl");
        t.File(@"video\.uas-sort\sub\x.MP4");
        t.File(@"video\2026\2026-09\2026-09-27 Zachar Bay\DJI_0128.MP4");
        var root = Path.Join(t.Path, "video");
        foreach (var exclude in new[] { ".uas-sort", ".UAS-SORT" })
        {
            var r = _lister.Enumerate(root, recurse: true, new HashSet<string> { exclude });
            Assert.DoesNotContain(r.Entries, e => e.FullPath.Contains(".uas-sort", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(r.Entries, e => e.RelPath == @"2026\2026-09\2026-09-27 Zachar Bay\DJI_0128.MP4");
            Assert.Equal(4, r.Entries.Length);   // 2026, 2026-09, the event folder, the clip
        }
    }

    [Fact]
    public void LedgerStoreTopLevelListing_SeesNothingBelow()
    {
        using var t = new TempDir();
        t.File(@"video\.uas-sort\ledger-A.jsonl");
        t.File(@"video\.uas-sort\sub\x.MP4");
        var r = _lister.Enumerate(Path.Join(t.Path, "video", ".uas-sort"), recurse: false, NoExclusions);
        Assert.Equal(["ledger-A.jsonl", "sub"], r.Entries.Select(e => e.RelPath).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void MissingRoot_IsAnError_NotAnException()
    {
        using var t = new TempDir();
        var missing = Path.Join(t.Path, "gone");
        var r = _lister.Enumerate(missing, recurse: true, NoExclusions);
        Assert.Empty(r.Entries);
        var error = Assert.Single(r.Errors);
        Assert.Equal(missing, error.Path);
        Assert.True(Kernel32.IsNotFound(error.Win32Error));
    }

    [Fact]
    public void Junction_IsListedButNotEntered()
    {
        using var t = new TempDir();
        t.File(@"target\inside.MP4");
        var link = Path.Join(t.Path, "root", "link");
        Directory.CreateDirectory(Path.Join(t.Path, "root"));
        Assert.Equal(0, Cmd.Run($"mklink /J \"{link}\" \"{Path.Join(t.Path, "target")}\""));
        var r = _lister.Enumerate(Path.Join(t.Path, "root"), recurse: true, NoExclusions);
        Assert.Empty(r.Errors);   // the junction's tag was read (a failed read is an error)
        Assert.Equal("link", Assert.Single(r.Entries).RelPath);
    }

    [Fact]
    public void ReparseTag_OfAJunction_IsReadFromItsParentListing()
    {
        using var t = new TempDir();
        var target = t.Sub("target");
        var link = Path.Join(t.Path, "link");
        Assert.Equal(0, Cmd.Run($"mklink /J \"{link}\" \"{target}\""));
        using var hold = new FileStream(t.File(@"target\held.MP4"), FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.Equal(0xA0000003u, WindowsDirectoryLister.TryReadReparseTag(link, out var error));
        Assert.Equal(0, error);
        Assert.Null(WindowsDirectoryLister.TryReadReparseTag(Path.Join(t.Path, "gone"), out error));
        Assert.True(Kernel32.IsNotFound(error));
    }

    [Theory]
    [InlineData(0x10u, null, true)]                  // plain directory
    [InlineData(0x10u, 0xA0000003u, true)]           // no reparse bit: the tag is irrelevant
    [InlineData(0x410u, 0xA0000003u, false)]         // junction / mount point
    [InlineData(0x410u, 0xA000000Cu, false)]         // symbolic link
    [InlineData(0x410u, 0xA0000027u, false)]         // another name surrogate (WCI link)
    [InlineData(0x410u, 0x20000001u, false)]         // any tag with the name-surrogate bit
    [InlineData(0x410u, 0x9000001Au, true)]          // IO_REPARSE_TAG_CLOUD (OneDrive placeholder folder)
    [InlineData(0x410u, 0x9000601Au, true)]          // IO_REPARSE_TAG_CLOUD_6
    [InlineData(0x410u, 0x9000101Au, true)]          // IO_REPARSE_TAG_CLOUD_1
    [InlineData(0x80410u, 0x9000601Au, true)]        // pinned cloud folder
    [InlineData(0x410u, null, false)]                // reparse point whose tag could not be read
    public void ShouldDescend_SkipsOnlyNameSurrogates(uint attributes, uint? reparseTag, bool expected)
        => Assert.Equal(expected, WindowsDirectoryLister.ShouldDescend(attributes, reparseTag));

    [Fact]
    public void FileRoot_IsAnError_AndIsNeverOpened()
    {
        using var t = new TempDir();
        var path = t.File("DJI_0128.MP4");
        using var hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        var r = _lister.Enumerate(path, recurse: false, NoExclusions);
        Assert.Empty(r.Entries);
        Assert.Equal([(Path.GetFullPath(path), 267)], r.Errors.ToArray());
    }
}
