// tests/UasSort.Core.Tests/Cleanup/CleanupCandidatesTests.cs
using static UasSort.Core.Tests.Cleanup.CleanupScenario;
#pragma warning disable CA1861 // expected delete orders read best inline next to their scenario; each test runs once

namespace UasSort.Core.Tests.Cleanup;

public class CleanupCandidatesTests
{
    private static readonly DateTime Jul3 = Utc(2026, 7, 3, 20, 0);    // Jul 3 12:00 AKDT

    private static string[] Names(CleanupCandidate c) => [.. c.Files.Select(f => CleanupPaths.Name(f.RelPath))];

    [Fact]
    public void Video_deletes_its_companions_first_and_the_mp4_last()
    {
        var s = new CleanupScenario();
        var id = s.AddVideo(Jul3, companions: Comp.Lrf | Comp.Srt | Comp.Trinf | Comp.Avc1);
        var c = s.Candidate(id);
        const string n = "DJI_20260703120000_0101_D";
        Assert.Equal(new[] { $".{n}.MP4.avc1", $".{n}.MP4.trinf", $"{n}.LRF", $"{n}.SRT", $"{n}.MP4" }, Names(c));
        Assert.Equal(4, c.NeverCopied.Length);
        Assert.Equal(1_020_526_592, c.AllocatedBytes);               // 7630 + 153 + 1 + 1 + 1 clusters of 128 KiB
        Assert.Equal(CleanupEligibility.Evidence, c.Eligibility);
        Assert.Equal(EvidenceSource.Listed, c.Source);
        Assert.Equal("same name and size in the library", c.Reason);
        Assert.Equal(ItemKind.Video, c.Kind);
        Assert.Single(c.Proofs);
    }

    [Fact]
    public void Cover_jpg_is_a_companion_only_when_its_skip_rule_is_on()
    {
        var s = new CleanupScenario();
        var on = s.AddVideo(Jul3, companions: Comp.CoverSkip);
        var off = s.AddVideo(Jul3.AddHours(1), companions: Comp.CoverUnknown);
        var cands = s.Candidates();
        Assert.Contains("DJI_20260703120000_0101_D.JPG", Names(cands.Single(c => c.Unit == on)));
        var offC = cands.Single(c => c.Unit == off);
        Assert.Equal(new[] { "DJI_20260703130000_0102_D.MP4" }, Names(offC));
        Assert.Equal(CleanupEligibility.Evidence, offC.Eligibility);           // the MP4 still goes
        var loose = CleanupPlanner.LooseFiles(s.Inputs(), cands);
        var jpg = Assert.Single(loose, k => k.CardRelPaths[0].EndsWith("0102_D.JPG", StringComparison.Ordinal));
        Assert.Equal("unknown file", jpg.Reason);
        Assert.Null(jpg.Unit);
    }

    [Fact]
    public void Photo_pair_deletes_the_twin_first_and_twin_off_counts_as_never_copied()
    {
        var s = new CleanupScenario();
        var id = s.AddPhoto(Jul3, twin: true);
        var c = s.Candidate(id);
        Assert.Equal(new[] { "DJI_20260703120000_0101_D.JPG", "DJI_20260703120000_0101_D.DNG" }, Names(c));
        Assert.Empty(c.NeverCopied);
        Assert.Equal(2, c.Proofs.Length);

        var off = new CleanupScenario { CopyJpgTwin = false };
        var id2 = off.AddPhoto(Jul3, twin: true, twinCategory: AuditCategory.SkippedByRule, twinListed: false);
        var c2 = off.Candidate(id2);
        Assert.Equal(CleanupEligibility.Evidence, c2.Eligibility);
        Assert.Equal(new[] { "DJI_20260703120000_0101_D.JPG" }, c2.NeverCopied.Select(f => CleanupPaths.Name(f.RelPath)));
        Assert.Single(c2.Proofs);
        Assert.Equal(new[] { "DJI_20260703120000_0101_D.JPG", "DJI_20260703120000_0101_D.DNG" }, Names(c2));
    }

