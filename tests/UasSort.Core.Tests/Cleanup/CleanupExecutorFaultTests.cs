// tests/UasSort.Core.Tests/Cleanup/CleanupExecutorFaultTests.cs
#pragma warning disable CA1861 // expected path lists read best inline next to their scenario; each test runs once
using static UasSort.Core.Tests.Cleanup.CleanupScenario;

namespace UasSort.Core.Tests.Cleanup;

public sealed class CleanupExecutorFaultTests : IDisposable
{
    private static readonly CardIdentity OtherCard = Identity with { VolumeSerial = 0x5E6F7A8B };
    private readonly Dictionary<CleanupExecutorHarness, int> _expectedViolations = [];

    private CleanupExecutorHarness Harness(CleanupScenario s, int expectedViolations = 0)
    {
        var h = new CleanupExecutorHarness(s);
        _expectedViolations[h] = expectedViolations;
        return h;
    }

    public void Dispose()                                                    // the card-delete and hydration tripwires, at teardown
    {
        foreach (var (h, n) in _expectedViolations)
        {
            Assert.Equal(n, h.Fs.CardDeleteViolations.Count);
            Assert.Empty(h.Fs.HydrationViolations);
        }
    }

    private static (CleanupScenario S, ItemId[] Ids) Singles(int n)
    {
        var s = new CleanupScenario();
        var ids = Enumerable.Range(0, n).Select(i => s.AddVideo(Utc(2026, 7, 10 + i, 20, 0))).ToArray();
        return (s, ids);
    }

    private static string Rel(CleanupScenario s, ItemId id) => s.Primary(id).RelPath;

    private static string Full(string cardRelPath) => PathRules.Join(CardRoot, cardRelPath);

    private static Type[] Kinds(CleanupResult r) => [.. r.Outcomes.Select(o => o.GetType())];

    [Fact]
    public async Task Identity_change_before_the_third_file_stops_the_run()
    {
        var (s, _) = Singles(4);
        var h = Harness(s);
        var confirmed = s.Confirmed(Before(2026, 7, 26));
        h.Fs.Faults.OnCardDelete = _ => { if (h.Erasers.AllDeleted.Count == 1) h.Fs.SetCardIdentity(CardRoot, OtherCard); };
        var r = await h.Run(confirmed, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { typeof(Deleted), typeof(Deleted), typeof(CleanupCardSwapped), typeof(CleanupNotStarted) }, Kinds(r));
        Assert.Equal(OtherCard, ((CleanupCardSwapped)r.Outcomes[2]).Now);
        Assert.Equal(CleanupStop.CardSwapped, r.Stop);
        Assert.Equal(2, h.Deleted.Count);
    }

    [Fact]
    public async Task Identity_change_mid_unit_leaves_the_primary_on_the_card()
    {
        var s = new CleanupScenario();
        s.AddVideo(Utc(2026, 7, 10, 20, 0), companions: Comp.Lrf | Comp.Srt);
        s.AddVideo(Utc(2026, 7, 11, 20, 0));
        var h = Harness(s);
        var confirmed = s.Confirmed(Before(2026, 7, 26));
        h.Fs.Faults.OnCardDelete = _ => { if (h.Erasers.AllDeleted.Count == 1) h.Fs.SetCardIdentity(CardRoot, OtherCard); };
        var r = await h.Run(confirmed, TestContext.Current.CancellationToken);
        var partial = Assert.IsType<PartiallyDeleted>(r.Outcomes[0]);
        Assert.Equal(new[] { "DCIM/DJI_001/DJI_20260710120000_0101_D.LRF", "DCIM/DJI_001/DJI_20260710120000_0101_D.SRT" }, partial.DeletedPaths);
        Assert.Equal(new[] { "DCIM/DJI_001/DJI_20260710120000_0101_D.MP4" }, partial.StillOnCard);
        Assert.IsType<CleanupNotStarted>(r.Outcomes[1]);
        Assert.Equal(CleanupStop.CardSwapped, r.Stop);
    }

