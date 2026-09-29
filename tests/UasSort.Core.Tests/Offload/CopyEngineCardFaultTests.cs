// tests/UasSort.Core.Tests/Offload/CopyEngineCardFaultTests.cs
using UasSort.Core;
using UasSort.Core.Offload;
using UasSort.Testing.Offload;
using static UasSort.Core.Tests.Offload.CopyEngineTests;
using static UasSort.Testing.Offload.OffloadPlanBuilder;

namespace UasSort.Core.Tests.Offload;

public class CopyEngineCardFaultTests
{
    [Fact]
    public async Task TransientReadError_ReReadsTheChunkOnce_AndTheCopyCompletes()
    {
        var rig = Videos(2 * CopyEngine.ChunkBytes + 7);
        var job = rig.Batch.Jobs[0];
        rig.Fs.Faults.TransientCardReadError.Add(job.CardRelPath);

        var r = await rig.RunEngineAsync();

        Assert.IsType<Verified>(r.Outcomes[0]);
        Assert.Equal(rig.CardData[job.CardRelPath], rig.Fs.PeekContent(job.DestPath));
        Assert.Null(r.Stop);
    }

    [Fact]
    public async Task PersistentReadError_WithTheCardPresent_FailsThatFile_AndContinues()
    {
        var rig = Videos(1_000, 2_000);
        var job = rig.Batch.Jobs[0];
        rig.Fs.Faults.PersistentCardReadError.Add(job.CardRelPath);

        var r = await rig.RunEngineAsync();

        Assert.Equal(CopyPhase.Copy, Assert.IsType<Failed>(r.Outcomes[0]).Phase);
        Assert.IsType<Verified>(r.Outcomes[1]);
        Assert.Null(r.Stop);
        Assert.False(rig.Fs.Exists(OffloadPaths.TempOf(job.DestPath)));
    }

    [Fact]
    public async Task CardVanishingMidFile_IsFailedCopy_StopsWithCardRemoved()
    {
        var rig = Videos(2 * CopyEngine.ChunkBytes, 1_000);
        var job = rig.Batch.Jobs[0];
        rig.Fs.Faults.CardVanishesAfterBytes[job.CardRelPath] = CopyEngine.ChunkBytes + 10;

        var r = await rig.RunEngineAsync();

        Assert.Equal(["Failed", "NotStarted"], r.Kinds());
        Assert.Equal(CopyPhase.Copy, ((Failed)r.Outcomes[0]).Phase);
        Assert.Equal(StopReason.CardRemoved, r.Stop);
        Assert.False(rig.Fs.Exists(OffloadPaths.TempOf(job.DestPath)));
        Assert.False(rig.Fs.Exists(job.DestPath));
    }

    [Fact]
    public async Task ADifferentCardAfterAReadError_IsCardSwapped_AndStops()
    {
        var rig = Videos(1_000, 2_000, 3_000);
        var job = rig.Batch.Jobs[1];
        var other = OffloadPlanBuilder.Card with { VolumeSerial = 0xDEADBEEF };
        rig.Fs.Faults.PersistentCardReadError.Add(job.CardRelPath);
        rig.Fs.Faults.IdentityOnCall = n => n >= 3 ? other : null;   // 1, 2: step 0 of jobs 1 and 2; 3: after the failed re-read

        var r = await rig.RunEngineAsync();

        Assert.Equal(["Verified", "CardSwapped", "NotStarted"], r.Kinds());
        Assert.Equal(StopReason.CardSwapped, r.Stop);
        Assert.False(rig.Fs.Exists(OffloadPaths.TempOf(job.DestPath)));
    }
}
