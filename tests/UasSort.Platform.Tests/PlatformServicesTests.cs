namespace UasSort.Platform.Tests;

public sealed class PlatformServicesTests
{
    [Fact]
    public void Create_ComposesThePlatformClasses_AndWritesNothing()
    {
        using var temp = new TestTempDir();
        var appData = temp.Combine("appdata");                  // does not exist yet
        // Hard rule: the real Pictures folder follows OneDrive, and LedgerFor canonicalizes the derived photo root, so the
        // test passes a synthetic Pictures folder (the public Create(appDataDir, clock) uses KnownFolders.Pictures()).
        var p = PlatformServices.Create(appData, TimeProvider.System, picturesFolder: temp.Combine("Pictures"));

        Assert.Equal(Environment.MachineName, p.Machine);
        Assert.Equal(appData, p.AppDataDir);
        Assert.IsType<WindowsVolumeProvider>(p.Volumes);
        Assert.IsType<WindowsDirectoryLister>(p.Lister);
        Assert.IsType<SettingsStore>(p.Settings);
        var ledger = Assert.IsType<LedgerStore>(p.LedgerFor(temp.Combine("video")));
        Assert.EndsWith(@"\.uas-sort", ledger.Folder, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<GuardedFileOps>(p.FileOpsFor(SettingsDefaults.Derive(temp.FullPath), new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
        Assert.IsType<WindowsPhotoRootRecyclerFactory>(p.PhotoRecyclers);
        Assert.IsType<GuardedPhotoFileReader>(p.PhotoReaderFor(SettingsDefaults.Derive(temp.FullPath)));
        Assert.False(Directory.Exists(appData));                // no settings, drafts, reports or logs folder was created
    }
}
