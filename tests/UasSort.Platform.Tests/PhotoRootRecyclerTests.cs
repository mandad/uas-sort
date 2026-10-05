using System.Buffers.Binary;
using System.Text;
using Xunit.Sdk;
using static UasSort.Testing.PhotoCleanupFixtures;

namespace UasSort.Platform.Tests;

public sealed class PhotoRootRecyclerTests
{
    private static readonly DateOnly D = new(2025, 6, 1);

    private static ConfirmedPhotoCleanupPlan PlanFor(string photoRoot, params PhotoRow[] rows)
        => Confirmed(TimeProvider.System, PhotoCleanupMode.BeforeDate, rows, photoRoot);

    /// <summary>RecycleResult is a union: match, don't IsType (a boxed union is a RecycleResult).</summary>
    private static RecycleError Error(RecycleResult r) => r is RecycleError e ? e : throw new XunitException("Expected a RecycleError.");

    private static IPhotoRootRecycler Open(TestEnv env, ConfirmedPhotoCleanupPlan plan)
        => new WindowsPhotoRootRecyclerFactory(() => env.Settings, env.AppData, TestEnv.Machine, env.Facts).Open(plan);

    [Fact]
    public void Recycle_AConfirmedFileAndSetFolder_GoToTheRecycleBin_AndTheTestPurgesOnlyItsOwnItems()
    {
        using var env = new TestEnv();
        try
        {
            var root = env.C(env.PhotoRoot);
            env.Temp.File(@"photo\DJI_20250601121000_0002_D.DNG", 1_000);
            env.Temp.File(@"photo\001_0042\PANO_0001.DNG", 500);
            env.Temp.File(@"photo\001_0042\PANO_0002.DNG", 500);
            var plan = PlanFor(root, Row(Photo("DJI_20250601121000_0002_D.DNG", D)),
                                     Row(Set("001_0042", D, PhotoSetKind.Panorama, "PANO_0001.DNG", "PANO_0002.DNG")));
            using (var recycler = Open(env, plan))
            {
                Assert.Equal(1_000, recycler.Stat(Path.Join(root, "DJI_20250601121000_0002_D.DNG"))!.Size);
                Assert.True(recycler.Stat(Path.Join(root, "001_0042"))!.IsDirectory);
                Assert.True(recycler.Recycle(Path.Join(root, "DJI_20250601121000_0002_D.DNG")) is RecycleOk);
                Assert.True(recycler.Recycle(Path.Join(root, "001_0042")) is RecycleOk);
                Assert.Null(recycler.Stat(Path.Join(root, "001_0042")));
            }
            Assert.False(File.Exists(Path.Join(root, "DJI_20250601121000_0002_D.DNG")));
            Assert.False(Directory.Exists(Path.Join(root, "001_0042")));
            Assert.Equal(2, RecycleBinPurge.PurgeOwn(env.Temp.Path));          // both went to the Recycle Bin; nothing of anyone else's is touched
        }
        finally
        {
            RecycleBinPurge.PurgeOwn(env.Temp.Path);
        }
    }

    [Fact]
    public void Recycle_AnythingTheConfirmedPlanDoesNotName_IsRefused_AndStays()
    {
        using var env = new TestEnv();
        var root = env.C(env.PhotoRoot);
        var named = env.Temp.File(@"photo\A.DNG", 10);
        var other = env.Temp.File(@"photo\B.DNG", 10);
        var nested = env.Temp.File(@"photo\001_0042\PANO_0001.DNG", 10);
        var video = env.Temp.File(@"video\2025\DJI_20250601120000_0001_D.MP4", 10);
        using var recycler = Open(env, PlanFor(root, Row(Photo("A.DNG", D)), Row(Set("001_0042", D, PhotoSetKind.Panorama, "PANO_0001.DNG"))));
        Assert.Throws<UnsafeIoException>(() => recycler.Recycle(other));
        Assert.Throws<UnsafeIoException>(() => recycler.Recycle(nested));
        Assert.Throws<UnsafeIoException>(() => recycler.Recycle(video));
        Assert.Throws<UnsafeIoException>(() => recycler.Recycle(root));
        Assert.True(File.Exists(named) && File.Exists(other) && File.Exists(nested) && File.Exists(video));
    }

    [Fact]
    public void Open_APlanForAnotherPhotoFolder_IsRefused()
    {
        using var env = new TestEnv();
        var plan = PlanFor(env.C(env.Temp.Sub("elsewhere")), Row(Photo("A.DNG", D)));
        Assert.Throws<UnsafeIoException>(() => Open(env, plan));
    }

