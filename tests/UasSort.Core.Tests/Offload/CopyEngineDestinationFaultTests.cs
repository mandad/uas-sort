// tests/UasSort.Core.Tests/Offload/CopyEngineDestinationFaultTests.cs
using UasSort.Core;
using UasSort.Core.Offload;
using UasSort.Testing.Offload;
using static UasSort.Core.Tests.Offload.CopyEngineTests;
using static UasSort.Testing.Offload.OffloadPlanBuilder;

namespace UasSort.Core.Tests.Offload;

public class CopyEngineDestinationFaultTests
{
    [Fact]
    public async Task DiskFull_FailsTheInFlightFile_AndStops()
    {
        var rig = Videos(2 * CopyEngine.ChunkBytes, 1_000);
        rig.Fs.Faults.DiskFullAfterBytes = CopyEngine.ChunkBytes + 100;

        var r = await rig.RunEngineAsync();

        Assert.Equal(["Failed", "NotStarted"], r.Kinds());
        Assert.Equal(CopyPhase.Copy, ((Failed)r.Outcomes[0]).Phase);
        Assert.Equal(StopReason.DestinationFull, r.Stop);
        Assert.False(rig.Fs.Exists(OffloadPaths.TempOf(rig.Batch.Jobs[0].DestPath)));
    }

    [Fact]
    public async Task DestinationVolumeLost_FailsTheInFlightFile_AndStops()
    {
        var b = new OffloadPlanBuilder().WithPhotoRoot(@"D:\Photos");
        b.Photo("DJI_20260927140000_0001_D.DNG", 2 * CopyEngine.ChunkBytes, T0);
        b.Photo("DJI_20260927140100_0002_D.DNG", 1_000, T0.AddMinutes(1));
        var rig = new OffloadRig(b).Build();
        rig.Files.OnTempWrite = (_, _) => rig.Fs.Faults.LostRoots.Add(@"D:\");

        var r = await rig.RunEngineAsync();

        Assert.Equal(["Failed", "NotStarted"], r.Kinds());
        Assert.Equal(StopReason.DestinationLost, r.Stop);
        Assert.False(rig.Fs.Exists(rig.Batch.Jobs[0].DestPath));
    }

    [Fact]
    public async Task OneBitFlipOnReadBack_RetriesOnce_AndVerifies()
    {
        var rig = Videos(1_000);
        rig.Fs.Faults.CorruptVerify[rig.Batch.Jobs[0].DestPath] = 1;

        var r = await rig.RunEngineAsync();

        Assert.IsType<Verified>(r.Outcomes[0]);
        Assert.Equal(2, rig.Files.Calls.Count(c => c.StartsWith("CreateTemp ", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task TwoMismatches_AreFailedVerify_TheTempIsDeleted_AndTheRunContinues()
    {
        var rig = Videos(1_000, 2_000);
        var dest = rig.Batch.Jobs[0].DestPath;
        rig.Fs.Faults.CorruptVerify[dest] = 2;

        var r = await rig.RunEngineAsync();

        Assert.Equal(CopyPhase.Verify, Assert.IsType<Failed>(r.Outcomes[0]).Phase);
        Assert.IsType<Verified>(r.Outcomes[1]);
        Assert.Null(r.Stop);
        Assert.False(rig.Fs.Exists(dest));
        Assert.False(rig.Fs.Exists(OffloadPaths.TempOf(dest)));
    }

    [Fact]
    public async Task AppendFolderDeletedAfterTheScan_FailsItsJobs_AndIsNeverCreated()
    {
        var b = new OffloadPlanBuilder();
        var gone = new LibraryFolderRef(b.NewFolderPath(ZRel), new DateOnly(2026, 9, 27), "Zachar Bay");
        var a = b.Video("DJI_20260927160000_0160_D.MP4", 1_000, T0.AddHours(2));
        var n = b.Video("DJI_20260928160000_0170_D.MP4", 1_000, T0.AddDays(1));
        b.Group(new Append(gone, Confidence.High, "same day as clips already in this folder", null), Zachar, a)
         .Group(new NewFolder(@"2026\2026-09\2026-09-28 Next"), Zachar, n);
        var rig = new OffloadRig(b).Build();

        var r = await rig.RunEngineAsync();

        Assert.Equal(CopyPhase.CreateTemp, Assert.IsType<Failed>(r.Outcomes[0]).Phase);
        Assert.IsType<Verified>(r.Outcomes[1]);
        Assert.Null(r.Stop);
        Assert.False(rig.Fs.Exists(gone.FullPath));
        Assert.Contains($"EnsureDirectory {gone.FullPath} False", rig.Files.Calls);
    }

    [Fact]
    public async Task UnbufferedVerifyUnsupported_FallsBackToCached_AndSaysSo()
    {
        var rig = Videos(1_000);
        rig.Fs.Faults.UnbufferedUnsupported.Add(rig.Batch.Jobs[0].DestPath);

        var r = await rig.RunEngineAsync();

        Assert.Equal(VerifyMode.Cached, Assert.IsType<Verified>(r.Outcomes[0]).Mode);
        Assert.Equal("cached", Assert.IsType<FileRecord>(rig.Writer.Records[0]).Verify);
    }
}
