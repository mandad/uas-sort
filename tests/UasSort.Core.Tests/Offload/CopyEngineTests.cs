// tests/UasSort.Core.Tests/Offload/CopyEngineTests.cs
using System.IO.Hashing;
using UasSort.Core;
using UasSort.Core.Offload;
using UasSort.Testing;
using UasSort.Testing.Offload;
using static UasSort.Testing.Offload.OffloadPlanBuilder;

namespace UasSort.Core.Tests.Offload;

public class CopyEngineTests
{
    internal const string ZRel = @"2026\2026-09\2026-09-27 Zachar Bay";

    /// <summary>A NewFolder group of videos with the given sizes, one minute apart.</summary>
    internal static OffloadRig Videos(params long[] sizes)
    {
        var b = new OffloadPlanBuilder();
        var ids = sizes.Select((s, i) => b.Video($"DJI_2026092714{i:00}00_{i + 1:0000}_D.MP4", s, T0.AddMinutes(i), gps: Zachar)).ToArray();
        b.Group(new NewFolder(ZRel), Zachar, ids);
        return new OffloadRig(b).Build();
    }

    [Fact]
    public async Task CopiesVerifiesAndRenames_WritingFileAndFolderRecords()
    {
        var rig = Videos(3 * CopyEngine.ChunkBytes + 5, CopyEngine.ChunkBytes);

        var r = await rig.RunEngineAsync();

        Assert.Null(r.Stop);
        Assert.Equal("run-1", r.RunId);
        Assert.Equal(["Verified", "Verified"], r.Kinds());
        foreach (var job in rig.Batch.Jobs)
        {
            Assert.Equal(rig.CardData[job.CardRelPath], rig.Fs.PeekContent(job.DestPath));
            Assert.False(rig.Fs.Exists(OffloadPaths.TempOf(job.DestPath)));
            Assert.Equal(0u, rig.Fs.GetAttributes(job.DestPath)!.Value & 0x2u);
        }
        var records = rig.Writer.Records;
        Assert.Equal(["FileRecord", "FileRecord", "FolderRecord"], records.Select(x => x.GetType().Name).ToArray());
        var file = (FileRecord)records[0];
        var job0 = rig.Batch.Jobs[0];
        var verified = (Verified)r.Outcomes[0];
        Assert.Equal(XxHash128.HashToUInt128(rig.CardData[job0.CardRelPath]), verified.Hash);
        Assert.Equal(verified.Hash.ToString("x32", System.Globalization.CultureInfo.InvariantCulture), file.Xxh128);
        Assert.Equal(verified.Mode == VerifyMode.Cached ? "cached" : "unbuffered", file.Verify);
        Assert.Equal((1, "DESKTOP-A", "run-1", "video", "video"), (file.V, file.Machine, file.Run, file.Kind, file.Root));
        Assert.Equal(("DJI_20260927140000_0001_D.MP4", 3 * CopyEngine.ChunkBytes + 5L, job0.CardRelPath, job0.DestPath),
                     (file.Name, file.Size, file.Src, file.Dest));
        Assert.Equal((job0.CardMtimeUtc, (DateTime?)T0, "Mvhd", Tz, (DateOnly?)new DateOnly(2026, 9, 27)),
                     (file.Mtime, file.CaptureUtc, file.TimeSource, file.Tz, file.LocalDate));
        Assert.Equal((Zachar.Lat, Zachar.Lon), (file.Lat!.Value, file.Lon!.Value));
        var folder = (FolderRecord)records[2];
        Assert.Equal((rig.B.NewFolderPath(ZRel), "Zachar Bay", "created", "run-1", Tz), (folder.Path, folder.Desc, folder.Source, folder.Run, folder.Tz));
        rig.Fs.AssertNoViolations();
    }

