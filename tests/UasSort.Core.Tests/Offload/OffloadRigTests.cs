// tests/UasSort.Core.Tests/Offload/OffloadRigTests.cs
using UasSort.Core;
using UasSort.Core.Offload;
using UasSort.Testing;
using UasSort.Testing.Offload;
using static UasSort.Testing.Offload.OffloadPlanBuilder;

namespace UasSort.Core.Tests.Offload;

public class OffloadRigTests
{
    private const string ZRel = @"2026\2026-09\2026-09-27 Zachar Bay";

    private static OffloadRig OneVideo()
    {
        var b = new OffloadPlanBuilder();
        var v = b.Video("DJI_20260927140000_0123_D.MP4", 1_500_000, T0);
        b.Group(new NewFolder(ZRel), Zachar, v);
        return new OffloadRig(b).Build();
    }

    [Fact]
    public void FakeFileOps_WritesVerifiesAndRenamesAnOwnTemp()
    {
        var rig = OneVideo();
        var job = rig.Batch.Jobs[0];
        var data = rig.CardData[job.CardRelPath];

        rig.Files.EnsureDirectory(OffloadPaths.DirectoryOf(job.DestPath), allowCreate: true);
        var stream = rig.Files.CreateTemp(job.DestPath, job.Size, out var temp);
        Assert.Equal(OffloadPaths.TempOf(job.DestPath), temp);
        Assert.NotEqual(0u, rig.Fs.GetAttributes(temp)!.Value & 0x2u);
        stream.Write(data);
        rig.Files.FlushToDisk(stream);
        stream.Dispose();
        var check = rig.Files.VerifyHash(temp, job.Size, System.IO.Hashing.XxHash128.HashToUInt128(data), CancellationToken.None);
        Assert.True(check is HashMatch { Mode: VerifyMode.Unbuffered });
        rig.Files.FinalizeAttributes(temp, job.CardCreationUtc, job.CardMtimeUtc);
        Assert.Equal(0u, rig.Fs.GetAttributes(temp)!.Value & 0x2u);
        Assert.True(rig.Files.RenameNoReplace(temp, job.DestPath) is Renamed);
        Assert.True(rig.Files.ConfirmFinal(job.DestPath, job.Size));

        Assert.Equal(data, rig.Fs.PeekContent(job.DestPath));
        Assert.False(rig.Fs.Exists(temp));
        Assert.Equal(job.CardMtimeUtc, rig.Fs.Metadata(job.DestPath)!.MtimeUtc);
        rig.Fs.AssertNoViolations();
    }

    [Fact]
    public void FakeFileOps_NeverCreatesAnAppendFolder()
    {
        var rig = OneVideo();
        var append = rig.B.NewFolderPath(@"2026\2026-08\2026-08-01 Gone");

        Assert.Throws<DirectoryNotFoundException>(() => rig.Files.EnsureDirectory(append, allowCreate: false));
        Assert.Throws<UnsafeIoException>(() => rig.Files.EnsureDirectory(append, allowCreate: true));
        Assert.False(rig.Fs.Exists(append));
        Assert.Throws<UnsafeIoException>(() => rig.Files.DeleteOwnTemp(rig.B.NewFolderPath("x.MP4")));
    }

