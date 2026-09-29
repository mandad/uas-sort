namespace UasSort.Platform.Tests;

public sealed class CardEraserTests
{
    private static readonly CardIdentity Card = new(0x1A2B3C4D, null, "exFAT", 256_060_514_304);
    private const string Mp4 = @"DCIM\DJI_001\DJI_20260725232655_0117_D.MP4";
    private const string Lrf = @"DCIM\DJI_001\DJI_20260725232655_0117_D.LRF";
    private const string Trinf = @"DCIM\DJI_001\.DJI_20260725232655_0117_D.MP4.trinf";
    private const string Sibling = @"DCIM\DJI_001\DJI_20260726022937_0118_D.MP4";
    private const string ReadOnlyDng = @"DCIM\DJI_001\DJI_20260725233000_0116_D.DNG";
    private const string Set = @"DCIM\PANORAMA\001_0087";

    public enum Fault { None, NotVolumeRoot, NoFacts, Ntfs, OtherIdentity, ReadOnlyVolume, FixedUsb, Nvme, SystemVolume, RootOnVolume, NoMisc }

    private sealed class FakeVolumeFacts(string cardRoot, Fault fault, string videoRoot) : IVolumeFacts
    {
        private static string Sep(string p) => p.EndsWith('\\') ? p : p + '\\';

        public string? VolumePathName(string path)
        {
            if (fault == Fault.RootOnVolume && path.StartsWith(videoRoot, StringComparison.OrdinalIgnoreCase)) return Sep(cardRoot);
            if (path.StartsWith(cardRoot, StringComparison.OrdinalIgnoreCase))
                return fault == Fault.NotVolumeRoot ? Path.GetPathRoot(cardRoot) : Sep(cardRoot);
            return VolumeQuery.VolumePathName(path);
        }

        public VolumeFacts? Describe(string volumeRoot) => fault switch
        {
            Fault.NoFacts => null,
            Fault.Ntfs => new(Card with { FileSystem = "NTFS" }, false, "Sd", true, false),
            Fault.OtherIdentity => new(Card with { VolumeSerial = 0xDEADBEEF }, false, "Sd", true, false),
            Fault.ReadOnlyVolume => new(Card, true, "Sd", true, false),
            Fault.FixedUsb => new(Card, false, "Usb", false, false),
            Fault.Nvme => new(Card, false, "Nvme", false, false),
            Fault.SystemVolume => new(Card, false, "Sd", true, true),
            _ => new(Card, false, "Sd", true, false),
        };
    }

