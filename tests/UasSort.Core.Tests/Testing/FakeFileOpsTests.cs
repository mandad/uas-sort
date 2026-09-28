using System.IO.Hashing;

namespace UasSort.Core.Tests.Testing;

public class FakeFileOpsTests
{
    private const string V = FakeLayout.VideoRoot;
    private const string Z = V + @"\2026\2026-09\2026-09-27 Zachar Bay";
    private const string Council = V + @"\2026\2026-07\2026-07-25 Council Road";
    private const string Final = Z + @"\DJI_20260927140627_0128_D.MP4";
    private static readonly byte[] Data = [1, 2, 3, 4, 5];
    private static readonly DateTime T = new(2026, 9, 27, 18, 8, 1, DateTimeKind.Utc);

    private static (FakeFileSystem Fs, FakeFileOps Ops) Setup()
    {
        var fs = FakeLayout.NewFileSystem();
        fs.Context = FakeLayout.Context(newFolderDirs: [V + @"\2026", V + @"\2026\2026-09", Z]);
        fs.AddFile(Council + @"\DJI_20260725232655_0117_D.MP4", 1_000, T, FakeFileSystem.CloudOnlyPlaceholder);
        return (fs, new FakeFileOps(fs));
    }

    private static string WriteTemp(FakeFileOps ops, string final)
    {
        using var s = ops.CreateTemp(final, Data.Length, out var temp);
        s.Write(Data);
        ops.FlushToDisk(s);
        return temp;
    }

    [Fact]
    public void HappyPath_CreatesVerifiesFinalisesAndRenames()
    {
        var (fs, ops) = Setup();
        ops.EnsureDirectory(Z, allowCreate: true);
        string[] created = [V + @"\2026\2026-09", Z];
        Assert.Equal(created, ops.CreatedDirectories);
        var temp = WriteTemp(ops, Final);
        Assert.Equal(Final + ".uas-sort.tmp", temp);
        Assert.Equal(0x2022u, fs.GetAttributes(temp));
        Assert.IsType<HashMatch>(Unwrap(ops.VerifyHash(temp, Data.Length, XxHash128.HashToUInt128(Data), CancellationToken.None)));
        ops.FinalizeAttributes(temp, T, T);
        Assert.Equal(0u, fs.GetAttributes(temp)!.Value & 0x2u);
        Assert.IsType<Renamed>(Unwrap(ops.RenameNoReplace(temp, Final)));
        Assert.True(ops.ConfirmFinal(Final, Data.Length));
        Assert.Equal(Data, fs.PeekContent(Final));
        Assert.Equal(T, fs.Metadata(Final)!.MtimeUtc);
        ops.FlushDestination(Z, [Final]);
        string[] flushed = [Final, Z];
        Assert.Equal(flushed, ops.Flushed);
        Assert.Equal(1, ops.FlushToDiskCount);
        fs.AssertNoViolations();
    }

    [Fact]
    public void EnsureDirectory_NeverCreatesAnAppendTargetOrANonNewFolderPath()
    {
        var (fs, ops) = Setup();
        Assert.Throws<DirectoryNotFoundException>(() => ops.EnsureDirectory(V + @"\2026\2026-08\2026-08-01 Gone", allowCreate: false));
        Assert.Throws<UnsafeIoException>(() => ops.EnsureDirectory(V + @"\2026\2026-10\2026-10-04 Nome Roads", allowCreate: true));
        Assert.False(fs.Exists(V + @"\2026\2026-10"));
        ops.EnsureDirectory(Council, allowCreate: false);   // exists: no-op
    }

    [Fact]
    public void CorruptVerify_MismatchesTheGivenNumberOfTimes()
    {
        var (fs, ops) = Setup();
        ops.EnsureDirectory(Z, true);
        fs.Faults.CorruptVerify[Final] = 1;
        var temp = WriteTemp(ops, Final);
        var expected = XxHash128.HashToUInt128(Data);
        Assert.IsType<HashMismatch>(Unwrap(ops.VerifyHash(temp, Data.Length, expected, CancellationToken.None)));
        Assert.IsType<HashMatch>(Unwrap(ops.VerifyHash(temp, Data.Length, expected, CancellationToken.None)));
    }