    [Fact]
    public async Task DestinationWithTheSameSize_IsAlreadyThere_WithANameSizeRecord()
    {
        var rig = Videos(1_000);
        var dest = rig.Batch.Jobs[0].DestPath;
        rig.Fs.AddFile(dest, 1_000, T0);
        var before = rig.Fs.PeekContent(dest);

        var r = await rig.RunEngineAsync();

        Assert.Equal(["AlreadyThere"], r.Kinds());
        var file = Assert.IsType<FileRecord>(rig.Writer.Records[0]);
        Assert.Equal(("nameSize", (string?)null), (file.Verify, file.Xxh128));
        Assert.Equal(before, rig.Fs.PeekContent(dest));
        Assert.Equal("created", Assert.IsType<FolderRecord>(rig.Writer.Records[1]).Source);   // a file landed, so the group's folder is recorded
    }

    [Fact]
    public async Task DestinationWithAnotherSize_IsConflictAtRename_AndNothingIsWritten()
    {
        var rig = Videos(1_000);
        var dest = rig.Batch.Jobs[0].DestPath;
        rig.Fs.AddFile(dest, 7, T0);

        var r = await rig.RunEngineAsync();

        Assert.Equal(["ConflictAtRename"], r.Kinds());
        Assert.Null(r.Stop);
        Assert.Empty(rig.Writer.Records);
        Assert.Equal(7, rig.Fs.PeekContent(dest).Length);
    }

    [Fact]
    public async Task TargetAppearingBeforeTheRename_IsConflictAtRename_TheTempIsDeleted_AndTheRunContinues()
    {
        var rig = Videos(1_000, 2_000);
        var dest = rig.Batch.Jobs[0].DestPath;
        rig.Fs.Faults.TargetAppearsBeforeRename.Add(dest);

        var r = await rig.RunEngineAsync();

        Assert.Equal(["ConflictAtRename", "Verified"], r.Kinds());
        byte[] marker = [0x42];                                          // the 1-byte file Part 02's FakeFileOps puts there
        Assert.Equal(marker, rig.Fs.PeekContent(dest));
        Assert.False(rig.Fs.Exists(OffloadPaths.TempOf(dest)));
        Assert.Null(r.Stop);
    }

    [Fact]
    public async Task WrongSizeAfterTheRename_IsFailedConfirm_AndTheRunContinues()
    {
        var rig = Videos(1_000, 2_000);
        rig.Fs.Faults.SizeAfterRename[rig.Batch.Jobs[0].DestPath] = 999;

        var r = await rig.RunEngineAsync();

        Assert.Equal(CopyPhase.Confirm, Assert.IsType<Failed>(r.Outcomes[0]).Phase);
        Assert.IsType<Verified>(r.Outcomes[1]);
        Assert.Null(r.Stop);
        Assert.Single(rig.Writer.Records.OfType<FileRecord>());
    }

    [Fact]
    public async Task IdentityChangeAtStepZero_IsCardSwapped_AndStops()
    {
        var rig = Videos(1_000, 2_000, 3_000);
        var other = OffloadPlanBuilder.Card with { VolumeSerial = 0xDEADBEEF };
        rig.Fs.Faults.IdentityOnCall = n => n >= 2 ? other : null;

        var r = await rig.RunEngineAsync();

        Assert.Equal(["Verified", "CardSwapped", "NotStarted"], r.Kinds());
        Assert.Equal(other, ((CardSwapped)r.Outcomes[1]).Now);
        Assert.Equal(StopReason.CardSwapped, r.Stop);
        Assert.False(rig.Fs.Exists(rig.Batch.Jobs[1].DestPath));
    }

    [Fact]
    public async Task CardFileChangedSinceTheScan_IsChangedOnCard_AndTheRunContinues()
    {
        var rig = Videos(1_000, 2_000);
        var job = rig.Batch.Jobs[0];
        rig.Fs.AddFile(OffloadRig.CardPath(job.CardRelPath), 1_001, job.CardMtimeUtc);

        var r = await rig.RunEngineAsync();

        var changed = Assert.IsType<ChangedOnCard>(r.Outcomes[0]);
        Assert.Equal(1_001, changed.NowSize);
        Assert.IsType<Verified>(r.Outcomes[1]);
        Assert.Null(r.Stop);
    }

