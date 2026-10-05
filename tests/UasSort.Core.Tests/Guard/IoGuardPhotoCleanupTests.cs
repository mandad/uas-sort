// tests/UasSort.Core.Tests/Guard/IoGuardPhotoCleanupTests.cs
using static UasSort.Testing.PhotoCleanupFixtures;

namespace UasSort.Core.Tests.Guard;

public sealed class IoGuardPhotoCleanupTests
{
    private const string Root = FakeLayout.PhotoRoot;
    private const string Lr = @"X:\Lightroom";
    private const uint File = 0x20, Dir = 0x10, CloudOnly = FakeFileSystem.CloudOnlyPlaceholder;
    private const string Dng = Root + @"\DJI_20260601121000_0002_D.DNG";
    private const string Jpg = Root + @"\DJI_20260601121000_0002_D.JPG";
    private const string Pano = Root + @"\001_0087";
    private static readonly DateOnly D = new(2026, 6, 1);

    private static ConfirmedPhotoCleanupPlan Plan(string photoRoot = Root, string setFolder = "001_0087") => Confirmed(new FakeTimeProvider(), PhotoCleanupMode.Verify,
        [Row(Photo("DJI_20260601121000_0002_D.DNG", D, twin: "DJI_20260601121000_0002_D.JPG")),
         Row(Set(setFolder, D, PhotoSetKind.Panorama, "PANO_0001.DNG", "PANO_0002.DNG"))], photoRoot);

    private static GuardContext Ctx(ConfirmedPhotoCleanupPlan? plan = null, string? lightroom = Lr, IEnumerable<string>? previous = null)
        => FakeLayout.Context(cardRoot: null, previousPhotoRoots: previous) with { LightroomFolder = lightroom, PhotoCleanup = plan };

    private static string Kind(IoOp op, string path, uint? attributes, GuardContext ctx) => IoGuardPolicy.Check(op, path, attributes, ctx) switch
    {
        GuardAllow => "Allow",
        GuardUnsafe => "Unsafe",
        GuardCloudOnly => "CloudOnly",
        GuardHydration => "Hydration",
    };

    [Fact]
    public void Recycle_TheConfirmedFilesAndSetFolder_AreAllowed_EvenWhenCloudOnly()
    {
        var ctx = Ctx(Plan());
        Assert.Equal("Allow", Kind(IoOp.PhotoRootRecycle, Dng, File, ctx));
        Assert.Equal("Allow", Kind(IoOp.PhotoRootRecycle, Jpg.ToUpperInvariant(), File, ctx));
        Assert.Equal("Allow", Kind(IoOp.PhotoRootRecycle, Pano, Dir, ctx));
        Assert.Equal("Allow", Kind(IoOp.PhotoRootRecycle, Dng, CloudOnly, ctx));      // recycling never opens the data
    }

    [Theory]
    [InlineData(Root + @"\DJI_20260601999999_0009_D.DNG")]                 // not in the plan
    [InlineData(Root + @"\001_0087\PANO_0001.DNG")]                          // inside a set folder: only the folder moves
    [InlineData(Root)]                                                        // the root itself
    [InlineData(FakeLayout.VideoRoot + @"\2026\2026-06\2026-06-01 Juneau\DJI_20260601120000_0001_D.MP4")]
    [InlineData(FakeLayout.VideoRoot + @"\.uas-sort\ledger-DESKTOP-A.jsonl")]
    [InlineData(Lr + @"\2026\Damian_20260601_001.dng")]
    [InlineData(@"D:\LR_Catalog\Lightroom Catalog.lrcat")]
    [InlineData(FakeLayout.AppDataDir + @"\settings.json")]
    [InlineData(@"E:\DCIM\DJI_001\DJI_20260601121000_0002_D.DNG")]
    public void Recycle_AnythingElse_IsUnsafe(string path)
        => Assert.Equal("Unsafe", Kind(IoOp.PhotoRootRecycle, path, File, Ctx(Plan())));

    [Fact]
    public void Recycle_WithoutAPlan_OrAPlanForAnotherRoot_OrInAPreviousRoot_IsUnsafe()
    {
        Assert.Equal("Unsafe", Kind(IoOp.PhotoRootRecycle, Dng, File, Ctx()));
        Assert.Equal("Unsafe", Kind(IoOp.PhotoRootRecycle, @"X:\Old\DJI_20260601121000_0002_D.DNG", File, Ctx(Plan(@"X:\Old"))));
        Assert.Equal("Unsafe", Kind(IoOp.PhotoRootRecycle, Root + @"\001_0099", Dir,
                                    Ctx(Plan(setFolder: "001_0099"), previous: [Root + @"\001_0099"])));
        Assert.Equal("Unsafe", Kind(IoOp.PhotoRootRecycle, Dng, null, Ctx(Plan())));    // gone
    }

    [Fact]
    public void Read_PhotoRootFiles_SetMembers_AndLightroomFiles_AreAllowed_ButNeverHydrated()
    {
        var ctx = Ctx();
        Assert.Equal("Allow", Kind(IoOp.PhotoCleanupRead, Dng, File, ctx));
        Assert.Equal("Allow", Kind(IoOp.PhotoCleanupRead, Pano + @"\PANO_0001.DNG", File, ctx));
        Assert.Equal("Allow", Kind(IoOp.PhotoCleanupRead, Lr + @"\2026\2026-06-01\Damian_20260601_001.dng", File, ctx));
        Assert.Equal("Hydration", Kind(IoOp.PhotoCleanupRead, Dng, CloudOnly, ctx));
    }

    [Theory]
    [InlineData(FakeLayout.VideoRoot + @"\2026\x.MP4")]
    [InlineData(FakeLayout.VideoRoot + @"\.uas-sort\ledger-DESKTOP-A.jsonl")]
    [InlineData(Lr + @"\LR_Catalog\Lightroom Catalog.lrcat")]
    [InlineData(Lr + @"\Lightroom Catalog.lrcat")]
    [InlineData(Lr + @"\Lightroom Catalog Smart Previews.lrdata\a\b.dng")]
    [InlineData(Root + @"\001_0087\deeper\PANO_0001.DNG")]
    [InlineData(FakeLayout.AppDataDir + @"\settings.json")]
    [InlineData(@"Y:\Elsewhere\x.dng")]
    public void Read_AnythingElse_IsUnsafe(string path)
        => Assert.Equal("Unsafe", Kind(IoOp.PhotoCleanupRead, path, File, Ctx()));

    [Fact]
    public void Read_AFolder_OrALightroomFileWithoutTheSetting_IsUnsafe()
    {
        Assert.Equal("Unsafe", Kind(IoOp.PhotoCleanupRead, Pano, Dir, Ctx()));
        Assert.Equal("Unsafe", Kind(IoOp.PhotoCleanupRead, Lr + @"\2026\x.dng", File, Ctx(lightroom: null)));
    }

    [Theory]
    [InlineData(IoOp.ReadData)]
    [InlineData(IoOp.CreateNew)]
    [InlineData(IoOp.Delete)]
    [InlineData(IoOp.Rename)]
    [InlineData(IoOp.SetAttributesOrTimes)]
    public void EveryOtherOp_InTheLightroomFolderOrTheCatalog_IsUnsafe(IoOp op)
    {
        Assert.Equal("Unsafe", Kind(op, Lr + @"\2026\x.dng", File, Ctx()));
        Assert.Equal("Unsafe", Kind(op, @"D:\LR_Catalog\Lightroom Catalog.lrcat", File, Ctx()));
    }
}
