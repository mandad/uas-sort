using UasSort.Core.Tests.Support;

namespace UasSort.Core.Tests.Testing;

public class FakeCardTests
{
    private static readonly DateTime T = new(2026, 7, 26, 7, 51, 30, DateTimeKind.Utc);
    private const string Clip = "DCIM/DJI_001/DJI_20260726035000_0001_D.MP4";
    private const string Proxy = "DCIM/DJI_001/DJI_20260726035000_0001_D.LRF";
    private const string Other = "DCIM/DJI_001/DJI_20260726001000_0002_D.MP4";
    private static readonly CardSource Source = new(@"E:\", FakeLayout.CardId, false, false);

    private static FakeFileSystem Card()
    {
        var fs = FakeLayout.NewFileSystem();
        fs.AddFile(@"E:\" + Clip, 3_000_000, T);
        fs.AddFile(@"E:\" + Proxy, 1_000, T);
        fs.AddFile(@"E:\" + Other, 1_000, T);
        fs.AddFile(@"E:\DCIM\PANORAMA\001_0087\PANO_0001.DNG", 13_751_808, T);
        fs.AddFile(@"E:\DCIM\PANORAMA\001_0087\PANO_0002.DNG", 12_882_432, T);
        fs.AddFile(@"E:\MISC\FC9113.db", 4_096, T);
        return fs;
    }

    private static ConfirmedCleanupPlan Plan() => TestCleanupPlans.Confirmed(@"E:\",
        TestCleanupPlans.Candidate(Clip, [Proxy, Clip]),
        TestCleanupPlans.Candidate("DCIM/PANORAMA/001_0087",
            ["DCIM/PANORAMA/001_0087/PANO_0001.DNG", "DCIM/PANORAMA/001_0087/PANO_0002.DNG"], setFolder: "DCIM/PANORAMA/001_0087"));

    [Fact]
    public void Reader_ReadsCardFilesThroughTheGuard()
    {
        var fs = Card();
        var reader = new FakeCardReaderFactory(fs).Open(Source, FakeLayout.CardId);
        using var s = reader.OpenSequential(Clip);
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        Assert.Equal(fs.PeekContent(@"E:\" + Clip), ms.ToArray());
        Assert.Equal(new FakeGuardCall(IoOp.ReadData, @"E:\" + Clip.Replace('/', '\\'), "Allow"), fs.GuardLog[^1]);
        Assert.Equal(3_000_000, reader.Stat(Clip).Size);
        Assert.Throws<FileNotFoundException>(() => reader.Stat("DCIM/DJI_001/missing.MP4"));
    }

    [Fact]
    public void Reader_TransientErrorFiresOnceThenTheReReadSucceeds()
    {
        var fs = Card();
        fs.Faults.TransientCardReadError.Add(Clip);
        using var s = new FakeCardReaderFactory(fs).Open(Source, FakeLayout.CardId).OpenSequential(Clip);
        var buffer = new byte[1 << 20];
        Assert.Throws<IOException>(() => s.Read(buffer, 0, buffer.Length));
        Assert.Equal(0, s.Position);
        Assert.Equal(buffer.Length, s.Read(buffer, 0, buffer.Length));
    }

    [Fact]
    public void Reader_CardVanishingMidFile_RemovesTheCard()
    {
        var fs = Card();
        fs.Faults.CardVanishesAfterBytes[Clip] = 1_000_000;
        var reader = new FakeCardReaderFactory(fs).Open(Source, FakeLayout.CardId);
        using var s = reader.OpenSequential(Clip);
        Assert.Throws<IOException>(() => s.CopyTo(Stream.Null));
        Assert.True(fs.Faults.CardRemoved);
        Assert.Throws<IOException>(reader.CurrentIdentity);
        Assert.Equal(21, Assert.Single(reader.Relist().Errors).Win32Error);
    }

    [Fact]
    public void Reader_IdentityOnCall_SwapsTheCardMidRun()
    {
        var fs = Card();
        var other = FakeLayout.CardId with { VolumeSerial = 0x0BADF00D };
        fs.Faults.IdentityOnCall = n => n >= 3 ? other : null;
        var reader = new FakeCardReaderFactory(fs).Open(Source, FakeLayout.CardId);
        Assert.Equal(FakeLayout.CardId, reader.CurrentIdentity());
        Assert.Equal(FakeLayout.CardId, reader.CurrentIdentity());
        Assert.Equal(other, reader.CurrentIdentity());
    }

    [Fact]
    public void Reader_Relist_ListsTheWholeCard()
    {
        var listing = new FakeCardReaderFactory(Card()).Open(Source, FakeLayout.CardId).Relist();
        Assert.Contains(listing.Entries, e => e.RelPath == @"MISC\FC9113.db");
        Assert.Equal(6, listing.Entries.Count(e => !e.IsDirectory));
    }

    [Fact]
    public void Eraser_DeletesExactlyTheNamedFiles_AndFreesClusterRoundedSpace()
    {
        var fs = Card();
        using var eraser = new FakeCardEraserFactory(fs).Open(Source, FakeLayout.CardId, Plan());
        Assert.IsType<EraseOk>(Unwrap(eraser.DeleteFile(Proxy)));
        Assert.IsType<EraseOk>(Unwrap(eraser.DeleteFile(Clip)));
        Assert.False(fs.Exists(@"E:\" + Clip));
        Assert.True(fs.Exists(@"E:\" + Other));
        // 1,000 B → 1 cluster; 3,000,000 B → 23 clusters of 131,072 B
        Assert.Equal(12_400_000_000 + 131_072 + 23 * 131_072, fs.CardSpaceOf(@"E:\").FreeBytes);
        fs.AssertNoViolations();
    }

    [Fact]
    public void Eraser_AnUnnamedFile_ThrowsAndRecordsACardDeleteViolation()
    {
        var fs = Card();
        using var eraser = new FakeCardEraserFactory(fs).Open(Source, FakeLayout.CardId, Plan());
        Assert.Throws<UnsafeIoException>(() => eraser.DeleteFile(Other));
        Assert.True(fs.Exists(@"E:\" + Other));
        Assert.Equal(IoOp.CardDelete, Assert.Single(fs.CardDeleteViolations).Op);
        Assert.Throws<InvalidOperationException>(fs.AssertNoViolations);
    }

    [Fact]
    public void Eraser_RemovesASetFolderOnlyOnceEmpty()
    {
        var fs = Card();
        using var eraser = new FakeCardEraserFactory(fs).Open(Source, FakeLayout.CardId, Plan());
        Assert.Equal(145, Assert.IsType<EraseError>(Unwrap(eraser.RemoveEmptySetFolder("DCIM/PANORAMA/001_0087"))).Win32Error);
        eraser.DeleteFile("DCIM/PANORAMA/001_0087/PANO_0001.DNG");
        eraser.DeleteFile("DCIM/PANORAMA/001_0087/PANO_0002.DNG");
        Assert.IsType<EraseOk>(Unwrap(eraser.RemoveEmptySetFolder("DCIM/PANORAMA/001_0087")));
        Assert.False(fs.Exists(@"E:\DCIM\PANORAMA\001_0087"));
        Assert.True(fs.Exists(@"E:\DCIM\PANORAMA"));
        Assert.Throws<UnsafeIoException>(() => eraser.RemoveEmptySetFolder("DCIM/PANORAMA"));
    }

    [Fact]
    public void Eraser_InjectedErrors_AndDeletePending()
    {
        var fs = Card();
        fs.Faults.DeleteErrors[Proxy] = 19;
        fs.Faults.DeletePending.Add(Clip);
        using var eraser = new FakeCardEraserFactory(fs).Open(Source, FakeLayout.CardId, Plan());
        Assert.Equal(19, Assert.IsType<EraseError>(Unwrap(eraser.DeleteFile(Proxy))).Win32Error);
        Assert.True(fs.Exists(@"E:\" + Proxy));
        Assert.IsType<EraseOk>(Unwrap(eraser.DeleteFile(Clip)));
        var listing = new FakeCardReaderFactory(fs).Open(Source, FakeLayout.CardId).Relist();
        Assert.Contains(listing.Entries, e => e.RelPath == Clip.Replace('/', '\\'));
    }

    [Fact]
    public void Eraser_ReadOnlyFile_FailsWithAccessDenied()
    {
        var fs = Card();
        fs.SetAttributes(@"E:\" + Proxy, 0x21);
        using var eraser = new FakeCardEraserFactory(fs).Open(Source, FakeLayout.CardId, Plan());
        Assert.Equal(5, Assert.IsType<EraseError>(Unwrap(eraser.DeleteFile(Proxy))).Win32Error);
        Assert.True(fs.Exists(@"E:\" + Proxy));
    }

    [Fact]
    public void Factory_RefusesBrowsedWriteProtectedUnverifiedAndMismatchedSources()
    {
        var fs = Card();
        var factory = new FakeCardEraserFactory(fs);
        Assert.Throws<UnsafeIoException>(() => factory.Open(Source with { IsBrowsedFolder = true }, FakeLayout.CardId, Plan()));
        Assert.Throws<UnsafeIoException>(() => factory.Open(Source with { IsWriteProtected = true }, FakeLayout.CardId, Plan()));
        Assert.Throws<UnsafeIoException>(() => factory.Open(Source, FakeLayout.CardId with { VolumeSerial = 1 }, Plan()));
        fs.SetCardIdentity(@"E:\", FakeLayout.CardId with { VolumeSerial = 2 });
        Assert.Throws<UnsafeIoException>(() => factory.Open(Source, FakeLayout.CardId, Plan()));
        fs.SetCardIdentity(@"E:\", FakeLayout.CardId);
        factory.VolumeVerified = false;
        Assert.Throws<UnsafeIoException>(() => factory.Open(Source, FakeLayout.CardId, Plan()));
        Assert.Equal(0, factory.OpenCount);
    }

    [Fact]
    public void Eraser_AfterDispose_Throws()
    {
        var eraser = new FakeCardEraserFactory(Card()).Open(Source, FakeLayout.CardId, Plan());
        eraser.Dispose();
        Assert.Throws<ObjectDisposedException>(() => eraser.DeleteFile(Clip));
    }

    private static object Unwrap(EraseResult r) => r switch { EraseOk ok => ok, EraseError e => e };
}
