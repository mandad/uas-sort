namespace UasSort.Platform.Tests;

public sealed class VolumeTests
{
    [Fact]
    public void Provider_ReportsTheSystemVolume()
    {
        var system = KnownFolders.SystemVolumeRoot();
        var v = Assert.Single(new WindowsVolumeProvider().GetVolumes(), v => string.Equals(v.Root, system, StringComparison.OrdinalIgnoreCase));
        Assert.True(v.IsReady);
        Assert.True(v.IsSystemBootOrPaging);
        Assert.Equal("Fixed", v.DriveType);
        Assert.NotEqual(0u, v.Identity.VolumeSerial);
        Assert.True(v.Identity.TotalBytes > 0 && v.FreeBytes > 0);
        Assert.Equal(v.Identity.FileSystem == "NTFS", v.IsNtfs);
        Assert.False(string.IsNullOrEmpty(v.BusType));
    }

    [Fact]
    public void VolumePathName_OfATempFolder_IsItsDriveRoot()
    {
        using var t = new TempDir();
        Assert.Equal(Path.GetPathRoot(t.Path)!.ToUpperInvariant(), VolumeQuery.VolumePathName(t.Path)!.ToUpperInvariant());
        Assert.NotEqual(t.Path, VolumeQuery.VolumePathName(t.Path));
    }

    [Fact]
    public void Space_And_ClusterSize_OfTheSystemVolume()
    {
        var root = KnownFolders.SystemVolumeRoot();
        var (free, total) = VolumeQuery.Space(root)!.Value;
        Assert.InRange(free, 1, total);
        var cluster = VolumeQuery.ClusterBytes(root);
        Assert.True(cluster >= 512 && (cluster & (cluster - 1)) == 0);
    }

    [Theory]
    [InlineData(7u, "Usb")]
    [InlineData(12u, "Sd")]
    [InlineData(13u, "Mmc")]
    [InlineData(17u, "Nvme")]
    [InlineData(99u, "Unknown")]
    public void BusName_MapsStorageBusType(uint value, string expected) => Assert.Equal(expected, VolumeQuery.BusName(value));

    [Theory]
    [InlineData(false, "Removable", false)]
    [InlineData(false, "Fixed", false)]
    [InlineData(true, "Network", false)]
    [InlineData(true, "CDRom", false)]
    [InlineData(true, "NoRootDirectory", false)]
    [InlineData(true, "Removable", true)]
    [InlineData(true, "Fixed", true)]
    public void ProbesVolume_SkipsUnreadyAndNonLocalDrives(bool ready, string driveType, bool expected)
        => Assert.Equal(expected, WindowsVolumeProvider.ProbesVolume(ready, driveType));

    [Fact]
    public void MissingDrive_HasNoFacts()
    {
        var letter = Cmd.FreeDriveLetter();
        Assert.Null(VolumeQuery.Information($@"{letter}:\"));
        Assert.Null(VolumeQuery.Bus($@"{letter}:\"));
        Assert.False(VolumeQuery.IsSystemBootOrPaging($@"{letter}:\"));
    }
}