    [Fact]
    public void FakeFileOps_HonoursTheDestinationFaults()
    {
        var rig = OneVideo();
        var job = rig.Batch.Jobs[0];
        var data = rig.CardData[job.CardRelPath];
        var expected = System.IO.Hashing.XxHash128.HashToUInt128(data);
        rig.Files.EnsureDirectory(OffloadPaths.DirectoryOf(job.DestPath), allowCreate: true);
        rig.Fs.Faults.CorruptVerify[job.DestPath] = 1;
        rig.Fs.Faults.TargetAppearsBeforeRename.Add(job.DestPath);

        using (var s = rig.Files.CreateTemp(job.DestPath, job.Size, out _)) s.Write(data);
        var temp = OffloadPaths.TempOf(job.DestPath);
        Assert.True(rig.Files.VerifyHash(temp, job.Size, expected, CancellationToken.None) is HashMismatch);
        Assert.True(rig.Files.VerifyHash(temp, job.Size, expected, CancellationToken.None) is HashMatch);
        rig.Files.FinalizeAttributes(temp, job.CardCreationUtc, job.CardMtimeUtc);
        Assert.True(rig.Files.RenameNoReplace(temp, job.DestPath) is TargetExists);
        rig.Files.DeleteOwnTemp(temp);
        Assert.NotEqual(data.Length, rig.Fs.PeekContent(job.DestPath).Length);

        rig.Fs.Faults.SizeAfterRename[job.DestPath] = 1;
        Assert.False(rig.Files.ConfirmFinal(job.DestPath, 3));

        rig.Fs.Faults.DiskFullAfterBytes = 10;
        var other = rig.B.NewFolderPath(ZRel + @"\other.MP4");
        using (var s = rig.Files.CreateTemp(other, 100, out _))
        {
            var e = Assert.Throws<IOException>(() => s.Write(new byte[100]));
            Assert.Equal(0x70, e.HResult & 0xFFFF);
        }
        rig.Fs.Faults.LostRoots.Add(rig.B.VideoRoot);
        Assert.Throws<DirectoryNotFoundException>(() => rig.Files.FreeBytes(other));
        rig.Fs.AssertNoViolations();
    }

    [Fact]
    public void FakeFileOps_RecordsCallsAndFlushedDestinations()
    {
        var rig = OneVideo();
        var dir = OffloadPaths.DirectoryOf(rig.Batch.Jobs[0].DestPath);
        rig.Files.EnsureDirectory(dir, allowCreate: true);

        rig.Files.FlushDestination(dir, []);

        Assert.Equal([$"EnsureDirectory {dir} True", $"FlushDestination {dir}"], rig.Files.Calls.ToArray());
        Assert.Equal([(dir, 0)], rig.Files.FlushedDestinations.ToArray());
        rig.Fs.AssertNoViolations();
    }

    [Fact]
    public void FakeLedgerStore_OnTheFileSystem_CreatesPinsAndAppendsOnlyTheOwnFile()
    {
        var rig = OneVideo();
        rig.Build(ledgerOnFileSystem: true);

        Assert.Equal(LedgerFolderState.Missing, rig.Ledger.Check().State);
        rig.Ledger.EnsureFolder();
        using (var w = rig.Ledger.OpenOwn()) w.Append(new TornRecord(1, "id-1", FakeLayout.Machine, T0, 3));

        Assert.Single(rig.Writer.Records);
        var own = LedgerPaths.OwnFile(rig.B.VideoRoot, FakeLayout.Machine);
        var line = System.Text.Encoding.UTF8.GetString(rig.Fs.PeekContent(own)).TrimEnd('\n');
        Assert.IsType<TornRecord>(LedgerCodec.TryParse(line, out _));
        Assert.Equal(LedgerFolderState.Ok, rig.Ledger.Check().State);
        Assert.Equal(["Check", "EnsureFolder", "OpenOwn", "Check"], rig.Ledger.Calls);
        rig.Fs.AssertNoViolations();
    }

    [Fact]
    public void FakeLedgerStore_CopyInto_CopiesEveryRecordButTorn_Once()
    {
        var rig = OneVideo();
        rig.Build(ledgerOnFileSystem: true);
        rig.Ledger.EnsureFolder();
        using (var w = rig.Ledger.OpenOwn())
        {
            w.Append(new RevokeRecord(1, "id-1", FakeLayout.Machine, T0, "dec-1"));
            w.Append(new TornRecord(1, "id-2", FakeLayout.Machine, T0, 3));
        }
        var current = LedgerSnapshots.Empty(rig.Ledger.Check()) with { SourceFiles = [LedgerPaths.OwnFile(rig.B.VideoRoot, FakeLayout.Machine)] };
        const string newRoot = @"C:\Users\u\Videos\UAS";
        rig.Fs.AddDirectory(newRoot);

        rig.Ledger.CopyInto(newRoot, current);
        rig.Ledger.CopyInto(newRoot, current);

        var copied = System.Text.Encoding.UTF8.GetString(rig.Fs.PeekContent(LedgerPaths.OwnFile(newRoot, FakeLayout.Machine)))
                           .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.IsType<RevokeRecord>(LedgerCodec.TryParse(Assert.Single(copied), out _));
        Assert.Equal($"CopyInto {newRoot}", rig.Ledger.Calls[^1]);
        rig.Fs.AssertNoViolations();
    }
}
