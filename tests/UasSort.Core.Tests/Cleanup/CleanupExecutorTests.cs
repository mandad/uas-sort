// tests/UasSort.Core.Tests/Cleanup/CleanupExecutorTests.cs
#pragma warning disable CA1861 // expected path lists read best inline next to their scenario; each test runs once
using static UasSort.Core.Tests.Cleanup.CleanupScenario;

namespace UasSort.Core.Tests.Cleanup;

public sealed class CleanupExecutorTests : IDisposable
{
    private readonly List<CleanupExecutorHarness> _harnesses = [];

    private CleanupExecutorHarness Harness(CleanupScenario s)
    {
        var h = new CleanupExecutorHarness(s);
        _harnesses.Add(h);
        return h;
    }

    public void Dispose()                                                    // the card-delete and hydration tripwires, at teardown
    {
        foreach (var h in _harnesses) h.Fs.AssertNoViolations();
    }

    private static (CleanupScenario S, ItemId Video, ItemId Set, ItemId Moved) Card()
    {
        var s = new CleanupScenario();
        var video = s.AddVideo(Utc(2026, 7, 20, 20, 0), companions: Comp.Lrf);
        var set = s.AddSet("001_0087", Utc(2026, 7, 21, 20, 0));
        var moved = s.AddPhoto(Utc(2026, 7, 22, 20, 0), AuditCategory.InLedger, listed: false, ledger: VerifyKind.Cached);
        s.AddVideo(Utc(2026, 7, 28, 20, 0));                                 // after the cutoff
        return (s, video, set, moved);
    }

    [Fact]
    public async Task Happy_path_deletes_oldest_first_primary_last_and_releases_everything()
    {
        var (s, video, set, moved) = Card();
        var h = Harness(s);
        var confirmed = s.Confirmed(Before(2026, 7, 26));
        var heldDuringDeletes = new List<int>();
        h.Fs.Faults.OnCardDelete = _ => heldDuringDeletes.Add(h.Thumbs.ActivePauses * 100 + h.Lock.Holds * 10 + h.Power.Active);

        var r = await h.Run(confirmed, TestContext.Current.CancellationToken);

        Assert.Null(r.Stop);
        Assert.Equal(new[] { video, set, moved }, r.Outcomes.Select(o => o.Unit));
        Assert.All(r.Outcomes, o => Assert.IsType<Deleted>(o));
        Assert.True(((Deleted)r.Outcomes[1]).SetFolderRemoved);
        Assert.Equal(new[] { "DCIM/DJI_001/DJI_20260720120000_0101_D.LRF", "DCIM/DJI_001/DJI_20260720120000_0101_D.MP4",
                      "DCIM/PANORAMA/001_0087/PANO_0001.DNG", "DCIM/PANORAMA/001_0087/PANO_0002.DNG", "DCIM/PANORAMA/001_0087/PANO_0003.DNG",
                      "DCIM/DJI_001/DJI_20260722120000_0102_D.DNG" }, h.Deleted);
        Assert.Equal(new[] { "DCIM/PANORAMA/001_0087" }, h.RemovedDirs);
        Assert.True(h.OnCard("DCIM/DJI_001/DJI_20260728120000_0103_D.MP4"));
        Assert.Equal(6, heldDuringDeletes.Count);
        Assert.All(heldDuringDeletes, v => Assert.Equal(111, v));            // pause, lock and keep-awake held for every delete
        Assert.Equal((0, 0, 0), (h.Thumbs.ActivePauses, h.Lock.Holds, h.Power.Active));
        Assert.Equal(new[] { "Cleaning up drone card" }, h.Power.Reasons);
        var order = new[] { "Check", "EnsureFolder", "SnapshotToBackup", "OpenOwn", "Load" }.Select(h.CallIndex).ToArray();
        Assert.DoesNotContain(-1, order);
        Assert.Equal(order.Order(), order);                                  // ledger prepared, then the fresh ledger loaded
        Assert.True(h.Ledger.Writer.Disposed);
        Assert.DoesNotContain(h.Fs.GuardLog, g => g.Op == IoOp.ReadData);    // no card or library file opened
        Assert.Equal(1, h.Erasers.OpenCount);
        Assert.Equal(confirmed.Plan.SpaceBefore.FreeBytes + confirmed.Plan.AllocatedBytes, r.SpaceAfter.FreeBytes);
        Assert.Empty(r.StillListed);
        Assert.Same(confirmed, r.Plan);
        Assert.Equal(new DateTime(2026, 10, 12, 19, 30, 5, DateTimeKind.Utc), r.StartUtc);
        var last = h.Progress.Items[^1];
        Assert.Equal((6, 6), (last.FilesDone, last.FilesTotal));
    }

