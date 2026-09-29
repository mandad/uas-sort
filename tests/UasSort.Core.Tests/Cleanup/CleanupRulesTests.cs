// tests/UasSort.Core.Tests/Cleanup/CleanupRulesTests.cs
namespace UasSort.Core.Tests.Cleanup;

public class CleanupRulesTests
{
    private const string Tz = "America/Anchorage";
    private const string VideoRel = "DCIM/DJI_001/DJI_20260725232655_0117_D.MP4";
    private const string PhotoRel = "DCIM/DJI_001/DJI_20260725232700_0118_D.DNG";
    private static readonly DateTime T = new(2026, 7, 26, 3, 28, 25, DateTimeKind.Utc);
    private static readonly DateTime Sep27 = new(2026, 9, 27, 21, 7, 2, DateTimeKind.Utc);
    private static readonly DateTime Oct4 = new(2026, 10, 4, 20, 0, 0, DateTimeKind.Utc);

    private static CardEntry Entry(string rel, EntryClass cls, uint attrs = 0x20) => new(rel, 1_000_000, T, T, T, attrs, cls, null);

    private static Newness NewnessOf(string n) => n switch
    {
        "new" => new IsNew(NewReason.NoMatch, null),
        "conflict" => new Conflict(@"C:\Lib\UAS Videos\2026\x\DJI_20260725232655_0117_D.MP4", 5),
        "probably" => new ProbablyImported("videos from this day are already in the library"),
        "dismissed" => new Decided(DecisionKind.Dismissed, Oct4, "DESKTOP-A"),
        "assumed" => new Decided(DecisionKind.AssumedImported, Oct4, "DESKTOP-A"),
        _ => new Imported(Evidence.LibraryNameSize, null, "same name and size"),
    };

    private static FileFacts Facts(AuditCategory cat, string newness = "imported", bool photo = false, bool listed = true,
                                   string ledger = "none", bool truncated = false)
    {
        var rel = photo ? PhotoRel : VideoRel;
        var key = CleanupKeys.Key(rel, 1_000_000);
        LedgerDecision? decision = newness switch
        {
            "dismissed" => new LedgerDecision("d1", key, DecisionKind.Dismissed, Oct4, "DESKTOP-A", null, "not needed"),
            "assumed" => new LedgerDecision("d2", key, DecisionKind.AssumedImported, Oct4, "DESKTOP-A", null, "confirmed by you"),
            _ => null,
        };
        LedgerFile? record = ledger switch
        {
            "unbuffered" or "cached" or "nameSize" => new LedgerFile(key, rel, photo ? DestRoot.Photo : DestRoot.Video, @"C:\Lib\x",
                null, ledger == "unbuffered" ? VerifyKind.Unbuffered : ledger == "cached" ? VerifyKind.Cached : VerifyKind.NameSize,
                Sep27, T, null, Tz, new DateOnly(2026, 7, 25), null, null, "DESKTOP-A", "run-1"),
            _ => null,
        };
        return new FileFacts(Entry(rel, photo ? EntryClass.Photo : EntryClass.Video), photo ? ItemKind.Photo : ItemKind.Video,
                             IsCompanion: false, ChangedSinceScan: false, UnderEnumerationError: false, UnitProbeError: false,
                             UnitTruncated: truncated, cat, NewnessOf(newness), decision, listed, record, Tz);
    }

    [Theory]
    [InlineData(AuditCategory.NameSizeMatch, "imported", false, true, "none", false, CleanupEligibility.Evidence, "", "Listed", "same name and size in the library")]
    [InlineData(AuditCategory.InLedger, "imported", false, true, "unbuffered", false, CleanupEligibility.Evidence, "", "Listed", "in the history, verified")]
    [InlineData(AuditCategory.VerifiedThisRun, "imported", false, true, "unbuffered", false, CleanupEligibility.Evidence, "", "Listed", "copied and verified today")]
    [InlineData(AuditCategory.InLedger, "imported", false, false, "unbuffered", false, CleanupEligibility.NotInLibrary, "NoLongerInLibrary", "", "copied on Sep 27, no longer in your library")]
    [InlineData(AuditCategory.NameSizeMatch, "imported", false, false, "none", false, CleanupEligibility.NotInLibrary, "NoLongerInLibrary", "", "no longer in your library")]
    [InlineData(AuditCategory.InLedger, "imported", true, false, "cached", false, CleanupEligibility.Evidence, "", "HistoryOnly", "copied and verified on Sep 27; Lightroom may have moved it")]
    [InlineData(AuditCategory.NameSizeMatch, "imported", true, false, "nameSize", false, CleanupEligibility.NotInLibrary, "NoLongerInLibrary", "", "matched by name and size on Sep 27, no longer in your library")]
    [InlineData(AuditCategory.NameSizeMatch, "imported", true, false, "none", false, CleanupEligibility.Never, "", "", "not proven either way")]
    [InlineData(AuditCategory.NameSizeMatch, "imported", true, true, "none", false, CleanupEligibility.Evidence, "", "Listed", "same name and size in the library")]
    [InlineData(AuditCategory.AssumedByRule, "probably", true, false, "none", false, CleanupEligibility.NotInLibrary, "ProbablyImported", "", "probably imported, not proven")]
    [InlineData(AuditCategory.Unaccounted, "new", false, false, "none", false, CleanupEligibility.NotInLibrary, "New", "", "new: not in your library")]
    [InlineData(AuditCategory.Unaccounted, "conflict", false, false, "none", false, CleanupEligibility.NotInLibrary, "Conflict", "", "a different file named DJI_20260725232655_0117_D.MP4 is in the library")]
    [InlineData(AuditCategory.Unaccounted, "imported", false, true, "none", false, CleanupEligibility.Never, "", "", "not proven either way")]
    [InlineData(AuditCategory.SkippedByRule, "imported", true, true, "none", false, CleanupEligibility.Never, "", "", "not proven either way")]
    [InlineData(AuditCategory.NameSizeMatch, "imported", false, true, "none", true, CleanupEligibility.NotInLibrary, "Unfinished", "", "unfinished (a same-size copy is in your library; the drone may still repair the card copy)")]
    [InlineData(AuditCategory.Unaccounted, "new", false, false, "none", true, CleanupEligibility.NotInLibrary, "Unfinished", "", "unfinished recording; the drone may still repair it")]
    [InlineData(AuditCategory.ConfirmedByYou, "dismissed", false, false, "none", false, CleanupEligibility.NotInLibrary, "Dismissed", "", "you marked it not needed on Oct 4")]
    [InlineData(AuditCategory.ConfirmedByYou, "assumed", true, false, "none", false, CleanupEligibility.NotInLibrary, "RecordedAsImported", "", "you recorded it as imported on Oct 4; not verified")]
    [InlineData(AuditCategory.ConfirmedByYou, "dismissed", false, false, "none", true, CleanupEligibility.NotInLibrary, "Dismissed", "", "you marked it not needed on Oct 4")]
    public void Eligibility_table(AuditCategory cat, string newness, bool photo, bool listed, string ledger, bool truncated,
                                  CleanupEligibility expected, string nil, string source, string reason)
    {
        var v = CleanupRules.Classify(Facts(cat, newness, photo, listed, ledger, truncated));
        Assert.NotNull(v);
        Assert.Equal(expected, v.Eligibility);
        Assert.Equal(nil.Length == 0 ? (NotInLibraryReason?)null : Enum.Parse<NotInLibraryReason>(nil), v.NotInLibrary);
        Assert.Equal(source.Length == 0 ? (EvidenceSource?)null : Enum.Parse<EvidenceSource>(source), v.Source);
        Assert.Equal(reason, v.Reason);
    }