    [Fact]
    public void UnbufferedUnsupported_VerifiesInCachedMode()
    {
        var (fs, ops) = Setup();
        ops.EnsureDirectory(Z, true);
        fs.Faults.UnbufferedUnsupported.Add(Final);
        var temp = WriteTemp(ops, Final);
        var r = ops.VerifyHash(temp, Data.Length, XxHash128.HashToUInt128(Data), CancellationToken.None);
        Assert.Equal(VerifyMode.Cached, Assert.IsType<HashMatch>(Unwrap(r)).Mode);
    }

    [Fact]
    public void TargetAppearingBeforeRename_IsNeverOverwritten()
    {
        var (fs, ops) = Setup();
        ops.EnsureDirectory(Z, true);
        fs.Faults.TargetAppearsBeforeRename.Add(Final);
        var temp = WriteTemp(ops, Final);
        Assert.IsType<TargetExists>(Unwrap(ops.RenameNoReplace(temp, Final)));
        byte[] marker = [0x42];
        Assert.Equal(marker, fs.PeekContent(Final));
        ops.DeleteOwnTemp(temp);
        Assert.False(fs.Exists(temp));
    }

    [Fact]
    public void SizeAfterRename_FailsConfirm()
    {
        var (fs, ops) = Setup();
        ops.EnsureDirectory(Z, true);
        fs.Faults.SizeAfterRename[Final] = 4;
        var temp = WriteTemp(ops, Final);
        ops.RenameNoReplace(temp, Final);
        Assert.False(ops.ConfirmFinal(Final, Data.Length));
    }

    [Fact]
    public void DiskFull_ThrowsWhileWriting()
    {
        var (fs, ops) = Setup();
        ops.EnsureDirectory(Z, true);
        fs.Faults.DiskFullAfterBytes = 3;
        using var s = ops.CreateTemp(Final, Data.Length, out _);
        var e = Assert.Throws<IOException>(() => s.Write(Data));
        Assert.Equal(unchecked((int)0x80070070), e.HResult);
    }

    [Fact]
    public void DeleteOwnTemp_AcceptsOnlyTempNames_AndDeletesStaleTemps()
    {
        var (fs, ops) = Setup();
        Assert.Throws<UnsafeIoException>(() => ops.DeleteOwnTemp(Council + @"\DJI_20260725232655_0117_D.MP4"));
        fs.AddFile(Council + @"\DJI_20260725232655_0117_D.MP4.uas-sort.tmp", 10, T, 0x2022);
        ops.DeleteOwnTemp(Council + @"\DJI_20260725232655_0117_D.MP4.uas-sort.tmp");
        Assert.False(fs.Exists(Council + @"\DJI_20260725232655_0117_D.MP4.uas-sort.tmp"));
        Assert.True(fs.Exists(Council + @"\DJI_20260725232655_0117_D.MP4"));
        fs.AssertNoViolations();
    }

    [Fact]
    public void CreateTemp_RefusesAnExistingTemp_AndALostRoot()
    {
        var (fs, ops) = Setup();
        ops.EnsureDirectory(Z, true);
        WriteTemp(ops, Final);
        Assert.Throws<IOException>(() => ops.CreateTemp(Final, 5, out _));
        fs.Faults.LostRoots.Add(V);
        Assert.Throws<DirectoryNotFoundException>(() => ops.CreateTemp(Z + @"\other.MP4", 5, out _));
    }

    [Fact]
    public void TryGetSize_And_FreeBytes_AreMetadataOnly()
    {
        var (fs, ops) = Setup();
        Assert.True(ops.TryGetSize(Council + @"\DJI_20260725232655_0117_D.MP4", out var size));
        Assert.Equal(1_000, size);
        Assert.False(ops.TryGetSize(Z + @"\missing.MP4", out _));
        Assert.Equal(fs.DestinationFreeBytes, ops.FreeBytes(Z));
        fs.AssertNoViolations();
    }

    private static object Unwrap(VerifyResult r) => r switch { HashMatch m => m, HashMismatch x => x };
    private static object Unwrap(RenameResult r) => r switch { Renamed x => x, TargetExists t => t };
}
