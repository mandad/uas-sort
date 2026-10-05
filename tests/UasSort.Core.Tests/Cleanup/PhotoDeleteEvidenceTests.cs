// tests/UasSort.Core.Tests/Cleanup/PhotoDeleteEvidenceTests.cs
using UasSort.Testing.Offload;
using static UasSort.Testing.Offload.OffloadPlanBuilder;

namespace UasSort.Core.Tests.Cleanup;

public sealed class PhotoDeleteEvidenceTests
{
    private const string P = "DJI_20260927140000_0002_D.DNG";
    private const string V = "DJI_20260927140000_0001_D.MP4";
    private static readonly CardDiffResult NoChange = new([], [], [], 0);

    internal static LedgerPhotoDelete Removed(string name, long size, string evidence = "lightroom", string? set = null)
        => new("p1", FileKey.Of(name, size), FakeLayout.PhotoRoot + @"\" + name, new DateTime(2026, 10, 4, 20, 0, 0, DateTimeKind.Utc), T0,
               set, evidence, "verify", new DateOnly(2026, 9, 30), "DESKTOP-A", "run-p");

    private static LedgerSnapshot With(LedgerSnapshot s, params LedgerPhotoDelete[] removed)
        => s with { PhotoDeletes = removed.ToImmutableDictionary(r => r.Key) };

    [Fact]
    public void Audit_APhotoRemovedFromPictureOffload_IsInLedger_WithTheRemovalDetail()
    {
        var b = new OffloadPlanBuilder();
        var photo = b.Photo(P, 200, T0, new IsNew(NewReason.NoMatch, null));
        var plan = b.Build();
        var units = AuditCategorizer.Categorize(plan.Base.Scan.Inventory, plan, null, With(plan.Base.Scan.Ledger, Removed(P, 200)), NoChange);
        var unit = units.Units.Single(u => u.Unit == photo);
        Assert.Equal(AuditCategory.InLedger, unit.Worst);
        Assert.Equal("removed from Picture Offload after Lightroom import", unit.Lines[0].Detail);
    }

    [Fact]
    public void Audit_AVideoKeyInPhotoDeletes_IsIgnored()
    {
        var b = new OffloadPlanBuilder();
        var video = b.Video(V, 100, T0, new IsNew(NewReason.NoMatch, null));
        b.Group(new NewFolder(@"2026\2026-09\2026-09-27 Zachar Bay"), Zachar, video);
        var plan = b.Build();
        var units = AuditCategorizer.Categorize(plan.Base.Scan.Inventory, plan, null, With(plan.Base.Scan.Ledger, Removed(V, 100)), NoChange);
        Assert.Equal(AuditCategory.Unaccounted, units.Units.Single(u => u.Unit == video).Worst);
    }

    [Theory]
    [InlineData("dateOnly", "removed from Picture Offload by date")]
    [InlineData("lightroom", "removed from Picture Offload after Lightroom import")]
    [InlineData("panoramaStitch", "removed from Picture Offload after Lightroom import")]
    [InlineData("hyperlapseResult", "removed from Picture Offload; its hyperlapse video is in the library")]
    [InlineData("unverifiedConfirmed", "removed from Picture Offload (not confirmed in Lightroom)")]
    public void Rules_APhotoWithAPhotoDelete_IsHistoryOnlyEvidence(string evidence, string reason)
    {
        var e = new CardEntry("DCIM/DJI_001/" + P, 200, T0, T0, T0, 0x20, EntryClass.Photo, null);
        var facts = new FileFacts(e, ItemKind.Photo, IsCompanion: false, ChangedSinceScan: false, UnderEnumerationError: false,
                                  UnitProbeError: false, UnitTruncated: false, AuditCategory.InLedger, new IsNew(NewReason.NoMatch, null),
                                  null, Listed: false, null, "America/Anchorage", Removed(P, 200, evidence));
        var v = CleanupRules.Classify(facts)!;
        Assert.Equal((CleanupEligibility.Evidence, (EvidenceSource?)EvidenceSource.HistoryOnly, reason), (v.Eligibility, v.Source, v.Reason));
    }

    [Fact]
    public void Rules_AVideoNeverUsesAPhotoDelete()
    {
        var e = new CardEntry("DCIM/DJI_001/" + V, 100, T0, T0, T0, 0x20, EntryClass.Video, null);
        var facts = new FileFacts(e, ItemKind.Video, false, false, false, false, false, AuditCategory.InLedger,
                                  new IsNew(NewReason.NoMatch, null), null, false, null, "America/Anchorage", Removed(V, 100));
        Assert.Equal(CleanupEligibility.NotInLibrary, CleanupRules.Classify(facts)!.Eligibility);
    }

    [Fact]
    public void ExecutorRecheck_APhotoDeleteKeepsHistoryOnlyPhotoEvidence()
    {
        var fresh = FreshEvidence.From(new LibraryListings(
            new RootListing(FakeLayout.VideoRoot, DestRoot.Video, false, true, new ListingResult([], [])),
            new RootListing(FakeLayout.PhotoRoot, DestRoot.Photo, false, true, new ListingResult([], [])), []));
        var proof = new FileProof("DCIM/DJI_001/" + P, FileKey.Of(P, 200), AuditCategory.InLedger, null, false, null);
        var empty = LedgerSnapshots.Empty(LedgerSnapshots.Detached(@"C:\x\.uas-sort", []));
        Assert.True(CleanupExecutor.ProofHolds(ItemKind.Photo, proof, fresh, With(empty, Removed(P, 200))));
        Assert.False(CleanupExecutor.ProofHolds(ItemKind.Video, proof, fresh, With(empty, Removed(P, 200))));
        Assert.False(CleanupExecutor.ProofHolds(ItemKind.Photo, proof, fresh, empty));
    }
}
