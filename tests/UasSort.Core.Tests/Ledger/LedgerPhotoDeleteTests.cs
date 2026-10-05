// tests/UasSort.Core.Tests/Ledger/LedgerPhotoDeleteTests.cs
using UasSort.Core.Ledger;
using static UasSort.Core.Tests.Ledger.LedgerLines;

namespace UasSort.Core.Tests.Ledger;

public sealed class LedgerPhotoDeleteTests
{
    private const string Dng = "DJI_20260601121000_0002_D.DNG";

    [Fact]
    public void PhotoDelete_IsIndexedByFileKey_WithItsFields_AndIsNoFileRecord()
    {
        var snap = TestLedger.Snapshot(PhotoDelete("p1", Dng, 25_165_824, evidence: "dateOnly", mode: "beforeDate"));
        var d = Assert.Single(snap.PhotoDeletes).Value;
        Assert.Equal(FileKey.Of(Dng, 25_165_824), d.Key);
        Assert.Equal(@"C:\Lib\UAS Videos\Picture Offload\" + Dng, d.Dest);
        Assert.Equal(("dateOnly", "beforeDate", new DateOnly(2026, 6, 30)), (d.Evidence, d.Mode, d.Cutoff));
        Assert.Equal(DateTimeKind.Utc, d.AtUtc.Kind);
        Assert.Empty(snap.ParseIssues);
        Assert.Empty(snap.Files);
    }

    [Fact]
    public void PhotoDelete_Revoked_IsGone_AndTheLatestWins()
    {
        Assert.Empty(TestLedger.Snapshot(PhotoDelete("p1", Dng, 1), Revoke("r1", "p1")).PhotoDeletes);
        var twice = TestLedger.Snapshot(PhotoDelete("p1", Dng, 1, at: Utc(2026, 10, 4)), PhotoDelete("p2", Dng, 1, at: Utc(2026, 10, 5)));
        Assert.Equal("p2", Assert.Single(twice.PhotoDeletes).Value.Id);
    }

    [Theory]
    [InlineData("bogus", "verify", "bad evidence 'bogus'")]
    [InlineData("lightroom", "sometimes", "bad mode 'sometimes'")]
    public void PhotoDelete_BadValues_AreParseIssues(string evidence, string mode, string issue)
    {
        var snap = TestLedger.Snapshot(PhotoDelete("p1", Dng, 1, evidence: evidence, mode: mode));
        Assert.Empty(snap.PhotoDeletes);
        Assert.Equal(issue, Assert.Single(snap.ParseIssues).Reason);
    }

    [Fact]
    public void PhotoDelete_SetMembers_CarryTheSetName()
    {
        var snap = TestLedger.Snapshot(PhotoDelete("p1", "PANO_0001.DNG", 10, set: "001_0087"), PhotoDelete("p2", "PANO_0002.DNG", 11, set: "001_0087"));
        Assert.Equal(2, snap.PhotoDeletes.Count);
        Assert.All(snap.PhotoDeletes.Values, d => Assert.Equal("001_0087", d.Set));
    }

    [Theory]
    [InlineData(PhotoEvidence.Lightroom, "lightroom")]
    [InlineData(PhotoEvidence.HyperlapseResult, "hyperlapseResult")]
    [InlineData(PhotoEvidence.PanoramaStitch, "panoramaStitch")]
    [InlineData(PhotoEvidence.DateOnly, "dateOnly")]
    [InlineData(PhotoEvidence.UnverifiedConfirmed, "unverifiedConfirmed")]
    public void PhotoDeleteRecords_EvidenceText(PhotoEvidence e, string text)
    {
        Assert.Equal(text, PhotoDeleteRecords.Evidence(e));
        Assert.True(PhotoDeleteRecords.IsEvidence(text));
    }
}
