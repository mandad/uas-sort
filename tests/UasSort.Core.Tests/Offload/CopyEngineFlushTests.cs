// tests/UasSort.Core.Tests/Offload/CopyEngineFlushTests.cs
using UasSort.Core;
using UasSort.Core.Offload;
using UasSort.Testing.Offload;
using static UasSort.Testing.Offload.OffloadPlanBuilder;

namespace UasSort.Core.Tests.Offload;

public class CopyEngineFlushTests
{
    private static OffloadRig VideoOnCPhotosOnD(bool knowD = true)
    {
        var b = new OffloadPlanBuilder().WithPhotoRoot(@"D:\Photos");
        var v = b.Video("DJI_20260927140000_0001_D.MP4", 1_000, T0);
        b.Group(new NewFolder(@"2026\2026-09\2026-09-27 Zachar Bay"), Zachar, v);
        b.Photo("DJI_20260927140100_0002_D.DNG", 2_000, T0.AddMinutes(1));
        b.Photo("DJI_20260927140200_0003_D.DNG", 3_000, T0.AddMinutes(2));
        var rig = new OffloadRig(b).Build();
        if (knowD) rig.Volumes.Add(OffloadVolumes.ExFatD);
        return rig;
    }

    private sealed class CancelWhenFilesDone(int n, CancellationTokenSource cts) : IProgress<OffloadProgress>
    {
        public void Report(OffloadProgress p) { if (p.FilesDone == n) cts.Cancel(); }
    }

    [Fact]
    public async Task ExFatDestination_IsFlushed_AndNeedsSafeRemoval_NtfsIsNot()
    {
        var rig = VideoOnCPhotosOnD();

        var r = await rig.RunEngineAsync();

        Assert.Equal([@"D:\"], r.VolumesNeedingSafeRemoval.ToArray());
        Assert.Equal([(@"D:\Photos", 2)], rig.Files.FlushedDestinations.ToArray());
        rig.Fs.AssertNoViolations();
    }

    [Fact]
    public async Task AfterCancel_TheFilesAlreadyRenamed_AreStillFlushed()
    {
        var rig = VideoOnCPhotosOnD();
        using var cts = new CancellationTokenSource();

        var r = await rig.RunEngineAsync(ct: cts.Token, progress: new CancelWhenFilesDone(2, cts));

        Assert.Equal(["Verified", "Verified", "NotStarted"], r.Kinds());
        Assert.Equal(StopReason.Cancelled, r.Stop);
        Assert.Equal([(@"D:\Photos", 1)], rig.Files.FlushedDestinations.ToArray());
        Assert.Equal([@"D:\"], r.VolumesNeedingSafeRemoval.ToArray());
    }

    [Fact]
    public async Task AnUnknownVolume_IsTreatedAsNeedingSafeRemoval()
    {
        var rig = VideoOnCPhotosOnD(knowD: false);

        var r = await rig.RunEngineAsync();

        Assert.Equal([@"D:\"], r.VolumesNeedingSafeRemoval.ToArray());
    }

    [Fact]
    public async Task AllOnNtfsFixed_NothingIsFlushed()
    {
        var rig = CopyEngineTests.Videos(1_000, 2_000);

        var r = await rig.RunEngineAsync();

        Assert.Empty(r.VolumesNeedingSafeRemoval);
        Assert.Empty(rig.Files.FlushedDestinations);
    }
}
