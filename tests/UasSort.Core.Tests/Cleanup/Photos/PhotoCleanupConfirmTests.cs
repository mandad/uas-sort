// tests/UasSort.Core.Tests/Cleanup/Photos/PhotoCleanupConfirmTests.cs
using static UasSort.Testing.PhotoCleanupFixtures;

namespace UasSort.Core.Tests.Cleanup.Photos;

public sealed class PhotoCleanupConfirmTests
{
    private static readonly DateOnly D = new(2026, 6, 1);
    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 10, 4, 20, 0, 0, TimeSpan.Zero));
    private static readonly PhotoRow Pair = Row(Photo("DJI_20260601121000_0002_D.DNG", D, twin: "DJI_20260601121000_0002_D.JPG"));
    private static readonly PhotoRow Pano = Row(Set("001_0087", D, PhotoSetKind.Panorama, "PANO_0001.DNG", "PANO_0002.DNG"), evidence: PhotoEvidence.PanoramaStitch);
    private static readonly PhotoRow Lonely = Row(Photo("DJI_20260601121500_0003_D.DNG", D), verified: false);

    private static PhotoCleanupAck Ack(PhotoCleanupPlan p, bool unverified, params string[] keys)
        => new(p.Fingerprint, [.. keys], MoveToRecycleBin: true, unverified);

    [Fact]
    public void Confirm_NamesTheFilesOfPhotoRowsAndTheFolderOfSetRows_AndKeepsTheRest()
    {
        var plan = Plan(PhotoCleanupMode.Verify, new DateOnly(2026, 6, 30), [Pair, Pano, Lonely]);
        var c = plan.Confirm(Ack(plan, false, Pair.Key, Pano.Key), Clock);
        Assert.Equal(
            new[] { PhotoRoot + @"\001_0087", PhotoRoot + @"\DJI_20260601121000_0002_D.DNG", PhotoRoot + @"\DJI_20260601121000_0002_D.JPG" },
            c.Paths.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal([Pair.Key, Pano.Key], c.Items.Select(r => r.Key));
        Assert.Equal([Lonely.Key], c.Kept.Select(r => r.Key));
        Assert.Equal(Clock.GetUtcNow().UtcDateTime, c.ConfirmedUtc);
        Assert.Equal(PhotoEvidence.PanoramaStitch, c.EvidenceOf(Pano));
    }

    [Fact]
    public void DefaultDelete_DateModeEveryRow_VerifyModeOnlyVerifiedRows()
    {
        Assert.Equal(3, Plan(PhotoCleanupMode.BeforeDate, new DateOnly(2026, 6, 30), [Pair, Pano, Lonely]).DefaultDelete().Count);
        Assert.Equal([Pano.Key, Pair.Key], Plan(PhotoCleanupMode.Verify, new DateOnly(2026, 6, 30), [Pair, Pano, Lonely]).DefaultDelete()
                                                .Order(StringComparer.OrdinalIgnoreCase));      // "001_0087" sorts before "DJI_…"
    }

    [Fact]
    public void Totals_CountPhotosSetsFilesBytesAndUnverified()
    {
        var plan = Plan(PhotoCleanupMode.Verify, new DateOnly(2026, 6, 30), [Pair, Pano, Lonely]);
        var t = plan.Totals(new HashSet<string>([Pair.Key, Pano.Key, Lonely.Key]));
        Assert.Equal(new PhotoTotals(2, 1, 5, Pair.Item.Bytes + Pano.Item.Bytes + Lonely.Item.Bytes, 1), t);
    }

    [Fact]
    public void EvidenceOf_DateModeIsDateOnly_UnverifiedButConfirmedIsUnverifiedConfirmed()
    {
        var dated = Plan(PhotoCleanupMode.BeforeDate, new DateOnly(2026, 6, 30), [Pair]);
        Assert.Equal(PhotoEvidence.DateOnly, dated.Confirm(Ack(dated, false, Pair.Key), Clock).EvidenceOf(Pair));
        var verify = Plan(PhotoCleanupMode.Verify, new DateOnly(2026, 6, 30), [Lonely]);
        Assert.Equal(PhotoEvidence.UnverifiedConfirmed, verify.Confirm(Ack(verify, true, Lonely.Key), Clock).EvidenceOf(Lonely));
    }

    [Fact]
    public void Confirm_RefusesEveryInconsistentAcknowledgement()
    {
        var plan = Plan(PhotoCleanupMode.Verify, new DateOnly(2026, 6, 30), [Pair, Lonely]);
        Assert.Throws<InvalidOperationException>(() => plan.Confirm(new PhotoCleanupAck("0000000000000000", [Pair.Key], true, false), Clock));
        Assert.Throws<InvalidOperationException>(() => plan.Confirm(new PhotoCleanupAck(plan.Fingerprint, [Pair.Key], false, false), Clock));
        Assert.Throws<InvalidOperationException>(() => plan.Confirm(Ack(plan, false), Clock));
        Assert.Throws<InvalidOperationException>(() => plan.Confirm(Ack(plan, false, "Nope.DNG"), Clock));
        Assert.Throws<InvalidOperationException>(() => plan.Confirm(Ack(plan, false, Lonely.Key), Clock));    // unverified without the second ack
        Assert.Throws<InvalidOperationException>(() => plan.Confirm(Ack(plan, true, Pair.Key), Clock));       // second ack without an unverified row
    }

    [Fact]
    public void Targets_RefuseAnythingButPlainNamesDirectlyInTheRoot()
    {
        var escape = Photo(@"..\x.DNG", D);
        Assert.Throws<InvalidOperationException>(() => PhotoCleanupPaths.Targets(PhotoRoot, escape));
        var deep = new PhotoItem("001_0087", PhotoItemKind.Set, PhotoSetKind.Unknown, "001_0087", [Member(@"001_0087\sub\PANO_0001.DNG", D)]);
        Assert.Throws<InvalidOperationException>(() => PhotoCleanupPaths.Targets(PhotoRoot, deep));
        var foreign = new PhotoItem("001_0087", PhotoItemKind.Set, PhotoSetKind.Unknown, "001_0087", [Member(@"002_0001\PANO_0001.DNG", D)]);
        Assert.Throws<InvalidOperationException>(() => PhotoCleanupPaths.Targets(PhotoRoot, foreign));
    }
}