    [Fact]
    public void Dng_in_ledger_with_an_assumed_twin_makes_the_pair_not_in_library()
    {
        var s = new CleanupScenario();
        var id = s.AddPhoto(Jul3, AuditCategory.InLedger, listed: true, twin: true, twinCategory: AuditCategory.AssumedByRule,
                            twinListed: false, ledger: null);
        var c = s.Candidate(id);
        Assert.Equal(CleanupEligibility.NotInLibrary, c.Eligibility);
        Assert.Equal(NotInLibraryReason.ProbablyImported, c.NotInLibrary);
        Assert.Equal(AuditCategory.AssumedByRule, c.Evidence);
        Assert.Null(c.Source);
    }

    [Fact]
    public void Set_members_go_in_ordinal_name_order_and_name_their_folder()
    {
        var s = new CleanupScenario();
        var id = s.AddSet("001_0087", Jul3, members: 3);
        var c = s.Candidate(id);
        Assert.Equal(new[] { "PANO_0001.DNG", "PANO_0002.DNG", "PANO_0003.DNG" }, Names(c));
        Assert.Equal("DCIM/PANORAMA/001_0087", c.SetFolder);
        Assert.Equal(ItemKind.Set, c.Kind);
        Assert.Equal(3, c.Proofs.Length);
    }

    [Fact]
    public void A_companion_never_makes_a_unit_eligible_and_a_changed_one_keeps_the_unit()
    {
        var s = new CleanupScenario();
        var nil = s.AddVideo(Jul3, AuditCategory.Unaccounted, listed: false, companions: Comp.Lrf);
        var changed = s.AddVideo(Jul3.AddHours(1), companions: Comp.Lrf);
        s.MarkChanged("DCIM/DJI_001/DJI_20260703130000_0102_D.LRF");
        s.AddLoose("DCIM/DJI_001/DJI_20260101000000_0999_D.LRF", EntryClass.Skip);   // orphan
        var cands = s.Candidates();
        var n = cands.Single(c => c.Unit == nil);
        Assert.Equal(CleanupEligibility.NotInLibrary, n.Eligibility);
        Assert.Contains("DJI_20260703120000_0101_D.LRF", Names(n));
        var ch = cands.Single(c => c.Unit == changed);
        Assert.Equal(CleanupEligibility.Never, ch.Eligibility);
        Assert.Equal("changed since the scan", ch.Reason);
        Assert.Equal("DJI_20260703130000_0102_D.MP4", Names(ch)[^1]);
        var orphan = Assert.Single(CleanupPlanner.LooseFiles(s.Inputs(), cands));
        Assert.Equal("not tied to a clip", orphan.Reason);
        Assert.DoesNotContain(cands, c => c.Files.Any(f => f.RelPath.EndsWith("0999_D.LRF", StringComparison.Ordinal)));
    }

    [Fact]
    public void Never_units_probe_error_read_only_enumeration_error_and_changed_on_card()
    {
        var s = new CleanupScenario();
        var probe = s.AddVideo(Jul3, probeError: "bad moov");
        var ro = s.AddVideo(Jul3.AddHours(1), attributes: 0x21);
        var changedRun = s.AddVideo(Jul3.AddHours(2));
        s.OffloadChanged(changedRun);
        var cands = s.Candidates();
        Assert.Equal("its metadata couldn't be read", cands.Single(c => c.Unit == probe).Reason);
        Assert.Equal("marked read-only on the card", cands.Single(c => c.Unit == ro).Reason);
        Assert.Equal("changed since the scan", cands.Single(c => c.Unit == changedRun).Reason);
        Assert.All(cands, c => Assert.Equal(CleanupEligibility.Never, c.Eligibility));

        var e = new CleanupScenario();
        var under = e.AddVideo(Jul3);
        e.AddEnumerationError("DCIM/DJI_001");
        Assert.Equal("part of the card couldn't be read", e.Candidate(under).Reason);
    }