    [Fact]
    public async Task One_card_delete_record_per_deleted_file_with_its_evidence_and_no_run_record()
    {
        var s = new CleanupScenario { CopyJpgTwin = false };
        var video = s.AddVideo(Utc(2026, 7, 20, 20, 0), companions: Comp.Lrf);
        s.AddSet("001_0087", Utc(2026, 7, 21, 20, 0), members: 1);
        s.AddPhoto(Utc(2026, 7, 22, 20, 0), AuditCategory.InLedger, listed: false, ledger: VerifyKind.Cached);
        s.AddPhoto(Utc(2026, 7, 23, 20, 0), AuditCategory.InLedger, listed: true, twin: true,
                   twinCategory: AuditCategory.SkippedByRule, twinListed: false, ledger: VerifyKind.Unbuffered);
        s.AddVideo(Utc(2026, 7, 24, 20, 0), AuditCategory.Unaccounted, listed: false);
        var h = Harness(s);
        var confirmed = s.Confirmed(Before(2026, 7, 26, include: true));

        var r = await h.Run(confirmed, TestContext.Current.CancellationToken);

        Assert.Null(r.Stop);
        var records = h.Ledger.Writer.Records.Cast<CardDeleteRecord>().ToList();
        Assert.Equal(h.Deleted, records.Select(x => x.Src));
        Assert.Equal(new[]
        {
            "companionOf:NameSizeMatch", "NameSizeMatch",                    // LRF, then its MP4
            "NameSizeMatch",                                                  // set member
            "historyOnly:InLedger",                                           // photo moved by Lightroom
            "companionOf:InLedger", "InLedger",                               // JPG twin (copying off), then its DNG
            "notInLibraryConfirmed",                                          // reviewed new clip
        }, records.Select(x => x.Evidence));
        Assert.DoesNotContain(h.Ledger.Writer.Records, x => x is RunRecord);
        var mp4 = records[1];
        Assert.Equal((1, "DJI_20260720120000_0101_D.MP4", 1_000_000_000L), (mp4.V, mp4.Name, mp4.Size));
        Assert.Equal(video.CardRelPath, mp4.Unit);
        Assert.Equal(Utc(2026, 7, 20, 20, 0), mp4.CaptureUtc);
        Assert.Equal("same name and size in the library", mp4.Reason);
        Assert.Equal("beforeDate", mp4.Mode);
        Assert.Equal(CommitTailRecords.Card(Identity, "FC9113", "9f3c0a6d12e4b7a1"), mp4.Card);
        Assert.Equal(new RunCard("1A2B3C4D", null, "exFAT", "FC9113", "9f3c0a6d12e4b7a1"), mp4.Card);
        Assert.Equal(r.RunId, mp4.Run);
        Assert.Equal(Environment.MachineName, mp4.Machine);
        Assert.Equal(new DateTime(2026, 10, 12, 19, 30, 5, DateTimeKind.Utc), mp4.At);
        Assert.Null(mp4.Set);
        Assert.Equal("001_0087", records[2].Set);
        Assert.Equal(records.Count, records.Select(x => x.Id).Distinct().Count());
    }

