// tests/UasSort.Core.Tests/Ledger/LedgerSnapshotTests.cs
using System.Globalization;
using UasSort.Core.Ledger;
using UasSort.Core;
using static UasSort.Core.Tests.Ledger.LedgerLines;

namespace UasSort.Core.Tests.Ledger;

public sealed class LedgerSnapshotTests
{
    private const string ZrelDir = @"C:\Lib\UAS Videos\2026\2026-09\2026-09-27 Zachar Bay";
    private const string N128 = "DJI_20260927140627_0128_D.MP4";

    [Fact]
    public void LedgerSnapshot_Files_OnePerKeyWithTypedFields()
    {
        var snap = TestLedger.Snapshot(
            FileRec("f1", N128, 89_612_345, ZrelDir + @"\" + N128, verify: "nameSize", at: Utc(2026, 9, 27, 21, 0), xxh128: null),
            FileRec("f2", N128, 89_612_345, ZrelDir + @"\" + N128, at: Utc(2026, 9, 27, 21, 7, 2), lat: 57.5504421, lon: -153.738973,
                    tz: "America/Anchorage", captureUtc: Utc(2026, 9, 27, 18, 6, 27), sessionUtc: Utc(2026, 9, 27, 17, 59, 28),
                    serial: "1581F0001"));
        LedgerFile f = Assert.Single(snap.Files).Value;
        Assert.Equal(FileKey.Of(N128, 89_612_345), f.Key);
        Assert.Equal(VerifyKind.Unbuffered, f.Verify);
        Assert.Equal(DestRoot.Video, f.Root);
        Assert.Equal(UInt128.Parse("5e0c0000000000000000000000000001", NumberStyles.HexNumber, CultureInfo.InvariantCulture), f.Xxh128);
        Assert.Equal(new GeoPoint(57.5504421, -153.738973), f.Point);
        Assert.Equal("America/Anchorage", f.TzId);
        Assert.Equal(new DateOnly(2026, 9, 27), f.LocalDate);
        Assert.Equal(Utc(2026, 9, 27, 18, 6, 27), f.CaptureUtc);
        Assert.True(f.Session is { DroneSerial: "1581F0001" } s && s.SessionUtc == Utc(2026, 9, 27, 17, 59, 28));
        Assert.Equal(("DESKTOP-A", "8f1c0001"), (f.Machine, f.Run));
        Assert.Empty(snap.ParseIssues);
    }

    [Fact]
    public void LedgerSnapshot_LaterNameSizeRecord_DoesNotDowngradeVerified()
    {
        // decision 5: strongest verification wins (Unbuffered > Cached > NameSize); a later weaker record never downgrades
        string dest = ZrelDir + @"\" + N128;
        var snap = TestLedger.Snapshot(
            FileRec("f1", N128, 89_612_345, dest, verify: "cached", at: Utc(2026, 9, 27, 21, 0)),
            FileRec("f2", N128, 89_612_345, dest, verify: "unbuffered", at: Utc(2026, 9, 27, 21, 7, 2)),
            FileRec("f3", N128, 89_612_345, dest, verify: "nameSize", at: Utc(2026, 10, 4, 20, 11), xxh128: null, machine: "LAPTOP-B"),
            FileRec("f4", N128, 89_612_345, dest, verify: "cached", at: Utc(2026, 10, 5, 9, 0), machine: "LAPTOP-B"));
        LedgerFile kept = Assert.Single(snap.Files).Value;
        Assert.Equal(VerifyKind.Unbuffered, kept.Verify);
        Assert.Equal(Utc(2026, 9, 27, 21, 7, 2), kept.AtUtc);
        Assert.Equal("DESKTOP-A", kept.Machine);
        Assert.NotNull(kept.Xxh128);

        // among equal strength the latest record wins
        var twice = TestLedger.Snapshot(
            FileRec("g1", N128, 89_612_345, dest, verify: "nameSize", at: Utc(2026, 9, 27, 21, 0), xxh128: null),
            FileRec("g2", N128, 89_612_345, dest, verify: "nameSize", at: Utc(2026, 10, 4, 20, 11), xxh128: null, machine: "LAPTOP-B"));
        LedgerFile latest = Assert.Single(twice.Files).Value;
        Assert.Equal((VerifyKind.NameSize, "LAPTOP-B"), (latest.Verify, latest.Machine));
        Assert.Empty(snap.ParseIssues);
    }

    [Fact]
    public void LedgerSnapshot_Decisions_AreTakenAfterRevokesFromAnyMachine()
    {
        var snap = TestLedger.Snapshot(
            Decision("a91", "DJI_20260725233000_0120_D.DNG", 27_411_200),
            Decision("b02", "DJI_20260726001000_0121_D.DNG", 27_000_000, kind: "dismissed"),
            Revoke("r1", "a91", machine: "LAPTOP-B"));
        LedgerDecision d = Assert.Single(snap.Decisions).Value;
        Assert.Equal("b02", d.Id);
        Assert.Equal(DecisionKind.Dismissed, d.Kind);
        Assert.Equal(FileKey.Of("DJI_20260726001000_0121_D.DNG", 27_000_000), d.Key);
    }

    [Fact]
    public void LedgerSnapshot_Seen_LatestPerKey()
    {
        var snap = TestLedger.Snapshot(
            Seen("s1", "X.DNG", 10, Utc(2026, 10, 2)),
            Seen("s2", "X.DNG", 10, Utc(2026, 10, 4, 20, 11)),
            Seen("s3", "Y.DNG", 11, Utc(2026, 10, 4)));
        Assert.Equal(2, snap.Seen.Count);
        Assert.Equal(Utc(2026, 10, 4, 20, 11), snap.Seen[FileKey.Of("x.dng", 10)]);
    }

    [Fact]
    public void LedgerSnapshot_Sets_GroupedByNameAndFirstFrame()
    {
        DateTime first = Utc(2026, 5, 25, 13, 30, 28), other = Utc(2026, 6, 1, 10, 0, 0);
        const string dir = @"C:\Lib\UAS Videos\Picture Offload\001_0087\";
        var snap = TestLedger.Snapshot(
            FileRec("p2", "PANO_0002.DNG", 12_882_432, dir + "PANO_0002.DNG", root: "photo", kind: "setMember", set: "001_0087", captureUtc: first),
            FileRec("p1", "PANO_0001.DNG", 13_751_808, dir + "PANO_0001.DNG", root: "photo", kind: "setMember", set: "001_0087", captureUtc: first),
            FileRec("q1", "PANO_0001.DNG", 13_000_000, @"C:\Lib\UAS Videos\Picture Offload\001_0087 2026-06-01\PANO_0001.DNG",
                    root: "photo", kind: "setMember", set: "001_0087", captureUtc: other));
        ImmutableArray<LedgerSet> sets = snap.SetsByName["001_0087"];
        Assert.Equal(2, sets.Length);
        Assert.Equal(first, sets[0].FirstFrameCaptureUtc);
        Assert.Equal(new[] { ("PANO_0001.DNG", 13_751_808L), ("PANO_0002.DNG", 12_882_432L) },
                     sets[0].Members.Select(m => (m.Member, m.Size)));
        Assert.Equal(other, sets[1].FirstFrameCaptureUtc);
        Assert.Equal(3, snap.Files.Count);
    }

    [Fact]
    public void LedgerSnapshot_Folders_CaseInsensitiveAndWidened()
    {
        var snap = TestLedger.Snapshot(
            FolderRec("fo1", ZrelDir, "Zachar Bay", new DateOnly(2026, 9, 27), new DateOnly(2026, 9, 27), "America/Anchorage", 57.5415, -153.7409),
            FolderRec("fo2", ZrelDir.ToUpperInvariant() + @"\", "Zachar Bay", new DateOnly(2026, 9, 28), new DateOnly(2026, 9, 28),
                      "America/Anchorage", source: "appended"));
        Assert.Single(snap.Folders);
        LedgerFolder f = snap.Folders[@"c:\lib\uas videos\2026\2026-09\2026-09-27 zachar bay"];
        Assert.Equal((new DateOnly(2026, 9, 27), new DateOnly(2026, 9, 28)), (f.Start, f.End));
        Assert.Equal(FolderSource.Appended, f.Source);
        Assert.Equal(new GeoPoint(57.5415, -153.7409), f.Centroid);
        Assert.Equal("America/Anchorage", f.TzId);
    }

    [Fact]
    public void LedgerSnapshot_RunsAndCardDeletes()
    {
        var snap = TestLedger.Snapshot(
            Run("u1", "8f1c0001", Utc(2026, 9, 27, 21, 0), Utc(2026, 9, 27, 21, 30), @"C:\Lib\UAS Videos", @"C:\Lib\UAS Videos\Picture Offload"),
            CardDelete("c1", "DJI_20260725232655_0117_D.MP4", 1_234_567_890));
        LedgerRun run = Assert.Single(snap.Runs);
        Assert.Equal(VerdictLevel.SafeWithAssumptions, run.Verdict);
        Assert.Equal(0x1A2B3C4Du, run.Card.VolumeSerial);
        Assert.Equal("exFAT", run.Card.FileSystem);
        Assert.Equal(17, run.Counts[AuditCategory.VerifiedThisRun]);
        Assert.Equal(@"C:\Lib\UAS Videos", run.VideoRoot);
        Assert.Equal(@"C:\Lib\UAS Videos\Picture Offload", run.PhotoRoot);
        Assert.Equal("FC9113", run.Model);
        LedgerCardDelete cd = Assert.Single(snap.CardDeletes);
        Assert.Equal(FileKey.Of("DJI_20260725232655_0117_D.MP4", 1_234_567_890), cd.Key);
        Assert.Equal("InLedger", cd.Evidence);
        Assert.Empty(snap.Files);            // a cardDelete is read by no rule
    }

    [Fact]
    public void LedgerSnapshot_BadFieldValues_AreParseIssuesAndDropped()
    {
        var snap = TestLedger.Snapshot(
            FileRec("f1", N128, 1, ZrelDir + @"\" + N128, verify: "maybe"),
            Run("u1", "r1", Utc(2026, 9, 27), Utc(2026, 9, 27), @"C:\V", @"C:\P", verdict: "Great"),
            Decision("d1", "X.DNG", 1, kind: "perhaps"),
            FolderRec("fo1", ZrelDir, "Z", new DateOnly(2026, 9, 27), new DateOnly(2026, 9, 27), "America/Anchorage", source: "moved"));
        Assert.Equal([1, 2, 3, 4], snap.ParseIssues.Select(i => i.Line));
        Assert.Empty(snap.Files);
        Assert.Empty(snap.Runs);
        Assert.Empty(snap.Decisions);
        Assert.Empty(snap.Folders);
    }

    [Fact]
    public void LedgerSnapshots_Empty_CarriesTheStatus()
    {
        LedgerFolderStatus status = TestLedger.Status();
        LedgerSnapshot e = LedgerSnapshots.Empty(status);
        Assert.Same(status, e.Status);
        Assert.Empty(e.Files);
        Assert.Empty(e.SourceFiles);
        Assert.Empty(e.ParseIssues);
        Assert.False(e.Folders.ContainsKey(@"C:\anything"));
    }
}
