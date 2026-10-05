// tests/UasSort.Core.Tests/Cleanup/Photos/PhotoCleanupVerifierTests.cs
using UasSort.Core.Tests.Ledger;
using static UasSort.Core.Tests.Ledger.LedgerLines;
using static UasSort.Testing.PhotoCleanupFixtures;

namespace UasSort.Core.Tests.Cleanup.Photos;

public sealed class PhotoCleanupVerifierTests
{
    private const string Lr = @"X:\Lightroom";
    private static readonly DateOnly D = new(2026, 6, 1);
    private static readonly DateTime S = new(2026, 6, 1, 12, 10, 0);
    private static readonly DateTime SUtc = new(2026, 6, 1, 20, 10, 0, DateTimeKind.Utc);      // S on a UTC−8 drone clock

    private static ExifStamp St(int second = 0, string? sub = null, string model = "FC9113") => new(S.AddSeconds(second), sub, model);

    private static LightroomPhoto L(string name, ExifStamp stamp) => new(Lr + @"\2026\2026-06-01\" + name, stamp);

    private static PhotoItem P(string name, ExifStamp? stamp, uint attributes = 0x20, DateTime? captureUtc = null)
        => new(name, PhotoItemKind.Photo, PhotoSetKind.Unknown, null,
               [Member(name, D, attributes: attributes, stamp: stamp, captureUtc: captureUtc ?? (stamp is null ? null : (DateTime?)SUtc.AddSeconds((stamp.Second - S).TotalSeconds)))]);

    /// <summary>A loose JPG-only still with its pixel size (the stitched-panorama shape test).</summary>
    private static PhotoItem Jpg(string name, ExifStamp stamp, (int Width, int Height)? pixels)
        => new(name, PhotoItemKind.Photo, PhotoSetKind.Unknown, null, [Member(name, D, 9_000_000, stamp: stamp) with { Pixels = pixels }]);

    private static PhotoVerification Verify(PhotoItem item, IEnumerable<PhotoItem> all, LedgerSnapshot? ledger = null, params LightroomPhoto[] lightroom)
        => PhotoCleanupVerifier.Verify(item, PhotoCleanupVerifier.ContextFor([.. all], LightroomIndex.From(Lr, lightroom), ledger ?? TestLedger.Empty()));

    [Fact]
    public void Verify_APhotoWithTheSameSubSecondsAndModel_IsInLightroom()
    {
        var a = P("A.DNG", St(sub: "045"));
        Assert.Equal(new PhotoVerification(true, PhotoEvidence.Lightroom, @"in Lightroom: 2026\2026-06-01\Damian_20260601_001.dng"),
                     Verify(a, [a], null, L("Damian_20260601_001.dng", St(sub: "0450"))));
    }

    [Fact]
    public void Verify_AModelOrSecondMismatch_IsNotFound()
    {
        var a = P("A.DNG", St());
        Assert.Equal("not found in Lightroom", Verify(a, [a], null, L("x.dng", St(model: "FC8282"))).Text);
        Assert.False(Verify(a, [a], null, L("x.dng", St(second: 1))).Verified);
    }

    [Fact]
    public void Verify_ALightroomJpgWithTheSameStamp_NeverVerifiesAPhotoOrAFrame()
    {
        var a = P("A.DNG", St(sub: "045"));
        Assert.Equal("not found in Lightroom", Verify(a, [a], null, L("Damian_20260601_001.jpg", St(sub: "045"))).Text);   // an export or edit
        var set = new PhotoItem("001_0087", PhotoItemKind.Set, PhotoSetKind.Unknown, "001_0087", [Member(@"001_0087\PANO_0001.DNG", D, stamp: St(5))]);
        Assert.False(Verify(set, [set], null, L("pano-frame.jpg", St(5))).Verified);
        Assert.Equal(new PhotoVerification(true, PhotoEvidence.Lightroom, @"in Lightroom: 2026\2026-06-01\x.dng"),
                     Verify(a, [a], null, L("x.jpg", St(sub: "045")), L("x.dng", St(sub: "045"))));        // a DNG and its export: the DNG
    }

    [Fact] // [Review Focus 1]
    public void Verify_ACloudOnlyPhoto_CantBeChecked()
    {
        var a = P("A.DNG", null, FakeFileSystem.CloudOnlyPlaceholder);
        Assert.Equal(new PhotoVerification(false, PhotoEvidence.Lightroom, "not on this PC (cloud-only) — couldn't check"),
                     Verify(a, [a], null, L("x.dng", St())));
    }

    [Fact] // [Review Focus 3]
    public void Verify_ShotsInTheSameSecond_NeedOneLightroomFileEach()
    {
        var a = P("A.DNG", St(sub: "100"));
        var b = P("B.DNG", St(sub: "600"));
        Assert.True(Verify(a, [a, b], null, L("a.dng", St(sub: "100"))).Verified);
        Assert.Equal("not found in Lightroom (another shot in the same second is)", Verify(b, [a, b], null, L("a.dng", St(sub: "100"))).Text);
        Assert.False(Verify(a, [a, b], null, L("x.dng", St())).Verified);                        // Lightroom has no sub-seconds: can't tell which
        PhotoItem[] aeb = [P("E1.DNG", St()), P("E2.DNG", St()), P("E3.DNG", St())];
        Assert.Equal("3 shots in the same second; only 1 in Lightroom — can't tell which", Verify(aeb[0], aeb, null, L("x.dng", St())).Text);
        Assert.Equal("3 shots in the same second; only 1 in Lightroom — can't tell which",
                     Verify(aeb[0], aeb, null, L("x.dng", St()), L("x.jpg", St()), L("y.jpg", St())).Text);   // exports don't count
        Assert.True(Verify(aeb[1], aeb, null, L("x.dng", St()), L("y.dng", St()), L("z.dng", St())).Verified);
        var only = P("O.DNG", St());
        Assert.True(Verify(only, [only], null, L("o.dng", St(sub: "500"))).Verified);           // to the second when one side has none
    }

    [Fact] // [Review Focus 3]
    public void Verify_AnUnreadableShotInTheSameSecond_LeavesOnlyExactSubSecondMatches()
    {
        var a = P("A.DNG", St());
        var cloud = P("B.DNG", null, FakeFileSystem.CloudOnlyPlaceholder, captureUtc: SUtc.AddMilliseconds(400));   // dated from the ledger only
        Assert.Equal("2 shots in the same second, 1 of them couldn't be read — can't tell which",
                     Verify(a, [a, cloud], null, L("x.dng", St())).Text);
        var exact = P("C.DNG", St(sub: "100"));
        Assert.True(Verify(exact, [exact, cloud], null, L("c.dng", St(sub: "100"))).Verified);
        var later = P("D.DNG", null, FakeFileSystem.CloudOnlyPlaceholder, captureUtc: SUtc.AddSeconds(1));         // another second: no peer
        Assert.True(Verify(a, [a, later], null, L("x.dng", St())).Verified);
    }

    [Fact]
    public void Verify_APair_IsDecidedByItsDng()
    {
        var pair = new PhotoItem("A.DNG", PhotoItemKind.Photo, PhotoSetKind.Unknown, null,
            [Member("A.DNG", D, stamp: St()), Member("A.JPG", D, 8_000_000, stamp: St(second: 40))]);
        Assert.True(Verify(pair, [pair], null, L("a.dng", St())).Verified);
    }

    [Fact]
    public void Verify_ASetWithEveryFrameInLightroom_IsVerified_ElseItSaysWhichFramesAreMissing()
    {
        var set = new PhotoItem("001_0087", PhotoItemKind.Set, PhotoSetKind.Panorama, "001_0087",
            [Member(@"001_0087\PANO_0001.DNG", D, stamp: St()), Member(@"001_0087\PANO_0002.DNG", D, stamp: St(second: 2))]);
        Assert.Equal(new PhotoVerification(true, PhotoEvidence.Lightroom, "every frame is in Lightroom (2)"),
                     Verify(set, [set], null, L("p1.dng", St()), L("p2.dng", St(second: 2))));
        Assert.Equal("no stitched panorama next to the frames; 1 of 2 frames not confirmed: not found in Lightroom",
                     Verify(set, [set], null, L("p1.dng", St())).Text);
    }

    [Fact]
    public void Verify_APanorama_ByItsOnePanoramaShapedStitchedImage()
    {
        var set = new PhotoItem("001_0087", PhotoItemKind.Set, PhotoSetKind.Panorama, "001_0087",
            [Member(@"001_0087\PANO_0001.DNG", D, stamp: St()), Member(@"001_0087\PANO_0002.DNG", D, stamp: St(second: 20))]);
        var stitched = Jpg("DJI_20260601121120_0010_D.JPG", St(second: 80), (8192, 4096));
        var late = Jpg("DJI_20260601121500_0011_D.JPG", St(second: 300), (8192, 4096));
        Assert.Equal(new PhotoVerification(true, PhotoEvidence.PanoramaStitch, "stitched panorama DJI_20260601121120_0010_D.JPG is in Lightroom"),
                     Verify(set, [set, stitched, late], null, L("pano.dng", St(second: 80))));
        Assert.True(Verify(set, [set, stitched], null, L("pano-export.jpg", St(second: 80))).Verified);      // spec §4: DNG or JPG
        Assert.False(Verify(set, [set, late], null, L("late.dng", St(second: 300))).Verified);              // more than 2 min after the last frame
    }

    [Fact]
    public void Verify_AnOrdinaryJpgNextToThePanorama_NeverVerifiesIt()
    {
        var set = new PhotoItem("001_0087", PhotoItemKind.Set, PhotoSetKind.Panorama, "001_0087",
            [Member(@"001_0087\PANO_0001.DNG", D, stamp: St()), Member(@"001_0087\PANO_0002.DNG", D, stamp: St(second: 20))]);
        var normal = Jpg("DJI_20260601121050_0010_D.JPG", St(second: 50), (4032, 3024));
        var stitched = Jpg("DJI_20260601121120_0011_D.JPG", St(second: 80), (8192, 4096));
        var unknownSize = Jpg("DJI_20260601121120_0012_D.JPG", St(second: 80), null);
        Assert.Equal("DJI_20260601121050_0010_D.JPG next to the frames is not panorama-shaped; 2 of 2 frames not confirmed: not found in Lightroom",
                     Verify(set, [set, normal], null, L("n.dng", St(second: 50))).Text);
        Assert.Equal("2 JPG photos next to the frames — can't tell which is the stitched panorama; 2 of 2 frames not confirmed: not found in Lightroom",
                     Verify(set, [set, normal, stitched], null, L("n.dng", St(second: 50)), L("s.dng", St(second: 80))).Text);
        Assert.False(Verify(set, [set, unknownSize], null, L("u.dng", St(second: 80))).Verified);
    }

    private const string UnreadableJpgNextToFrames = "a JPG next to the frames couldn't be read — can't tell which is the stitched panorama";

    private static PhotoItem Pano(string folder, params int[] seconds)
        => new(folder, PhotoItemKind.Set, PhotoSetKind.Panorama, folder,
               [.. seconds.Select((s, i) => Member($@"{folder}\PANO_{i + 1:0000}.DNG", D, stamp: St(s)))]);

    /// <summary>A loose JPG-only still without a stamp: cloud-only (dated from the ledger) or with no date at all.</summary>
    private static PhotoItem StamplessJpg(string name, DateOnly? date)
        => new(name, PhotoItemKind.Photo, PhotoSetKind.Unknown, null,
               [new PhotoMember(name, 9_000_000, Mtime, FakeFileSystem.CloudOnlyPlaceholder,
                                date is { } d ? (d == D ? SUtc.AddSeconds(45) : d.ToDateTime(new TimeOnly(20, 10, 45), DateTimeKind.Utc)) : null,
                                date, date is null ? CaptureSource.None : CaptureSource.Ledger, null,
                                date is null ? PhotoCleanupRules.DateUnknownCloudOnly : null, null)]);

    [Fact] // branch-2 ruling (deferred minor P.9 #1): two panoramas back to back, set A's stitched JPG cloud-only, set B's in Lightroom
    public void Verify_APanoramaNextToAStitchedJpgThatCouldntBeRead_IsUnverified_NeverByAnotherPanoramasStitch()
    {
        var a = Pano("001_0087", 0, 40);                                                           // 12:10:00 – 12:10:40
        var stitchedA = StamplessJpg("DJI_20260601121045_0010_D.JPG", D);                         // cloud-only: no stamp
        var b = Pano("001_0088", 70, 110);                                                         // 12:11:10 – 12:11:50
        var stitchedB = Jpg("DJI_20260601121155_0020_D.JPG", St(115), (8192, 4096));
        PhotoItem[] all = [a, stitchedA, b, stitchedB];
        var lightroom = L("pano-b.dng", St(115));
        Assert.True(Verify(a, [a, b, stitchedB], null, lightroom).Verified);                       // the bug: B's stitch "verifies" A
        Assert.Equal(new PhotoVerification(false, PhotoEvidence.PanoramaStitch, UnreadableJpgNextToFrames + "; 2 of 2 frames not confirmed: not found in Lightroom"),
                     Verify(a, all, null, lightroom));
        Assert.False(Verify(b, all, null, lightroom).Verified);                                     // conservative: B can't tell either
    }

    [Fact]
    public void Verify_AStamplessJpg_BlocksThePanoramaWhenUndatedOrWithinADayOfTheFrames_NotOtherwise()
    {
        var set = Pano("001_0087", 0, 20);
        var stitched = Jpg("DJI_20260601121120_0010_D.JPG", St(80), (8192, 4096));
        var lightroom = L("pano.dng", St(80));
        Assert.True(Verify(set, [set, stitched, StamplessJpg("X.JPG", D.AddDays(2))], null, lightroom).Verified);
        Assert.True(Verify(set, [set, stitched, StamplessJpg("X.JPG", D.AddDays(-2))], null, lightroom).Verified);
        Assert.StartsWith(UnreadableJpgNextToFrames, Verify(set, [set, stitched, StamplessJpg("X.JPG", D.AddDays(1))], null, lightroom).Text, StringComparison.Ordinal);
        Assert.StartsWith(UnreadableJpgNextToFrames, Verify(set, [set, stitched, StamplessJpg("X.JPG", D.AddDays(-1))], null, lightroom).Text, StringComparison.Ordinal);
        Assert.StartsWith(UnreadableJpgNextToFrames, Verify(set, [set, stitched, StamplessJpg("X.JPG", null)], null, lightroom).Text, StringComparison.Ordinal);
        var pair = new PhotoItem("Y.DNG", PhotoItemKind.Photo, PhotoSetKind.Unknown, null,                     // a DNG+JPG pair is no stitch candidate
            [Member("Y.DNG", D, attributes: FakeFileSystem.CloudOnlyPlaceholder, captureUtc: SUtc.AddSeconds(300)),
             Member("Y.JPG", D, attributes: FakeFileSystem.CloudOnlyPlaceholder, captureUtc: SUtc.AddSeconds(300))]);
        Assert.True(Verify(set, [set, stitched, pair], null, lightroom).Verified);
        Assert.True(Verify(set, [set, stitched, StamplessJpg("Z.DNG", D)], null, lightroom).Verified);           // not a JPG
    }

    private static readonly DateTime T0 =new(2026, 6, 1, 20, 10, 0, DateTimeKind.Utc);
    private const string Serial = "1581F0001";

    private const string Run1 = "8f1c0001", Run2 = "8f1c0002";
    private const string VideoName = "DJI_20260601161130_0007_D.MP4";

    /// <summary>Hyperlapse frames as the offload really records them (TimeResolver/OffloadRecords): a setMember file record per frame with
    /// its run, capture time and set name, and NO session or serial (only MP4s carry a SessionKey). session: the hypothetical extra path.</summary>
    private static PhotoItem Hyperlapse(string? run = Run1, SessionKey? session = null)
    {
        LedgerFile? Rec(string name, DateTime capture) => run is null ? null : new(FileKey.Of(name, 13_000_000), "DCIM/HYPERLAPSE/001_0042/" + name,
            DestRoot.Photo, PhotoRoot + @"\001_0042\" + name, null, VerifyKind.Unbuffered, T0.AddHours(5), capture, null, "America/Juneau",
            DateOnly.FromDateTime(capture), session, "001_0042", "DESKTOP-A", run);
        return new PhotoItem("001_0042", PhotoItemKind.Set, PhotoSetKind.Hyperlapse, "001_0042",
            [Member(@"001_0042\HYPERLAPSE_0001.DNG", D, captureUtc: T0, ledger: Rec("HYPERLAPSE_0001.DNG", T0)),
             Member(@"001_0042\HYPERLAPSE_0002.DNG", D, captureUtc: T0.AddSeconds(90), ledger: Rec("HYPERLAPSE_0002.DNG", T0.AddSeconds(90)))]);
    }

    private static LedgerSnapshot Video(DateTime capture, string run = Run1, string? serial = Serial, DateTime? sessionUtc = null)
        => TestLedger.Snapshot(FileRec("v1", VideoName, 300_000_000, @"C:\Lib\UAS Videos\2026\2026-06\2026-06-01 Juneau\" + VideoName,
                                       captureUtc: capture, serial: serial, sessionUtc: sessionUtc ?? T0.AddMinutes(-5), run: run));

    private static readonly PhotoVerification HyperlapseVerified =
        new(true, PhotoEvidence.HyperlapseResult, "hyperlapse result video is in the library: " + VideoName);

    [Fact] // branch-2 ruling: the frames link to DJI's result video through the offload run
    public void Verify_AHyperlapse_ByAVideoOfTheSameOffloadRunWithinTwoMinutesOfItsSpan()
    {
        var set = Hyperlapse();
        Assert.Equal(HyperlapseVerified, Verify(set, [set], Video(T0.AddSeconds(150))));                     // 1 min after the last frame
        Assert.Equal(HyperlapseVerified, Verify(set, [set], Video(T0.AddSeconds(-120))));                    // the window's first instant
        Assert.Equal(HyperlapseVerified, Verify(set, [set], Video(T0.AddSeconds(210), serial: null, sessionUtc: T0)));   // no serial needed
        Assert.Equal("hyperlapse result video not in the library", Verify(set, [set], Video(T0.AddSeconds(211))).Text);
        Assert.Equal("hyperlapse result video not in the library", Verify(set, [set], Video(T0.AddHours(1))).Text);
    }

    [Fact]
    public void Verify_AHyperlapse_AVideoOfAnotherOffloadInTheWindow_DoesNotVerify()
    {
        var set = Hyperlapse();
        Assert.Equal("a video in the library was shot then, but it came from another offload — can't tell it is this hyperlapse's result",
                     Verify(set, [set], Video(T0.AddSeconds(150), run: Run2)).Text);
    }

    [Fact]
    public void Verify_AHyperlapse_WhoseFramesHaveNoOffloadRecord_IsUnverified_WithItsReason()
    {
        var set = Hyperlapse(run: null);
        Assert.Equal(new PhotoVerification(false, PhotoEvidence.HyperlapseResult,
                         "these frames have no offload record in the history — their result video can't be identified"),
                     Verify(set, [set], Video(T0.AddSeconds(150))));
    }

    [Fact] // the session and serial paths stay as additional ways (ruling), should a frame record ever carry a session
    public void Verify_AHyperlapse_TheSessionAndSerialPaths_StillVerify()
    {
        var set = Hyperlapse(run: Run2, session: new SessionKey(Serial, T0.AddMinutes(-30)));
        Assert.Equal(HyperlapseVerified, Verify(set, [set], Video(T0.AddHours(3), sessionUtc: T0.AddMinutes(-30))));   // same session, any time
        Assert.Equal(HyperlapseVerified, Verify(set, [set], Video(T0.AddSeconds(150))));                                // same serial, in the window
        Assert.Equal("a video in the library was shot then, but it came from another offload — can't tell it is this hyperlapse's result",
                     Verify(set, [set], Video(T0.AddSeconds(150), serial: "1581F0999")).Text);
    }
}