    [Fact]
    public async Task A_deleted_file_that_stays_listed_is_reported_as_still_listed()
    {
        var (s, video, _, _) = Card();
        var h = Harness(s);
        h.Fs.Faults.DeletePending.Add("DCIM/DJI_001/DJI_20260720120000_0101_D.MP4");
        var r = await h.Run(s.Confirmed(Before(2026, 7, 26)), TestContext.Current.CancellationToken);
        Assert.IsType<Deleted>(r.Outcomes.Single(o => o.Unit == video));
        Assert.Equal(new[] { "DCIM/DJI_001/DJI_20260720120000_0101_D.MP4" }, r.StillListed);
    }

    [Fact]
    public async Task Offload_lock_held_elsewhere_deletes_nothing()
    {
        var (s, _, _, _) = Card();
        var h = Harness(s);
        h.Lock.HeldElsewhere = true;
        var r = await h.Run(s.Confirmed(Before(2026, 7, 26)), TestContext.Current.CancellationToken);
        Assert.Equal(CleanupStop.OffloadLockHeld, r.Stop);
        Assert.All(r.Outcomes, o => Assert.IsType<CleanupNotStarted>(o));
        Assert.Empty(h.Deleted);
        Assert.Empty(h.Ledger.Calls);
        Assert.Empty(h.Power.Reasons);
        Assert.Equal(0, h.Thumbs.ActivePauses);
    }

    [Theory]
    [InlineData(LedgerFolderState.CloudOnly)]
    [InlineData(LedgerFolderState.Unwritable)]
    [InlineData(LedgerFolderState.VideoRootMissing)]
    public async Task Unavailable_ledger_folder_deletes_nothing(LedgerFolderState state)
    {
        var (s, _, _, _) = Card();
        var h = Harness(s);
        h.SetLedgerState(state);
        var r = await h.Run(s.Confirmed(Before(2026, 7, 26)), TestContext.Current.CancellationToken);
        Assert.Equal(CleanupStop.LedgerUnavailable, r.Stop);
        Assert.All(r.Outcomes, o => Assert.IsType<CleanupNotStarted>(o));
        Assert.Empty(h.Deleted);
        Assert.Equal(new[] { "Check" }, h.Ledger.Calls);
        Assert.Equal((0, 0, 0), (h.Thumbs.ActivePauses, h.Lock.Holds, h.Power.Active));
    }

    [Fact]
    public async Task Missing_ledger_folder_is_created_before_the_writer_is_opened()
    {
        var (s, _, _, _) = Card();
        var h = Harness(s);
        h.SetLedgerState(LedgerFolderState.Missing);
        var r = await h.Run(s.Confirmed(Before(2026, 7, 26)), TestContext.Current.CancellationToken);
        Assert.Null(r.Stop);
        Assert.True(h.CallIndex("EnsureFolder") < h.CallIndex("OpenOwn"));
        Assert.True(h.CallIndex("SnapshotToBackup") < h.CallIndex("OpenOwn"));
    }

    [Fact]
    public async Task Failing_to_create_the_ledger_folder_stops_before_the_first_delete()
    {
        var (s, _, _, _) = Card();
        var h = Harness(s);
        h.SetLedgerState(LedgerFolderState.Missing);
        h.Ledger.EnsureFolderThrows = true;
        var r = await h.Run(s.Confirmed(Before(2026, 7, 26)), TestContext.Current.CancellationToken);
        Assert.Equal(CleanupStop.LedgerUnavailable, r.Stop);
        Assert.Empty(h.Deleted);
        Assert.Equal(-1, h.CallIndex("OpenOwn"));
    }

    [Fact]
    public async Task Eraser_factory_refusal_is_an_internal_safety_stop_with_nothing_deleted()
    {
        var (s, _, _, _) = Card();
        var h = Harness(s);
        h.Erasers.VolumeVerified = false;
        var r = await h.Run(s.Confirmed(Before(2026, 7, 26)), TestContext.Current.CancellationToken);
        Assert.Equal(CleanupStop.InternalSafetyStop, r.Stop);
        Assert.All(r.Outcomes, o => Assert.IsType<CleanupNotStarted>(o));
        Assert.Empty(h.Deleted);
        Assert.Equal((0, 0, 0), (h.Thumbs.ActivePauses, h.Lock.Holds, h.Power.Active));
    }
}
