// tests/UasSort.Core.Tests/Ledger/LedgerCodecTests.cs
using UasSort.Core.Ledger;
using UasSort.Core;
using static UasSort.Core.Tests.Ledger.LedgerLines;

namespace UasSort.Core.Tests.Ledger;

public sealed class LedgerCodecTests
{
    private const string Dest = @"C:\Lib\UAS Videos\2026\2026-09\2026-09-27 Zachar Bay\DJI_20260927140627_0128_D.MP4";

    public static TheoryData<string, LedgerRecord> EveryKind => new()
    {
        { "file", FileRec("6d0e0001", "DJI_20260927140627_0128_D.MP4", 89_612_345, Dest, lat: 57.5504421, lon: -153.738973,
                       tz: "America/Anchorage", captureUtc: Utc(2026, 9, 27, 18, 6, 27), sessionUtc: Utc(2026, 9, 27, 17, 59, 28), serial: "1581F0001") },
        { "folder", FolderRec("f0000001", @"C:\Lib\UAS Videos\2026\2026-09\2026-09-27 Zachar Bay", "Zachar Bay",
                              new DateOnly(2026, 9, 27), new DateOnly(2026, 9, 27), "America/Anchorage", 57.5415, -153.7409) },
        { "seen", Seen("s0000001", "DJI_20261002184012_0131_D.DNG", 27_399_100, Utc(2026, 10, 4, 20, 11)) },
        { "decision", Decision("a91", "PANO_0001.DNG", 13_751_808, set: "001_0087") },
        { "revoke", Revoke("r0000001", "a91") },
        { "run", Run("u0000001", "8f1c0001", Utc(2026, 9, 27, 21, 0), Utc(2026, 9, 27, 21, 30), @"C:\Lib\UAS Videos", @"C:\Lib\UAS Videos\Picture Offload") },
        { "torn", Torn("t0000001", 412) },
        { "cardDelete", CardDelete("c0000001", "DJI_20260725232655_0117_D.MP4", 1_234_567_890) },
        { "photoDelete", PhotoDelete("p0000001", "DJI_20260601121000_0002_D.DNG", 25_165_824) },
    };

    [Theory]
    [MemberData(nameof(EveryKind))]
    public void LedgerCodec_EveryKind_RoundTripsAsOneLineWithTFirst(string kind, LedgerRecord record)
    {
        string line = LedgerCodec.Serialize(record);
        Assert.StartsWith("{\"t\":\"" + kind + "\",", line, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", line, StringComparison.Ordinal);
        LedgerRecord? back = LedgerCodec.TryParse(line, out string? error);
        Assert.Null(error);
        Assert.NotNull(back);
        Assert.Equal(record.GetType(), back.GetType());
        Assert.Equal(line, LedgerCodec.Serialize(back));
    }

    [Fact]
    public void LedgerCodec_ParsesTheReferenceFileLine()
    {
        const string line = """
            {"t":"file","v":1,"id":"6d0e0001","machine":"DESKTOP-A","run":"8f1c0001","at":"2026-09-27T21:07:02Z","kind":"video","name":"DJI_20260927140627_0128_D.MP4","size":89612345,"src":"DCIM/DJI_001/DJI_20260927140627_0128_D.MP4","root":"video","dest":"C:\\Lib\\UAS Videos\\2026\\2026-09\\2026-09-27 Zachar Bay\\DJI_20260927140627_0128_D.MP4","xxh128":"5e0c0000000000000000000000000001","verify":"unbuffered","mtime":"2026-09-27T18:08:01Z","captureUtc":"2026-09-27T18:06:27Z","timeSource":"Mvhd","lat":57.5504421,"lon":-153.738973,"tz":"America/Anchorage","localDate":"2026-09-27","sessionUtc":"2026-09-27T17:59:28Z","serial":"1581F0001","set":null}
            """;
        var f = Assert.IsType<FileRecord>(LedgerCodec.TryParse(line, out string? error));
        Assert.Null(error);
        Assert.Equal("DJI_20260927140627_0128_D.MP4", f.Name);
        Assert.Equal(89_612_345, f.Size);
        Assert.Equal("unbuffered", f.Verify);
        Assert.Equal(Utc(2026, 9, 27, 18, 6, 27), f.CaptureUtc);
        Assert.Equal(DateTimeKind.Utc, f.At.Kind);
        Assert.Equal(new DateOnly(2026, 9, 27), f.LocalDate);
        Assert.Equal(Dest, f.Dest);
        Assert.Null(f.Set);
    }

    [Fact]
    public void LedgerCodec_AcceptsTheDiscriminatorOutOfOrder()
    {
        const string line = """{"v":1,"id":"r0000001","machine":"LAPTOP-B","at":"2026-10-05T02:00:00Z","decision":"a91","t":"revoke"}""";
        var r = Assert.IsType<RevokeRecord>(LedgerCodec.TryParse(line, out _));
        Assert.Equal("a91", r.Decision);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("[]")]
    [InlineData("""{"v":1,"id":"x1","machine":"A","at":"2026-10-05T02:00:00Z","decision":"a91"}""")]
    [InlineData("""{"t":"bogus","v":1,"id":"x1","machine":"A"}""")]
    [InlineData("""{"t":"revoke","v":2,"id":"x1","machine":"A","at":"2026-10-05T02:00:00Z","decision":"a91"}""")]
    [InlineData("""{"t":"revoke","v":1,"id":"","machine":"A","at":"2026-10-05T02:00:00Z","decision":"a91"}""")]
    [InlineData("""{"t":"revoke","v":1,"id":"x1","machine":"","at":"2026-10-05T02:00:00Z","decision":"a91"}""")]
    public void LedgerCodec_RejectsBadLinesWithAReason(string line)
    {
        Assert.Null(LedgerCodec.TryParse(line, out string? error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void LedgerCodec_NamesTheUnsupportedVersion()
    {
        LedgerCodec.TryParse("""{"t":"torn","v":2,"id":"t1","machine":"A","at":"2026-10-06T18:02:11Z","line":4}""", out string? error);
        Assert.Equal("unsupported v 2", error);
    }
}
