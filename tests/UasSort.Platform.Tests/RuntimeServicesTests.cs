namespace UasSort.Platform.Tests;

public sealed class RuntimeServicesTests
{
    private static string UniqueName() => @"Local\uas-sort-test-" + Guid.NewGuid().ToString("N");

    [Fact]
    public void PowerRequest_IsCreatedAndCleared()
    {
        var awake = new PowerRequest().KeepSystemAwake("Offloading drone media");
        awake.Dispose();
        awake.Dispose();
    }

    [Fact]
    public async Task OffloadLock_IsExclusive_AndReleasableFromAnotherThread()
    {
        var name = UniqueName();
        var first = new OffloadLock(name).TryAcquire();
        Assert.NotNull(first);
        Assert.Null(new OffloadLock(name).TryAcquire());
        await Task.Run(() => first!.Dispose(), TestContext.Current.CancellationToken);
        using var again = new OffloadLock(name).TryAcquire();
        Assert.NotNull(again);
    }

    [Fact]
    public void Eject_RefusesTheSystemVolume_AndAMissingDrive_WithoutPnPCalls()
    {
        var eject = new DeviceEject();
        Assert.True(eject.Eject(KnownFolders.SystemVolumeRoot()) is EjectRefused r1 && r1.Reason.Contains("system", StringComparison.OrdinalIgnoreCase));
        Assert.True(eject.Eject($@"{Cmd.FreeDriveLetter()}:\") is EjectRefused);
        Assert.True(eject.Eject(Path.GetTempPath()) is EjectRefused);
    }

    [Fact]
    public void ShellLauncher_ValidatesBeforeLaunching()
    {
        using var t = new TempDir();
        var shell = new ShellLauncher();
        Assert.Throws<ArgumentException>(() => shell.OpenHttps(new Uri("http://example.com/")));
        Assert.Throws<ArgumentException>(() => shell.OpenHttps(new Uri("file:///C:/Windows/notepad.exe")));
        Assert.Throws<DirectoryNotFoundException>(() => shell.OpenFolder(Path.Join(t.Path, "missing")));
        Assert.Throws<FileNotFoundException>(() => shell.OpenFile(Path.Join(t.Path, "missing.json")));
    }

    [Fact]
    public void FileLog_AppendsDailyFiles_AndPrunesAfter14Days()
    {
        using var env = new TestEnv();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 27, 21, 7, 2, TimeSpan.Zero));
        var log = new FileLog(env.AppData, TestEnv.Machine, env.Facts, clock);
        log.Info("scan started");
        log.Error("copy failed", new IOException("disk full"));
        var today = Path.Join(env.AppData, "logs", "uas-sort-20260927.log");
        var lines = File.ReadAllLines(today);
        Assert.StartsWith("2026-09-27T21:07:02.000Z INFO scan started", lines[0], StringComparison.Ordinal);
        Assert.Contains("ERROR copy failed", lines[1], StringComparison.Ordinal);
        Assert.Contains("disk full", string.Join('\n', lines), StringComparison.Ordinal);
        env.Temp.File(@"appdata\logs\uas-sort-20260913.log");
        env.Temp.File(@"appdata\logs\uas-sort-20260914.log");
        env.Temp.File(@"appdata\logs\other.txt");
        log.Prune();
        Assert.False(File.Exists(Path.Join(env.AppData, "logs", "uas-sort-20260913.log")));
        Assert.True(File.Exists(Path.Join(env.AppData, "logs", "uas-sort-20260914.log")));
        Assert.True(File.Exists(Path.Join(env.AppData, "logs", "other.txt")));
        Assert.True(File.Exists(today));
    }

    [Fact]
    public void AppAssets_OpenPlacesFromTheAppFolder_AndMissingSelfTestThrows()
    {
        using var t = new TempDir();
        t.File("places.bin.gz", 32);
        var assets = new AppAssets(t.Path, typeof(RuntimeServicesTests).Assembly);
        using (var s = assets.OpenPlaces()) Assert.Equal(32, s.Length);
        Assert.Throws<FileNotFoundException>(() => assets.OpenSelfTest("selftest.dng"));
    }
}