    [Fact]
    public void Ticked_for_offload_is_set_only_for_not_in_library_units_that_were_not_copied()
    {
        var s = new CleanupScenario();
        var ticked = s.AddVideo(Jul3, AuditCategory.Unaccounted, listed: false);
        var failed = s.AddVideo(Jul3.AddHours(1), AuditCategory.Unaccounted, listed: false);
        var copied = s.AddVideo(Jul3.AddHours(2), AuditCategory.VerifiedThisRun, listed: true);
        var unticked = s.AddVideo(Jul3.AddHours(3), AuditCategory.Unaccounted, listed: false);
        foreach (var id in new[] { ticked, failed, copied }) s.Included.Add(id);
        s.OffloadFailed(failed);
        s.OffloadVerified(copied);
        var cands = s.Candidates();
        Assert.True(cands.Single(c => c.Unit == ticked).TickedForOffload);
        Assert.True(cands.Single(c => c.Unit == failed).TickedForOffload);
        Assert.Equal(NotInLibraryReason.New, cands.Single(c => c.Unit == failed).NotInLibrary);
        Assert.False(cands.Single(c => c.Unit == copied).TickedForOffload);
        Assert.False(cands.Single(c => c.Unit == unticked).TickedForOffload);
    }

    [Fact]
    public void One_byte_file_counts_a_whole_cluster()
    {
        var s = new CleanupScenario();
        var id = s.AddVideo(Jul3, size: 1);
        Assert.Equal(131_072, s.Candidate(id).AllocatedBytes);
    }

    [Fact]
    public void Proofs_name_the_listed_folder_or_the_verified_history()
    {
        var s = new CleanupScenario();
        var video = s.AddVideo(Jul3, AuditCategory.InLedger, ledger: VerifyKind.Unbuffered);
        var moved = s.AddPhoto(Jul3.AddHours(1), AuditCategory.InLedger, listed: false, ledger: VerifyKind.Cached);
        var cands = s.Candidates();
        var vp = Assert.Single(cands.Single(c => c.Unit == video).Proofs);
        Assert.Equal(VideoRoot + "\\" + CouncilDir, vp.ListedFolder);
        Assert.True(vp.LedgerVerified);
        var m = cands.Single(c => c.Unit == moved);
        Assert.Equal(EvidenceSource.HistoryOnly, m.Source);
        Assert.Null(m.Proofs[0].ListedFolder);
        Assert.True(m.Proofs[0].LedgerVerified);
        Assert.Equal("copied and verified on Sep 27; Lightroom may have moved it", m.Reason);
    }

    [Fact]
    public void Candidate_carries_time_length_place_and_session_oldest_first()
    {
        var anvil = new GeoPoint(64.5627, -165.3696);
        var summit = new GeoPoint(anvil.Lat + Distance.FromMiles(0.2).Meters / GeoMath.MetersPerDegree, anvil.Lon);   // 0.2 mi north
        var session = new SessionKey("1581F6Z", Utc(2026, 7, 26, 7, 40));
        var s = new CleanupScenario
        {
            Places = new FakePlaceIndex(new PlaceHit("Anvil Mountain", summit, PlaceClass.Feature, "MT", 0, Ak, Distance.FromMiles(0.2))),
        };
        var later = s.AddVideo(Utc(2026, 7, 26, 8, 10), session: session);
        var earlier = s.AddVideo(Utc(2026, 7, 26, 7, 50), duration: TimeSpan.FromSeconds(222), gps: anvil, session: session);
        var cands = s.Candidates();
        Assert.Equal(new[] { earlier, later }, cands.Select(c => c.Unit));
        var c = cands[0];
        Assert.Equal(new DateOnly(2026, 7, 25), c.LocalDate);
        Assert.Equal(new DateTime(2026, 7, 25, 23, 50, 0), c.LocalTime);
        Assert.Equal(Ak, c.TzId);
        Assert.Equal(TimeSpan.FromSeconds(222), c.Duration);
        Assert.Equal(anvil, c.Location);
        Assert.Equal("near Anvil Mountain · 0.2 mi", c.PlaceLabel);
        Assert.Equal(session, c.Session);
        Assert.Null(cands[1].PlaceLabel);
    }
}
