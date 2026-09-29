using System.Security.AccessControl;

namespace UasSort.Platform.Tests;

public sealed class CardReaderTests
{
    private static ICardReader Open(TestEnv env)
    {
        var identity = VolumeQuery.Identity(VolumeQuery.VolumePathName(env.CardRoot)!)!;
        var source = new CardSource(env.C(env.CardRoot), identity, IsBrowsedFolder: false, IsWriteProtected: false);
        return new WindowsCardReaderFactory(env.Settings, env.AppData, TestEnv.Machine, env.Facts, new WindowsDirectoryLister())
            .Open(source, identity);
    }

    [Fact]
    public void Reader_OpensFilesWhoseAclDeniesAllWriteRights()
    {
        using var env = new TestEnv();
        var f = env.Temp.File(@"card\DCIM\DJI_001\DJI_20260927140627_0128_D.MP4", 8192);
        TempDir.Deny(f, FileSystemRights.Write | FileSystemRights.Delete | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership);
        var reader = Open(env);
        var expected = File.ReadAllBytes(f);
        using (var s = reader.OpenSequential(@"DCIM\DJI_001\DJI_20260927140627_0128_D.MP4"))
        {
            var got = new byte[8192];
            s.ReadExactly(got);
            Assert.Equal(expected, got);
            Assert.False(s.CanWrite);
        }
        using (var r = reader.OpenRandom("DCIM/DJI_001/DJI_20260927140627_0128_D.MP4"))
        {
            r.Position = 4096;
            Assert.Equal(expected[4096], (byte)r.ReadByte());
        }
    }

    [Fact]
    public void Reader_RefusesPathsOutsideTheCard()
    {
        using var env = new TestEnv();
        env.Temp.File(@"photo\DJI_0001.DNG");
        env.Temp.File("outside.bin");
        var reader = Open(env);
        Assert.Throws<UnsafeIoException>(() => reader.OpenRandom(@"..\photo\DJI_0001.DNG"));
        Assert.Throws<UnsafeIoException>(() => reader.OpenSequential(@"..\outside.bin"));
    }

    [Fact]
    public void Reader_RefusesAPlaceholderOnTheCard()
    {
        using var env = new TestEnv();
        env.Temp.File(@"card\DCIM\DJI_001\x.MP4", attributes: FileAttributes.Offline);
        Assert.Throws<UnsafeIoException>(() => Open(env).OpenRandom(@"DCIM\DJI_001\x.MP4"));
    }

    [Fact]
    public void Stat_ReportsSizeTimesAndAttributes_WithoutOpening()
    {
        using var env = new TestEnv();
        var f = env.Temp.File(@"card\DCIM\DJI_001\a.DNG", 100, FileAttributes.Hidden);
        using var hold = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.None);
        var e = Open(env).Stat(@"DCIM\DJI_001\a.DNG");
        Assert.Equal(100, e.Size);
        Assert.Equal(new FileInfo(f).LastWriteTimeUtc, e.MtimeUtc);
        Assert.True((e.RawAttributes & (uint)FileAttributes.Hidden) != 0);
        Assert.Equal(@"DCIM\DJI_001\a.DNG", e.RelPath);
        Assert.Throws<FileNotFoundException>(() => Open(env).Stat(@"DCIM\DJI_001\gone.DNG"));
    }

    [Fact]
    public void Relist_Identity_And_Space()
    {
        using var env = new TestEnv();
        env.Temp.File(@"card\DCIM\DJI_001\.x.MP4.trinf", attributes: FileAttributes.Hidden);
        var reader = Open(env);
        Assert.Contains(reader.Relist().Entries, e => e.RelPath == @"DCIM\DJI_001\.x.MP4.trinf");
        Assert.Equal(VolumeQuery.Identity(VolumeQuery.VolumePathName(env.CardRoot)!)!, reader.CurrentIdentity());
        var space = reader.Space();
        Assert.True(space.FreeBytes > 0 && space.TotalBytes >= space.FreeBytes && space.ClusterBytes >= 512);
    }
}