    [Fact]
    public void RecycleBinPurge_ReadsBothRecordVersions_AndRefusesForeignRoots()
    {
        const string path = @"C:\Users\u\AppData\Local\Temp\uas-sort-test-1\photo\A.DNG";
        var v2 = new byte[28 + (path.Length + 1) * 2];
        BinaryPrimitives.WriteInt64LittleEndian(v2, 2);
        BinaryPrimitives.WriteInt32LittleEndian(v2.AsSpan(24), path.Length + 1);
        Encoding.Unicode.GetBytes(path).CopyTo(v2, 28);
        Assert.Equal(path, RecycleBinPurge.OriginalPath(v2));
        var v1 = new byte[24 + 520];
        BinaryPrimitives.WriteInt64LittleEndian(v1, 1);
        Encoding.Unicode.GetBytes(path).CopyTo(v1, 24);
        Assert.Equal(path, RecycleBinPurge.OriginalPath(v1));
        Assert.Throws<InvalidOperationException>(() => RecycleBinPurge.PurgeOwn(@"C:\Users"));
        Assert.Throws<InvalidOperationException>(() => RecycleBinPurge.PurgeOwn(Path.Join(Path.GetTempPath(), "someone-else")));
    }

    [Fact] // [Review Focus 5]
    public void RecycleSink_RefusesEveryDeleteWithoutTheRecycleFlag()
    {
        var permanent = new RecycleSink();
        Assert.Equal(FileOperationCom.HResultCancelled, permanent.PreDeleteItem(0, 0));
        Assert.True(permanent.RefusedPermanentDelete);
        var recycle = new RecycleSink();
        Assert.Equal(0, recycle.PreDeleteItem(FileOperationCom.TsfDeleteRecycleIfPossible, 0));
        Assert.False(recycle.RefusedPermanentDelete);
        Assert.Equal(0, recycle.PostDeleteItem(FileOperationCom.TsfDeleteRecycleIfPossible, 0, 0, 1));
        Assert.True(recycle.Deleted);
        Assert.False(recycle.RemovedWithoutBinItem);
    }

    [Fact] // deferred minor P.11: psiNewlyCreated NULL = fully deleted, nothing arrived in the Recycle Bin
    public void RecycleSink_ASuccessfulDeleteWithoutANewItem_IsRemovedWithoutTheRecycleBin()
    {
        var sink = new RecycleSink();
        Assert.Equal(0, sink.PreDeleteItem(FileOperationCom.TsfDeleteRecycleIfPossible, 0));
        Assert.Equal(0, sink.PostDeleteItem(FileOperationCom.TsfDeleteRecycleIfPossible, 0, 0, 0));
        Assert.True(sink.Deleted);
        Assert.True(sink.RemovedWithoutBinItem);
        var failed = new RecycleSink();
        _ = failed.PostDeleteItem(FileOperationCom.TsfDeleteRecycleIfPossible, 0, unchecked((int)0x80070005), 0);
        Assert.False(failed.RemovedWithoutBinItem);                     // a failed delete removed nothing
    }

    [Fact] // deferred minor P.11
    public void Outcome_ADeleteThatLeftNothingInTheRecycleBin_IsNotRecycleOk()
    {
        var r = FileOperationCom.Outcome(false, 0, 0, deleted: true, aborted: false, removedWithoutBinItem: true);
        Assert.True(r is RecycleNotInBin { Message: "Windows removed it without putting it in the Recycle Bin" });
        Assert.True(FileOperationCom.Outcome(true, 0, 0, deleted: false, aborted: true, removedWithoutBinItem: true) is RecycleError { NotRecyclable: true });
    }

    [Theory] // [Review Focus 5]
    [InlineData(0)]                                   // PerformOperations reported success
    [InlineData(unchecked((int)0x800704C7))]          // … or ERROR_CANCELLED
    [InlineData(unchecked((int)0x80070005))]          // … or another failure
    public void Outcome_ARefusal_IsNotRecyclable_WhateverTheShellReturned(int performResult)
    {
        var e = Error(FileOperationCom.Outcome(refusedPermanentDelete: true, performResult, 0, deleted: false, aborted: true, removedWithoutBinItem: false));
        Assert.True(e.NotRecyclable);
        Assert.Equal(FileOperationCom.HResultCancelled, e.Code);
    }

    [Fact]
    public void Outcome_WithoutARefusal_MapsTheShellResult()
    {
        Assert.True(FileOperationCom.Outcome(false, 0, 0, deleted: true, aborted: false, removedWithoutBinItem: false) is RecycleOk);
        var failed = Error(FileOperationCom.Outcome(false, unchecked((int)0x80070005), 0, false, false, false));
        Assert.Equal((unchecked((int)0x80070005), false), (failed.Code, failed.NotRecyclable));
        Assert.Equal(unchecked((int)0x80070020), Error(FileOperationCom.Outcome(false, 0, unchecked((int)0x80070020), false, false, false)).Code);
        var cancelled = Error(FileOperationCom.Outcome(false, 0, 0, deleted: false, aborted: true, removedWithoutBinItem: false));
        Assert.Equal((FileOperationCom.HResultCancelled, false), (cancelled.Code, cancelled.NotRecyclable));
    }
}