    [Fact]
    public async Task Changed_or_gone_files_are_skipped_and_the_run_continues()
    {
        var (s, ids) = Singles(4);
        var h = Harness(s);
        var confirmed = s.Confirmed(Before(2026, 7, 26));
        var touched = s.Primary(ids[1]).MtimeUtc.AddMinutes(1);
        h.Fs.Touch(Full(Rel(s, ids[1])), mtimeUtc: touched);
        h.Fs.RemoveUnguarded(Full(Rel(s, ids[2])));
        var r = await h.Run(confirmed, TestContext.Current.CancellationToken);
        Assert.Null(r.Stop);
        Assert.Equal(new[] { typeof(Deleted), typeof(SkippedChanged), typeof(SkippedChanged), typeof(Deleted) }, Kinds(r));
        var changed = (SkippedChanged)r.Outcomes[1];
        Assert.Equal((1_000_000_000L, touched), (changed.NowSize!.Value, changed.NowMtimeUtc!.Value));
        var gone = (SkippedChanged)r.Outcomes[2];
        Assert.Null(gone.NowSize);
        Assert.Null(gone.NowMtimeUtc);
        Assert.True(h.OnCard(Rel(s, ids[1])));
    }

    [Fact]
    public async Task Evidence_recheck_uses_fresh_listings_and_the_fresh_ledger()
    {
        var s = new CleanupScenario();
        var video = s.AddVideo(Utc(2026, 7, 10, 20, 0), AuditCategory.InLedger, ledger: VerifyKind.Unbuffered);
        var photo = s.AddPhoto(Utc(2026, 7, 11, 20, 0), AuditCategory.InLedger, ledger: VerifyKind.Unbuffered);
        var history = s.AddPhoto(Utc(2026, 7, 12, 20, 0), AuditCategory.InLedger, listed: false, ledger: VerifyKind.Cached);
        var confirmed = s.Confirmed(Before(2026, 7, 26));                      // planned while every proof held
        var hp = s.Primary(history);
        s.LedgerFiles.Remove(CleanupKeys.Key(hp.RelPath, hp.Size));            // the fresh ledger no longer has it
        var h = Harness(s);
        h.Fs.RemoveUnguarded($@"{VideoRoot}\{CouncilDir}\{CleanupPaths.Name(Rel(s, video))}");   // clip deleted from the library
        h.Fs.RemoveUnguarded($@"{PhotoRoot}\{CleanupPaths.Name(Rel(s, photo))}");                // Lightroom moved it; the ledger still has it

        var r = await h.Run(confirmed, TestContext.Current.CancellationToken);

        Assert.Null(r.Stop);
        var v = Assert.IsType<SkippedEvidenceGone>(r.Outcomes[0]);
        Assert.Equal((Rel(s, video), "no longer in the library listing"), (v.CardRelPath, v.Why));
        Assert.IsType<Deleted>(r.Outcomes[1]);
        var hg = Assert.IsType<SkippedEvidenceGone>(r.Outcomes[2]);
        Assert.Equal("no longer in the library listing or the history", hg.Why);
        Assert.True(h.OnCard(Rel(s, video)));
        Assert.True(h.OnCard(hp.RelPath));
    }

    [Fact]
    public async Task A_delete_error_on_a_sets_fifth_member_keeps_the_rest_and_the_folder()
    {
        var s = new CleanupScenario();
        var set = s.AddSet("001_0087", Utc(2026, 7, 10, 20, 0), members: 33);
        var next = s.AddVideo(Utc(2026, 7, 11, 20, 0));
        var h = Harness(s);
        var confirmed = s.Confirmed(Before(2026, 7, 26));
        h.Fs.Faults.DeleteErrors["DCIM/PANORAMA/001_0087/PANO_0005.DNG"] = CleanupExecutor.ErrorAccessDenied;
        var r = await h.Run(confirmed, TestContext.Current.CancellationToken);
        Assert.Null(r.Stop);
        var partial = Assert.IsType<PartiallyDeleted>(r.Outcomes[0]);
        Assert.Equal(set, partial.Unit);
        Assert.Equal(4, partial.DeletedPaths.Length);
        Assert.Equal(29, partial.StillOnCard.Length);
        Assert.Equal("DCIM/PANORAMA/001_0087/PANO_0005.DNG", partial.StillOnCard[0]);
        Assert.Empty(h.RemovedDirs);
        Assert.True(h.OnCard("DCIM/PANORAMA/001_0087/PANO_0033.DNG"));
        Assert.Equal(next, Assert.IsType<Deleted>(r.Outcomes[1]).Unit);
        Assert.Equal(5, h.Ledger.Writer.Records.Count);
    }

