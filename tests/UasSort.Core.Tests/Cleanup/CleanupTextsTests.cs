// tests/UasSort.Core.Tests/Cleanup/CleanupTextsTests.cs
using static UasSort.Core.Tests.Cleanup.CleanupScenario;

namespace UasSort.Core.Tests.Cleanup;

public class CleanupTextsTests
{
    private const long Unit5 = 5_000_003_584;

    [Fact]
    public void Before_date_line_names_the_kept_files_and_the_reviewed_ones()
    {
        var s = new CleanupScenario();
        s.AddVideo(Utc(2026, 7, 20, 20, 0));
        s.AddVideo(Utc(2026, 7, 21, 20, 0));
        s.AddVideo(Utc(2026, 7, 22, 20, 0), probeError: "bad moov", companions: Comp.Lrf | Comp.Srt);
        s.AddVideo(Utc(2026, 7, 23, 20, 0), AuditCategory.Unaccounted, listed: false);
        Assert.Equal("Deletes 2 files captured before Jul 26, 2026 (local time at each site) that are in your library; "
                   + "Jul 26 and later are kept. 4 older files are kept (see 'Kept').",
                     CleanupTexts.CutoffLine(s.Build(Before(2026, 7, 26))));
        Assert.Equal("Deletes 2 files captured before Jul 26, 2026 (local time at each site) that are in your library, plus 1 you reviewed; "
                   + "Jul 26 and later are kept. 3 older files are kept (see 'Kept').",
                     CleanupTexts.CutoffLine(s.Build(Before(2026, 7, 26, include: true))));
    }

    [Fact]
    public void Before_date_says_everything_only_when_nothing_older_is_kept_and_names_a_continuing_flight()
    {
        var s = new CleanupScenario();
        var session = new SessionKey("1581F6Z", Utc(2026, 7, 26, 7, 40));
        s.AddVideo(Utc(2026, 7, 26, 7, 50), session: session);
        s.AddVideo(Utc(2026, 7, 26, 8, 10), session: session);
        Assert.Equal("Deletes everything captured before Jul 26, 2026 (local time at each site); Jul 26 and later are kept. "
                   + "A flight continues past the cutoff (Jul 26 00:10 AKDT); its later clips are kept.",
                     CleanupTexts.CutoffLine(s.Build(Before(2026, 7, 26))));
    }

    private static CleanupScenario AugustCard(string firstTz)
    {
        var s = new CleanupScenario { Space = new CardSpace(1_000_000_000, 256_060_514_304, 131_072) };
        s.AddVideo(Utc(2026, 7, 3, 20, 0), size: Unit5, companions: Comp.Lrf | Comp.Srt, tz: firstTz);
        s.AddVideo(Utc(2026, 8, 30, 22, 22), size: Unit5, companions: Comp.Lrf | Comp.Srt);
        s.AddVideo(Utc(2026, 8, 30, 23, 0), size: Unit5, companions: Comp.Lrf | Comp.Srt);
        s.AddVideo(Utc(2026, 8, 31, 1, 0), size: Unit5, companions: Comp.Lrf | Comp.Srt);
        return s;
    }

    [Fact]
    public void Free_space_line_states_range_cutoff_and_the_cutoff_day_count()
    {
        Assert.Equal("Deletes 6 files · 10.0 GB · captured Jul 3 – Aug 30 14:22 AKDT → cutoff: Aug 30 14:22 (3 of 9 files from Aug 30)",
                     CleanupTexts.CutoffLine(AugustCard(Ak).Build(HaveFree(10_000_000_000))));
        Assert.Equal("Deletes 6 files · 10.0 GB · captured Jul 3 HST – Aug 30 14:22 AKDT → cutoff: Aug 30 14:22 (3 of 9 files from Aug 30)",
                     CleanupTexts.CutoffLine(AugustCard("Pacific/Honolulu").Build(HaveFree(10_000_000_000))));
    }

    [Fact]
    public void Target_already_met_says_nothing_to_delete()
    {
        var s = new CleanupScenario { Space = new CardSpace(73_800_000_000, 256_060_514_304, 131_072) };
        s.AddVideo(Utc(2026, 7, 3, 20, 0));
        var plan = s.Build(HaveFree(20_000_000_000));
        Assert.Equal("E: already has 73.8 GB free; nothing to delete", CleanupTexts.NothingToDelete(plan));
        Assert.Equal("E: already has 73.8 GB free; nothing to delete", CleanupTexts.CutoffLine(plan));
        Assert.Null(CleanupTexts.ShortfallLine(plan));
    }

    [Fact]
    public void Shortfall_names_not_in_library_bytes_until_the_switch_is_on()
    {
        var s = new CleanupScenario();
        s.AddVideo(Utc(2026, 7, 3, 20, 0), size: Unit5);
        s.AddVideo(Utc(2026, 7, 4, 20, 0), AuditCategory.Unaccounted, listed: false, size: Unit5);
        s.AddVideo(Utc(2026, 7, 5, 20, 0), size: Unit5, probeError: "bad moov");
        s.AddVideo(Utc(2026, 7, 6, 20, 0), size: Unit5);
        Assert.Equal("Only 10.0 GB can be freed; 5.0 GB is held by files that are not in your library",
                     CleanupTexts.ShortfallLine(s.Build(HaveFree(200_000_000_000))));
        Assert.Equal("Only 15.0 GB can be freed; 5.0 GB is held by files uas-sort never deletes (unknown files, changed files, DJI system files)",
                     CleanupTexts.ShortfallLine(s.Build(HaveFree(200_000_000_000, include: true))));
        Assert.Null(CleanupTexts.NothingToDelete(s.Build(HaveFree(200_000_000_000))));
    }

    [Fact]
    public void Summary_splits_evidence_and_names_files_uas_sort_never_copies()
    {
        var s = new CleanupScenario { CopyJpgTwin = false };
        s.AddVideo(Utc(2026, 7, 3, 20, 0), companions: Comp.Lrf | Comp.Srt);
        s.AddPhoto(Utc(2026, 7, 3, 21, 0), AuditCategory.InLedger, listed: false, twin: true,
                   twinCategory: AuditCategory.SkippedByRule, twinListed: false, ledger: VerifyKind.Cached);
        s.AddPhoto(Utc(2026, 7, 3, 22, 0), AuditCategory.InLedger, listed: false, twin: true,
                   twinCategory: AuditCategory.SkippedByRule, twinListed: false, ledger: VerifyKind.Unbuffered);
        var plan = s.Build(Before(2026, 7, 26));
        Assert.Equal("1 in the library listing · 2 photos found only in the history (Lightroom may have moved them)",
                     CleanupTexts.EvidenceSplit(plan));
        Assert.Equal("Also deletes 4 files uas-sort never copies: 2 JPG twins (copying is off in Settings), 1 LRF proxy, 1 SRT caption",
                     CleanupTexts.NeverCopiesLine(plan));
        Assert.Equal("E:", CleanupTexts.VolumeName(@"E:\"));
    }
}