    [Fact]
    public async Task LedgerAppendFailure_KeepsTheFileVerified_AndStops()
    {
        var rig = Videos(1_000, 2_000);
        rig.Writer.ThrowWhen = x => x is FileRecord;

        var r = await rig.RunEngineAsync();

        Assert.Equal(["Verified", "NotStarted"], r.Kinds());
        Assert.Equal(StopReason.LedgerWriteFailed, r.Stop);
        Assert.True(rig.Fs.Exists(rig.Batch.Jobs[0].DestPath));
        Assert.Empty(rig.Writer.Records);
    }

    [Fact]
    public async Task CancelBetweenChunks_CancelsTheInFlightFile_AndLeavesNoTempAndNoRecord()
    {
        var rig = Videos(1_000, 2 * CopyEngine.ChunkBytes + 1, 3_000);
        using var cts = new CancellationTokenSource();
        var second = rig.Batch.Jobs[1].DestPath;
        rig.Files.OnTempWrite = (final, _) => { if (string.Equals(final, second, StringComparison.OrdinalIgnoreCase)) cts.Cancel(); };

        var r = await rig.RunEngineAsync(ct: cts.Token);

        Assert.Equal(["Verified", "Cancelled", "NotStarted"], r.Kinds());
        Assert.Equal(StopReason.Cancelled, r.Stop);
        Assert.False(rig.Fs.Exists(second));
        Assert.False(rig.Fs.Exists(OffloadPaths.TempOf(second)));
        Assert.Equal(["FileRecord", "FolderRecord"], rig.Writer.Records.Select(x => x.GetType().Name).ToArray());
    }

    [Fact]
    public async Task UnsafeIo_IsAnInternalSafetyStop()
    {
        var rig = Videos(1_000, 2_000);
        rig.Files.ThrowOnFinalize = new UnsafeIoException("SetAttributesOrTimes refused");

        var r = await rig.RunEngineAsync();

        Assert.Equal(CopyPhase.Finalize, Assert.IsType<Failed>(r.Outcomes[0]).Phase);
        Assert.IsType<NotStarted>(r.Outcomes[1]);
        Assert.Equal(StopReason.InternalSafetyStop, r.Stop);
        Assert.False(rig.Fs.Exists(OffloadPaths.TempOf(rig.Batch.Jobs[0].DestPath)));
    }

    [Fact]
    public async Task Progress_IsThrottledToTenPerSecond_WithRollingSpeed()
    {
        var rig = Videos(6 * CopyEngine.ChunkBytes);
        var clock = EngineRun.Clock();
        rig.Files.OnTempWrite = (_, _) => clock.Advance(TimeSpan.FromMilliseconds(50));
        var progress = new ListProgress<OffloadProgress>();

        await rig.RunEngineAsync(clock, progress: progress);

        var items = progress.Items;
        Assert.Equal(4, items.Count);
        Assert.Equal([2 * CopyEngine.ChunkBytes, 4L * CopyEngine.ChunkBytes, 6L * CopyEngine.ChunkBytes, 6L * CopyEngine.ChunkBytes],
                     items.Select(p => p.BytesDone).ToArray());
        Assert.Equal(21.0, items[2].MBps);
        Assert.InRange(items[0].Eta!.Value.TotalSeconds, 0.19, 0.21);
        Assert.Equal((1, 1, "DJI_20260927140000_0001_D.MP4"), (items[3].FilesDone, items[3].FilesTotal, items[3].CurrentFile));
        Assert.Equal(new GroupId(rig.Batch.Jobs[0].Item), items[3].Group);
    }
}