    [Fact]
    public async Task Write_protect_stops_the_run()
    {
        var (s, ids) = Singles(3);
        var h = Harness(s);
        var confirmed = s.Confirmed(Before(2026, 7, 26));
        h.Fs.Faults.DeleteErrors[Rel(s, ids[1])] = CleanupExecutor.ErrorWriteProtect;   // the lock switch flipped after the first delete
        var r = await h.Run(confirmed, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { typeof(Deleted), typeof(CleanupFailed), typeof(CleanupNotStarted) }, Kinds(r));
        var failed = (CleanupFailed)r.Outcomes[1];
        Assert.Equal((Rel(s, ids[1]), CleanupExecutor.ErrorWriteProtect), (failed.CardRelPath, failed.Win32Error));
        Assert.Equal(CleanupStop.WriteProtected, r.Stop);
    }

    [Theory]
    [InlineData(21)]                          // ERROR_NOT_READY from the delete itself
    [InlineData(1117)]                        // ERROR_IO_DEVICE, then CurrentIdentity() fails
    public async Task A_vanished_card_stops_with_card_removed(int error)
    {
        var (s, ids) = Singles(3);
        var h = Harness(s);
        var confirmed = s.Confirmed(Before(2026, 7, 26));
        var rel = Rel(s, ids[1]);
        h.Fs.Faults.DeleteErrors[rel] = error;
        h.Fs.Faults.OnCardDelete = p => { if (p == rel) h.Fs.Faults.CardRemoved = true; };   // the card goes as that delete fails
        var r = await h.Run(confirmed, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { typeof(Deleted), typeof(CleanupFailed), typeof(CleanupNotStarted) }, Kinds(r));
        Assert.Equal(error, ((CleanupFailed)r.Outcomes[1]).Win32Error);
        Assert.Equal(CleanupStop.CardRemoved, r.Stop);
        Assert.Equal(confirmed.Plan.SpaceBefore, r.SpaceAfter);
        Assert.Empty(r.StillListed);
    }

    [Fact]
    public async Task Access_denied_and_sharing_violation_fail_only_their_unit()
    {
        var (s, ids) = Singles(3);
        var h = Harness(s);
        var confirmed = s.Confirmed(Before(2026, 7, 26));
        h.Fs.Faults.DeleteErrors[Rel(s, ids[0])] = CleanupExecutor.ErrorAccessDenied;
        h.Fs.Faults.DeleteErrors[Rel(s, ids[1])] = CleanupExecutor.ErrorSharingViolation;
        var r = await h.Run(confirmed, TestContext.Current.CancellationToken);
        Assert.Null(r.Stop);
        Assert.Equal(new[] { typeof(CleanupFailed), typeof(CleanupFailed), typeof(Deleted) }, Kinds(r));
        Assert.Equal(CleanupExecutor.ErrorSharingViolation, ((CleanupFailed)r.Outcomes[1]).Win32Error);
    }