    [Fact]
    public void Never_rows_win_over_any_evidence()
    {
        var ok = Facts(AuditCategory.VerifiedThisRun);
        Assert.Equal("changed since the scan", CleanupRules.Classify(ok with { ChangedSinceScan = true })!.Reason);
        Assert.Equal("part of the card couldn't be read", CleanupRules.Classify(ok with { UnderEnumerationError = true })!.Reason);
        Assert.Equal("marked read-only on the card", CleanupRules.Classify(ok with { Entry = Entry(VideoRel, EntryClass.Video, 0x21) })!.Reason);
        Assert.Equal("unknown file", CleanupRules.Classify(ok with { Entry = Entry(VideoRel, EntryClass.Unknown) })!.Reason);
        Assert.Equal("its metadata couldn't be read", CleanupRules.Classify(ok with { UnitProbeError = true })!.Reason);
        Assert.Equal("DJI system file", CleanupRules.Classify(ok with { Entry = Entry("MISC/FC9113.db", EntryClass.Skip) })!.Reason);
        foreach (var f in new[] { ok with { ChangedSinceScan = true }, ok with { UnitProbeError = true } })
            Assert.Equal(CleanupEligibility.Never, CleanupRules.Classify(f)!.Eligibility);
    }

    [Fact]
    public void A_companion_inherits_unless_changed_read_only_or_unreadable()
    {
        var lrf = Facts(AuditCategory.Unaccounted, "new") with
        { Entry = Entry("DCIM/DJI_001/DJI_20260725232655_0117_D.LRF", EntryClass.Skip), IsCompanion = true };
        Assert.Null(CleanupRules.Classify(lrf));
        Assert.Equal(CleanupEligibility.Never, CleanupRules.Classify(lrf with { ChangedSinceScan = true })!.Eligibility);
        Assert.Equal(CleanupEligibility.Never, CleanupRules.Classify(lrf with { UnderEnumerationError = true })!.Eligibility);
        Assert.Equal(CleanupEligibility.Never,
            CleanupRules.Classify(lrf with { Entry = Entry("DCIM/DJI_001/DJI_20260725232655_0117_D.LRF", EntryClass.Skip, 0x21) })!.Eligibility);
    }

    [Theory]
    [InlineData("DCIM/DJI_001/DJI_20260725232655_0117_D.LRF", EntryClass.Skip, "not tied to a clip")]
    [InlineData("DCIM/DJI_001/.DJI_20260725232655_0117_D.MP4.trinf", EntryClass.Skip, "not tied to a clip")]
    [InlineData("MISC/FC9113.db", EntryClass.Skip, "DJI system file")]
    [InlineData("System Volume Information/IndexerVolumeGuid", EntryClass.Skip, "system file")]
    [InlineData("DCIM/DJI_A001/x.MP4", EntryClass.Unknown, "unknown file")]
    public void Loose_files_are_never_deleted_with_a_reason(string rel, EntryClass cls, string reason)
        => Assert.Equal(reason, CleanupRules.LooseReason(Entry(rel, cls), false, false));

    [Fact]
    public void Loose_reason_puts_changed_and_unreadable_first()
    {
        Assert.Equal("changed since the scan", CleanupRules.LooseReason(Entry("MISC/FC9113.db", EntryClass.Skip), true, true));
        Assert.Equal("part of the card couldn't be read", CleanupRules.LooseReason(Entry("MISC/FC9113.db", EntryClass.Skip), false, true));
    }
}
