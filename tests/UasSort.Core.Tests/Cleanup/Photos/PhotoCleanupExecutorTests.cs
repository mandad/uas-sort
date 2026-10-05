// tests/UasSort.Core.Tests/Cleanup/Photos/PhotoCleanupExecutorTests.cs
using System.Text.Json;
using static UasSort.Testing.PhotoCleanupFixtures;

namespace UasSort.Core.Tests.Cleanup.Photos;

public sealed class PhotoCleanupExecutorTests
{
    private static readonly DateOnly D = new(2026, 6, 1);

    private sealed class Rig
    {
        public Rig()
        {
            Ledger = new FakeLedgerStore(Fs, FakeLayout.VideoRoot, FakeLayout.Machine);
            Recyclers = new FakePhotoRootRecyclerFactory(Fs, FakeLayout.Context(cardRoot: null));
        }

        public FakeFileSystem Fs { get; } = FakeLayout.NewFileSystem();
        public FakeLedgerStore Ledger { get; }
        public FakePhotoRootRecyclerFactory Recyclers { get; }
        public FakeOffloadLock Lock { get; } = new();
        public FakePowerRequest Power { get; } = new();
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 4, 20, 0, 0, TimeSpan.Zero));
        public ListProgress<PhotoCleanupProgress> Progress { get; } = new();
        public List<PhotoDeleteRecord> Records => [.. Ledger.Writer.Records.OfType<PhotoDeleteRecord>()];

        public PhotoMember File(string rel, long size)
        {
            Fs.AddFile(PhotoRoot + @"\" + rel, size, Mtime);
            return Member(rel, D, size);
        }

        public PhotoRow PhotoRow(string name, string? twin = null, bool verified = true)
            => Row(new PhotoItem(name, PhotoItemKind.Photo, PhotoSetKind.Unknown, null,
                                  twin is null ? [File(name, 1_000)] : [File(name, 1_000), File(twin, 400)]), verified);

        public PhotoRow SetRow(string folder, params string[] members)
            => Row(new PhotoItem(folder, PhotoItemKind.Set, PhotoSetKind.Panorama, PhotoCleanupRules.SetNameOf(folder),
                                  [.. members.Select((m, i) => File(folder + "\\" + m, 2_000 + i))]), evidence: PhotoEvidence.PanoramaStitch);

        public Task<PhotoCleanupResult> Run(ConfirmedPhotoCleanupPlan plan, CancellationToken? ct = null /* null = the test's own token (xUnit1051) */)
            => PhotoCleanupExecutor.RunAsync(plan, new PhotoCleanupEnvironment(Recyclers, Fs, Ledger, Lock, Power, Clock, FakeLayout.Machine),
                                             Progress, ct ?? TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Run_RecyclesEachChosenItem_AndRecordsEveryFileRightAfterItsMove()
    {
        var rig = new Rig();
        var pair = rig.PhotoRow("A.DNG", "A.JPG");
        var pano = rig.SetRow("001_0087", "PANO_0001.DNG", "PANO_0002.DNG");
        var recordsAtEachMove = new List<int>();
        rig.Recyclers.BeforeRecycle = _ => recordsAtEachMove.Add(rig.Records.Count);

        var result = await rig.Run(Confirmed(rig.Clock, PhotoCleanupMode.Verify, [pair, pano]));

        Assert.Null(result.Stop);
        Assert.All(result.Outcomes, o => Assert.IsType<PhotoRecycled>(o));
        Assert.Equal([PhotoRoot + @"\A.JPG", PhotoRoot + @"\A.DNG", PhotoRoot + @"\001_0087"], rig.Recyclers.Recycled);
        Assert.Equal([0, 1, 2], recordsAtEachMove);
        Assert.False(rig.Fs.Exists(PhotoRoot + @"\A.DNG"));
        var records = rig.Records;
        Assert.Equal(["A.JPG", "A.DNG", "PANO_0001.DNG", "PANO_0002.DNG"], records.Select(r => r.Name));
        Assert.Equal((PhotoRoot + @"\001_0087\PANO_0001.DNG", "001_0087", "panoramaStitch", "verify", new DateOnly(2026, 6, 30)),
                     (records[2].Dest, records[2].Set, records[2].Evidence, records[2].Mode, records[2].Cutoff));
        Assert.Equal(((string?)null, "lightroom", result.RunId), (records[0].Set, records[0].Evidence, records[0].Run));
        Assert.True(rig.Ledger.Calls.IndexOf("SnapshotToBackup " + result.RunId) is >= 0 and var snap && snap < rig.Ledger.Calls.IndexOf("OpenOwn"));
        Assert.Equal((0, 0, 1), (rig.Lock.Holds, rig.Power.Active, rig.Recyclers.Disposed));
        Assert.Equal(new PhotoCleanupProgress(2, 2, pair.Item.Bytes + pano.Item.Bytes, pair.Item.Bytes + pano.Item.Bytes, null), rig.Progress.Items[^1]);
    }

    [Fact]
    public async Task Run_RowsLeftOnKeep_AreNotTouched()
    {
        var rig = new Rig();
        var a = rig.PhotoRow("A.DNG");
        var b = rig.PhotoRow("B.DNG", verified: false);
        var plan = Plan(PhotoCleanupMode.Verify, new DateOnly(2026, 6, 30), [a, b]);
        var result = await rig.Run(plan.Confirm(new PhotoCleanupAck(plan.Fingerprint, [a.Key], true, false), rig.Clock));
        Assert.Single(result.Outcomes);
        Assert.True(rig.Fs.Exists(PhotoRoot + @"\B.DNG"));
        Assert.Equal(["A.DNG"], rig.Records.Select(r => r.Name));
    }

    [Fact] // [Review Focus 4]
    public async Task Run_ItemsChangedSinceReview_AreSkipped()
    {
        var rig = new Rig();
        var resized = rig.PhotoRow("A.DNG");
        var gone = rig.PhotoRow("B.DNG");
        var grown = rig.SetRow("001_0087", "PANO_0001.DNG");
        var touched = rig.SetRow("001_0088", "PANO_0001.DNG");
        var plan = Confirmed(rig.Clock, PhotoCleanupMode.BeforeDate, [resized, gone, grown, touched]);
        rig.Fs.Touch(PhotoRoot + @"\A.DNG", size: 999);
        rig.Fs.RemoveUnguarded(PhotoRoot + @"\B.DNG");
        rig.Fs.AddFile(PhotoRoot + @"\001_0087\PANO_0002.DNG", 5, Mtime);
        rig.Fs.Touch(PhotoRoot + @"\001_0088\PANO_0001.DNG", mtimeUtc: Mtime.AddSeconds(1));

        var result = await rig.Run(plan);

        Assert.Equal(["A.DNG changed since review", "B.DNG is no longer in Picture Offload",
                      "001_0087 changed since review (2 files now, 1 at review)", "PANO_0001.DNG in 001_0088 changed since review"],
                     result.Outcomes.Select(o => Assert.IsType<PhotoSkippedChanged>(o).Why));
        Assert.Empty(rig.Recyclers.Recycled);
        Assert.Empty(rig.Records);
        Assert.True(rig.Fs.Exists(PhotoRoot + @"\A.DNG"));
    }

    [Fact] // [Review Focus 4]
    public async Task Run_ASetFolderThatNowHoldsAFolder_OrCantBeListedAgain_IsSkipped()
    {
        var rig = new Rig();
        var nested = rig.SetRow("001_0087", "PANO_0001.DNG");
        var unlisted = rig.SetRow("001_0088", "PANO_0001.DNG");
        var plan = Confirmed(rig.Clock, PhotoCleanupMode.BeforeDate, [nested, unlisted]);
        rig.Fs.AddDirectory(PhotoRoot + @"\001_0087\edits");
        rig.Fs.Faults.EnumerationErrors[PathRules.Normalize(PhotoRoot + @"\001_0088")] = 5;

        var result = await rig.Run(plan);

        Assert.Equal(["001_0087 changed since review (it now holds a folder)", "001_0088 couldn't be listed again"],
                     result.Outcomes.Select(o => Assert.IsType<PhotoSkippedChanged>(o).Why));
        Assert.Empty(rig.Recyclers.Recycled);
        Assert.Empty(rig.Records);
    }

    [Fact]
    public async Task Run_ASetFolderTheShellMovedOnlyPartly_RecordsAndReportsTheMembersAlreadyGone()
    {
        var rig = new Rig();
        var pano = rig.SetRow("001_0087", "PANO_0001.DNG", "PANO_0002.DNG");
        rig.Recyclers.Fails.Add(PhotoRoot + @"\001_0087");                                    // IFileOperation fails part-way …
        rig.Recyclers.BeforeRecycle = _ => rig.Fs.RemoveUnguarded(PhotoRoot + @"\001_0087\PANO_0001.DNG");   // … after moving one member

        var result = await rig.Run(Confirmed(rig.Clock, PhotoCleanupMode.BeforeDate, [pano]));

        var partly = Assert.IsType<PhotoPartlyRecycled>(Assert.Single(result.Outcomes));
        Assert.Equal([@"001_0087\PANO_0001.DNG"], partly.Recycled);
        Assert.Equal([@"001_0087\PANO_0002.DNG"], partly.Left);
        Assert.Equal(["PANO_0001.DNG"], rig.Records.Select(r => r.Name));
        Assert.Equal("001_0087", rig.Records[0].Set);
        Assert.Null(result.Stop);
    }

    [Fact]
    public async Task Run_AnUnexpectedRecyclerException_EndsInAResult_NotAFault()
    {
        var rig = new Rig();
        var a = rig.PhotoRow("A.DNG");
        var b = rig.PhotoRow("B.DNG");
        rig.Recyclers.BeforeRecycle = p =>
        {
            if (p.EndsWith(@"\A.DNG", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("IFileOperation: no object");
        };

        var result = await rig.Run(Confirmed(rig.Clock, PhotoCleanupMode.BeforeDate, [a, b]));

        Assert.Equal("IFileOperation: no object", Assert.IsType<PhotoRecycleFailed>(result.Outcomes[0]).Error);
        Assert.IsType<PhotoRecycled>(result.Outcomes[1]);
        Assert.True(rig.Fs.Exists(PhotoRoot + @"\A.DNG"));
        Assert.Equal(["B.DNG"], rig.Records.Select(r => r.Name));
    }

    [Fact] // [Review Focus 5]
    public async Task Run_AnItemTheRecycleBinCantTake_IsKept_AndNotRecorded()
    {
        var rig = new Rig();
        var cloud = rig.PhotoRow("C.DNG");
        var pair = rig.PhotoRow("A.DNG", "A.JPG");
        var fine = rig.PhotoRow("F.DNG");
        rig.Recyclers.NotRecyclable.Add(PhotoRoot + @"\C.DNG");
        rig.Recyclers.NotRecyclable.Add(PhotoRoot + @"\A.DNG");

        var result = await rig.Run(Confirmed(rig.Clock, PhotoCleanupMode.BeforeDate, [cloud, pair, fine]));

        Assert.True(Assert.IsType<PhotoRecycleFailed>(result.Outcomes[0]).NotRecyclable);
        Assert.True(rig.Fs.Exists(PhotoRoot + @"\C.DNG"));
        var partly = Assert.IsType<PhotoPartlyRecycled>(result.Outcomes[1]);
        Assert.Equal(["A.JPG"], partly.Recycled);
        Assert.Equal(["A.DNG"], partly.Left);
        Assert.True(rig.Fs.Exists(PhotoRoot + @"\A.DNG"));
        Assert.IsType<PhotoRecycled>(result.Outcomes[2]);
        Assert.Equal(["A.JPG", "F.DNG"], rig.Records.Select(r => r.Name));
        Assert.Null(result.Stop);
    }

    [Fact]
    public async Task Run_AFailedLedgerAppend_StopsAndNamesTheUnrecordedFile()
    {
        var rig = new Rig();
        var pair = rig.PhotoRow("A.DNG", "A.JPG");
        var next = rig.PhotoRow("B.DNG");
        rig.Ledger.Writer.ThrowWhen = r => r is PhotoDeleteRecord { Name: "A.JPG" };

        var result = await rig.Run(Confirmed(rig.Clock, PhotoCleanupMode.BeforeDate, [pair, next]));

        Assert.Equal(PhotoCleanupStop.LedgerWriteFailed, result.Stop);
        Assert.Equal([PhotoRoot + @"\A.JPG"], result.Unrecorded);
        Assert.Equal(["A.DNG"], Assert.IsType<PhotoPartlyRecycled>(result.Outcomes[0]).Left);
        Assert.True(rig.Fs.Exists(PhotoRoot + @"\A.DNG"));
        Assert.IsType<PhotoNotStarted>(result.Outcomes[1]);
    }

    [Fact]
    public async Task Run_WithTheOffloadLockHeld_OrARefusingRecycler_OrACloudOnlyLedger_MovesNothing()
    {
        var locked = new Rig();
        var a = locked.PhotoRow("A.DNG");
        locked.Lock.HeldElsewhere = true;
        var r1 = await locked.Run(Confirmed(locked.Clock, PhotoCleanupMode.BeforeDate, [a]));
        Assert.Equal(PhotoCleanupStop.OffloadLockHeld, r1.Stop);
        Assert.IsType<PhotoNotStarted>(Assert.Single(r1.Outcomes));
        Assert.DoesNotContain("OpenOwn", locked.Ledger.Calls);

        var refused = new Rig();
        var b = refused.PhotoRow("B.DNG");
        refused.Recyclers.OpenThrows = true;
        var r2 = await refused.Run(Confirmed(refused.Clock, PhotoCleanupMode.BeforeDate, [b]));
        Assert.Equal(PhotoCleanupStop.RecyclerRefused, r2.Stop);
        Assert.True(refused.Fs.Exists(PhotoRoot + @"\B.DNG"));

        var cloud = new Rig();
        var c = cloud.PhotoRow("C.DNG");
        cloud.Ledger.StatusOverride = new LedgerFolderStatus(LedgerPaths.For(FakeLayout.VideoRoot), LedgerFolderState.CloudOnly, true, true, false,
                                                             true, [], [], []);
        var r3 = await cloud.Run(Confirmed(cloud.Clock, PhotoCleanupMode.BeforeDate, [c]));
        Assert.Equal(PhotoCleanupStop.LedgerUnavailable, r3.Stop);
        Assert.Empty(cloud.Recyclers.Recycled);
    }

    [Fact]
    public async Task Run_Cancelled_StopsAfterTheCurrentItem()
    {
        var rig = new Rig();
        var a = rig.PhotoRow("A.DNG");
        var b = rig.PhotoRow("B.DNG");
        using var cts = new CancellationTokenSource();
        rig.Recyclers.BeforeRecycle = _ => cts.Cancel();
        var result = await rig.Run(Confirmed(rig.Clock, PhotoCleanupMode.BeforeDate, [a, b]), cts.Token);
        Assert.Equal(PhotoCleanupStop.Cancelled, result.Stop);
        Assert.IsType<PhotoRecycled>(result.Outcomes[0]);
        Assert.IsType<PhotoNotStarted>(result.Outcomes[1]);
    }

    [Fact]
    public async Task Report_ListsEveryRowWithItsDecisionEvidenceAndOutcome()
    {
        var rig = new Rig();
        var a = rig.PhotoRow("A.DNG");
        var b = rig.PhotoRow("B.DNG", verified: false);
        var plan = Plan(PhotoCleanupMode.Verify, new DateOnly(2026, 6, 30), [a, b], notTouched: ["notes.txt"]);
        var result = await rig.Run(plan.Confirm(new PhotoCleanupAck(plan.Fingerprint, [a.Key], true, false), rig.Clock));

        var report = PhotoCleanupReports.Build(result);

        Assert.Equal(("A.DNG", "delete", "lightroom", "moved to the Recycle Bin", true),
                     (report.Items[0].Item, report.Items[0].Decision, report.Items[0].Evidence, report.Items[0].Outcome, report.Items[0].LedgerRecorded));
        Assert.Equal(("B.DNG", "keep", "kept", false), (report.Items[1].Item, report.Items[1].Decision, report.Items[1].Outcome, report.Items[1].LedgerRecorded));
        Assert.Equal(["notes.txt"], report.NotTouched);
        var json = JsonSerializer.Serialize(report, CoreJsonContext.Default.PhotoCleanupReport);
        Assert.Contains("\"mode\": \"Verify\"", json, StringComparison.Ordinal);
        Assert.Contains("\"outcome\": \"moved to the Recycle Bin\"", json, StringComparison.Ordinal);
    }
}