    [Fact]
    public async Task A_failed_ledger_append_stops_and_the_file_counts_as_deleted()
    {
        var (s, ids) = Singles(2);
        var h = Harness(s);
        var confirmed = s.Confirmed(Before(2026, 7, 26));
        h.Ledger.Writer.ThrowWhen = _ => true;                                   // the first append throws IOException
        var r = await h.Run(confirmed, TestContext.Current.CancellationToken);
        Assert.Equal(CleanupStop.LedgerWriteFailed, r.Stop);
        Assert.Equal(1, Assert.IsType<Deleted>(r.Outcomes[0]).Files);
        Assert.IsType<CleanupNotStarted>(r.Outcomes[1]);
        Assert.False(h.OnCard(Rel(s, ids[0])));
        Assert.Empty(h.Ledger.Writer.Records);

        var multi = new CleanupScenario();
        multi.AddVideo(Utc(2026, 7, 10, 20, 0), companions: Comp.Lrf);
        var hm = Harness(multi);
        var cm = multi.Confirmed(Before(2026, 7, 26));
        hm.Ledger.Writer.ThrowWhen = _ => true;
        var rm = await hm.Run(cm, TestContext.Current.CancellationToken);
        var partial = Assert.IsType<PartiallyDeleted>(rm.Outcomes[0]);
        Assert.Equal(new[] { "DCIM/DJI_001/DJI_20260710120000_0101_D.LRF" }, partial.DeletedPaths);
        Assert.Equal(new[] { "DCIM/DJI_001/DJI_20260710120000_0101_D.MP4" }, partial.StillOnCard);
    }

    [Fact]
    public async Task Cancel_stops_after_the_current_file()
    {
        var s = new CleanupScenario();
        s.AddVideo(Utc(2026, 7, 10, 20, 0), companions: Comp.Lrf | Comp.Srt);
        s.AddVideo(Utc(2026, 7, 11, 20, 0));
        var h = Harness(s);
        var confirmed = s.Confirmed(Before(2026, 7, 26));
        using var cts = new CancellationTokenSource();
        h.Fs.Faults.OnCardDelete = _ => cts.Cancel();
        var r = await h.Run(confirmed, cts.Token);
        Assert.Equal(CleanupStop.Cancelled, r.Stop);
        Assert.Single(Assert.IsType<PartiallyDeleted>(r.Outcomes[0]).DeletedPaths);
        Assert.IsType<CleanupNotStarted>(r.Outcomes[1]);
        Assert.Single(h.Deleted);

        var (s2, _) = Singles(2);
        var h2 = Harness(s2);
        var r2 = await h2.Run(s2.Confirmed(Before(2026, 7, 26)), new CancellationToken(canceled: true));
        Assert.Equal(CleanupStop.Cancelled, r2.Stop);
        Assert.All(r2.Outcomes, o => Assert.IsType<CleanupNotStarted>(o));
        Assert.Empty(h2.Deleted);
    }

    [Fact]
    public async Task An_emptied_set_folder_that_cannot_be_removed_is_kept()
    {
        var s = new CleanupScenario();
        s.AddSet("001_0087", Utc(2026, 7, 10, 20, 0));
        var h = Harness(s);
        var confirmed = s.Confirmed(Before(2026, 7, 26));
        h.Fs.AddFile(Full("DCIM/PANORAMA/001_0087/Thumbs.db"), 10, ScanUtc);     // appeared after the plan; never deleted
        var r = await h.Run(confirmed, TestContext.Current.CancellationToken);
        Assert.Null(r.Stop);
        var d = Assert.IsType<Deleted>(r.Outcomes[0]);
        Assert.Equal(3, d.Files);
        Assert.False(d.SetFolderRemoved);
        Assert.True(h.OnCard("DCIM/PANORAMA/001_0087/Thumbs.db"));
    }

    [Fact]
    public async Task A_guard_refusal_mid_run_is_an_internal_safety_stop_and_trips_the_wire()
    {
        var (s, ids) = Singles(3);
        var h = Harness(s, expectedViolations: 1);
        var confirmed = s.Confirmed(Before(2026, 7, 26));
        h.Fs.SetAttributes(Full(Rel(s, ids[1])), 0x30);                         // now looks like a directory the plan doesn't name
        var r = await h.Run(confirmed, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { typeof(Deleted), typeof(CleanupFailed), typeof(CleanupNotStarted) }, Kinds(r));
        Assert.Equal(CleanupStop.InternalSafetyStop, r.Stop);
        Assert.True(h.OnCard(Rel(s, ids[1])));
    }
}
