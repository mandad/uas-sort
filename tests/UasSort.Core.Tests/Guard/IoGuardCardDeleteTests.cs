using UasSort.Core.Tests.Support;
using static UasSort.Core.Tests.Guard.IoGuardPolicyTests;

namespace UasSort.Core.Tests.Guard;

public class IoGuardCardDeleteTests
{
    private const string Mp4 = @"E:\DCIM\DJI_001\DJI_20260726035000_0001_D.MP4";
    private const string Lrf = @"E:\DCIM\DJI_001\DJI_20260726035000_0001_D.LRF";
    private const string Pano = @"E:\DCIM\PANORAMA\001_0087";

    private static ConfirmedCleanupPlan PlanFor(string cardRoot) => TestCleanupPlans.Confirmed(cardRoot,
        TestCleanupPlans.Candidate("DCIM/DJI_001/DJI_20260726035000_0001_D.MP4",
            ["DCIM/DJI_001/DJI_20260726035000_0001_D.LRF", "DCIM/DJI_001/DJI_20260726035000_0001_D.MP4"]),
        TestCleanupPlans.Candidate("DCIM/PANORAMA/001_0087", ["DCIM/PANORAMA/001_0087/PANO_0001.DNG"], setFolder: "DCIM/PANORAMA/001_0087"));

    private static string Delete(string path, uint? attributes, GuardContext ctx)
        => Kind(IoGuardPolicy.Check(IoOp.CardDelete, path, attributes, ctx));

    private static readonly GuardContext Verified = Context(PlanFor(@"E:\"), @"E:\", verified: true);

    [Fact]
    public void NamedFilesAndSetFolders_AreAllowedOnAVerifiedVolume()
    {
        Assert.Equal("Allow", Delete(Mp4, 0x20, Verified));
        Assert.Equal("Allow", Delete(Lrf, 0x22, Verified));
        Assert.Equal("Allow", Delete(@"e:\dcim\dji_001\dji_20260726035000_0001_d.mp4", 0x20, Verified));
        Assert.Equal("Allow", Delete(@"E:\DCIM\PANORAMA\001_0087\PANO_0001.DNG", 0x20, Verified));
        Assert.Equal("Allow", Delete(Pano, 0x10, Verified));
    }

    [Fact]
    public void NoPlan_UnnamedPath_OrUnverifiedVolume_AreUnsafe()
    {
        Assert.Equal("Unsafe", Delete(Mp4, 0x20, Context(null, @"E:\", verified: true)));
        Assert.Equal("Unsafe", Delete(@"E:\DCIM\DJI_001\DJI_20260726001000_0002_D.MP4", 0x20, Verified));
        Assert.Equal("Unsafe", Delete(Mp4, 0x20, Context(PlanFor(@"E:\"), @"E:\", verified: false)));
        Assert.Equal("Unsafe", Delete(Mp4, null, Verified));
    }

    [Theory]
    [InlineData(@"E:\DCIM")]
    [InlineData(@"E:\DCIM\DJI_001")]
    [InlineData(@"E:\MISC")]
    [InlineData(@"E:\")]
    public void DirectoriesOtherThanNamedSetFolders_AreUnsafe(string dir)
        => Assert.Equal("Unsafe", Delete(dir, 0x10, Verified));

    [Fact]
    public void ANamedFilePath_ThatIsADirectory_IsUnsafe()
        => Assert.Equal("Unsafe", Delete(Mp4, 0x10, Verified));

    [Fact]
    public void APlanForAnotherCardRoot_IsUnsafe()
        => Assert.Equal("Unsafe", Delete(@"F:\DCIM\DJI_001\DJI_20260726035000_0001_D.MP4", 0x20,
            Context(PlanFor(@"E:\"), @"F:\", verified: true)));

    [Theory]
    [InlineData(Council, "DJI_20260725232655_0117_D.MP4")]
    [InlineData(P, "DJI_20260725233000_0116_D.DNG")]
    [InlineData(Prev, "DJI_20260725233000_0116_D.DNG")]
    [InlineData(L, "ledger-DESKTOP-A.jsonl")]
    [InlineData(A, "settings.json")]
    [InlineData(@"C:\Temp\card\DCIM\DJI_001", "DJI_20260726035000_0001_D.MP4")]
    public void Rule1b_ProtectedRootsAreUnsafeEvenWhenTheCardRootAndPlanPointThere(string folder, string name)
    {
        var plan = TestCleanupPlans.Confirmed(folder, TestCleanupPlans.Candidate(name, [name]));
        var ctx = Context(plan, folder, verified: true);
        Assert.Equal("Unsafe", Delete(PathRules.Join(folder, name), 0x20, ctx));
    }

    [Theory]
    [InlineData(IoOp.Delete, Mp4, 0x20)]
    [InlineData(IoOp.OpenForFlush, Mp4, 0x20)]
    [InlineData(IoOp.OpenForFlush, @"E:\", 0x10)]
    [InlineData(IoOp.SetAttributesOrTimes, Mp4, 0x20)]
    [InlineData(IoOp.SetAttributesOrTimes, @"E:\", 0x10)]
    [InlineData(IoOp.CreateNew, @"E:\DCIM\DJI_001\new.MP4", -1)]
    [InlineData(IoOp.CreateDir, @"E:\DCIM\DJI_009", -1)]
    public void OtherWritesUnderTheCard_AreUnsafeWithOrWithoutAPlan(IoOp op, string path, long attributes)
    {
        uint? a = attributes < 0 ? null : (uint)attributes;
        Assert.Equal("Unsafe", Kind(IoGuardPolicy.Check(op, path, a, Verified)));
        Assert.Equal("Unsafe", Kind(IoGuardPolicy.Check(op, path, a, Context())));
    }
}