    private static void MakeCard(TestEnv env)
    {
        foreach (var f in new[] { Mp4, Lrf, Sibling, @"DCIM\PANORAMA\001_0087\PANO_0001.DNG", @"DCIM\PANORAMA\001_0087\PANO_0002.DNG",
                                  @"DCIM\PANORAMA\001_0088\PANO_0001.DNG", @"MISC\FC9113.db" })
            env.Temp.File(@"card\" + f);
        env.Temp.File(@"card\" + Trinf, attributes: FileAttributes.Hidden);
        env.Temp.File(@"card\" + ReadOnlyDng, attributes: FileAttributes.ReadOnly);
    }

    private static ConfirmedCleanupPlan Plan(TestEnv env, string[] files, string[] setFolders, string? cardRoot = null)
        => CleanupPlanFixtures.Confirmed(cardRoot ?? env.C(env.CardRoot), Card, files, setFolders,
                                         new FakeTimeProvider(new DateTimeOffset(2026, 10, 12, 19, 30, 0, TimeSpan.Zero)));

    private static WindowsCardEraserFactory Factory(TestEnv env, Fault fault = Fault.None)
        => new(env.Settings, env.AppData, TestEnv.Machine, env.Facts, new WindowsDirectoryLister(),
               new FakeVolumeFacts(env.C(env.CardRoot), fault, env.C(env.VideoRoot)), $@"{Cmd.FreeDriveLetter()}:\");

    private static CardSource Source(TestEnv env, bool browsed = false, bool writeProtected = false)
        => new(env.C(env.CardRoot), Card, browsed, writeProtected);

    private static string[] Listing(TestEnv env)
        => new WindowsDirectoryLister().Enumerate(env.CardRoot, true, new HashSet<string>()).Entries
                                       .Select(e => e.RelPath).Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public void DeleteFile_RemovesExactlyTheNamedFiles_IncludingAHiddenTrinf()
    {
        using var env = new TestEnv();
        MakeCard(env);
        var before = Listing(env);
        using var eraser = Factory(env).Open(Source(env), Card, Plan(env, [Lrf, Trinf, Mp4], []));
        foreach (var f in new[] { Lrf, Trinf, Mp4 }) Assert.True(eraser.DeleteFile(f) is EraseOk, f);
        Assert.Equal(before.Except([Lrf, Trinf, Mp4]).ToArray(), Listing(env));
    }

    [Fact]
    public void RemoveEmptySetFolder_FailsWhileNotEmpty_ThenRemovesIt()
    {
        using var env = new TestEnv();
        MakeCard(env);
        string[] members = [$@"{Set}\PANO_0001.DNG", $@"{Set}\PANO_0002.DNG"];
        using var eraser = Factory(env).Open(Source(env), Card, Plan(env, members, [Set]));
        Assert.True(eraser.RemoveEmptySetFolder(Set) is EraseError { Win32Error: 145 });
        Assert.True(Directory.Exists(Path.Join(env.CardRoot, Set)));
        foreach (var m in members) Assert.True(eraser.DeleteFile(m) is EraseOk);
        Assert.True(eraser.RemoveEmptySetFolder(Set) is EraseOk);
        Assert.False(Directory.Exists(Path.Join(env.CardRoot, Set)));
        Assert.True(Directory.Exists(Path.Join(env.CardRoot, @"DCIM\PANORAMA\001_0088")));
    }

    [Fact]
    public void ReadOnlyFile_FailsWithAccessDenied_AndIsUnchanged()
    {
        using var env = new TestEnv();
        MakeCard(env);
        using var eraser = Factory(env).Open(Source(env), Card, Plan(env, [ReadOnlyDng], []));
        Assert.True(eraser.DeleteFile(ReadOnlyDng) is EraseError { Win32Error: 5 });
        Assert.True(File.GetAttributes(Path.Join(env.CardRoot, ReadOnlyDng)).HasFlag(FileAttributes.ReadOnly));
    }

    [Fact]
    public void PathsOutsideThePlan_ThrowBeforeAnyWin32Call()
    {
        using var env = new TestEnv();
        MakeCard(env);
        env.Temp.File(@"photo\DJI_0001.DNG");
        using var eraser = Factory(env).Open(Source(env), Card, Plan(env, [Mp4], [Set]));
        Assert.Throws<UnsafeIoException>(() => eraser.DeleteFile(Sibling));
        Assert.Throws<UnsafeIoException>(() => eraser.RemoveEmptySetFolder(@"DCIM\DJI_001"));
        Assert.Throws<UnsafeIoException>(() => eraser.RemoveEmptySetFolder("MISC"));
        Assert.Throws<UnsafeIoException>(() => eraser.DeleteFile(@"..\photo\DJI_0001.DNG"));
        Assert.True(File.Exists(Path.Join(env.CardRoot, Sibling)));
        Assert.True(File.Exists(Path.Join(env.PhotoRoot, "DJI_0001.DNG")));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Factory_RefusesBrowsedOrWriteProtectedSources(bool browsed, bool writeProtected)
    {
        using var env = new TestEnv();
        MakeCard(env);
        Assert.Throws<UnsafeIoException>(() => Factory(env).Open(Source(env, browsed, writeProtected), Card, Plan(env, [Mp4], [])));
    }

    [Theory]
    [InlineData(Fault.NotVolumeRoot)]
    [InlineData(Fault.NoFacts)]
    [InlineData(Fault.Ntfs)]
    [InlineData(Fault.OtherIdentity)]
    [InlineData(Fault.ReadOnlyVolume)]
    [InlineData(Fault.FixedUsb)]
    [InlineData(Fault.Nvme)]
    [InlineData(Fault.SystemVolume)]
    [InlineData(Fault.RootOnVolume)]
    [InlineData(Fault.NoMisc)]
    public void Factory_RefusesAVolumeThatFailsAnyCheck(Fault fault)
    {
        using var env = new TestEnv();
        MakeCard(env);
        if (fault == Fault.NoMisc) Directory.Delete(Path.Join(env.CardRoot, "MISC"), recursive: true);
        Assert.Throws<UnsafeIoException>(() => Factory(env, fault).Open(Source(env), Card, Plan(env, [Mp4], [])));
    }

    [Fact]
    public void Factory_RefusesAPlanForAnotherCardRoot()
    {
        using var env = new TestEnv();
        MakeCard(env);
        var other = env.Temp.Sub("other-card");
        Assert.Throws<UnsafeIoException>(() => Factory(env).Open(Source(env), Card, Plan(env, [Mp4], [], env.C(other))));
    }

    [Fact]
    public void ProductionFactory_RefusesATempFolder()
    {
        using var env = new TestEnv();
        MakeCard(env);
        var production = new WindowsCardEraserFactory(env.Settings, env.AppData, TestEnv.Machine, env.Facts, new WindowsDirectoryLister());
        var ex = Assert.Throws<UnsafeIoException>(() => production.Open(Source(env), Card, Plan(env, [Mp4], [])));
        Assert.Contains("volume root", ex.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Join(env.CardRoot, Mp4)));
    }
}
