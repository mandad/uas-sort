// tests/UasSort.Core.Tests/Json/JsonContextTests.cs
using System.Text.Json;

namespace UasSort.Core.Tests.Json;

public class JsonContextTests
{
    // Ref §11 example lines with concrete values
    private const string FileLine = """{"t":"file","v":1,"id":"6d0e","machine":"DESKTOP-A","run":"8f1c","at":"2026-09-27T21:07:02Z","kind":"video","name":"DJI_20260927140627_0128_D.MP4","size":89612345,"src":"DCIM/DJI_001/DJI_20260927140627_0128_D.MP4","root":"video","dest":"C:\\V\\2026\\2026-09\\2026-09-27 Zachar Bay\\DJI_20260927140627_0128_D.MP4","xxh128":"5e0c","verify":"unbuffered","mtime":"2026-09-27T18:08:01Z","captureUtc":"2026-09-27T18:06:27Z","timeSource":"Mvhd","lat":57.5504421,"lon":-153.738973,"tz":"America/Anchorage","localDate":"2026-09-27","sessionUtc":"2026-09-27T17:59:28Z","serial":"SER1","set":null}""";

    private const string RunLine = """{"t":"run","v":1,"id":"u1","machine":"DESKTOP-A","run":"8f1c","start":"2026-09-27T21:00:00Z","end":"2026-09-27T21:30:00Z","app":"0.1.0","card":{"serial":"1A2B3C4D","label":null,"fs":"exFAT","model":"FC9113","inventoryHash":"9f3c0a6d12e4b7a1"},"roots":{"video":"C:\\V","photo":"C:\\V\\Picture Offload"},"verdict":"SafeWithAssumptions","counts":{"VerifiedThisRun":17,"AssumedByRule":40}}""";

    public static TheoryData<string, string> LedgerLines => new()
    {
        { "file", FileLine },
        { "folder", """{"t":"folder","v":1,"id":"f1","machine":"DESKTOP-A","run":"8f1c","path":"C:\\V\\2026\\2026-09\\2026-09-27 Zachar Bay","desc":"Zachar Bay","source":"created","lat":57.5415,"lon":-153.7409,"start":"2026-09-27","end":"2026-09-27","tz":"America/Anchorage"}""" },
        { "seen", """{"t":"seen","v":1,"id":"s1","machine":"DESKTOP-A","run":"8f1c","at":"2026-10-04T20:11:00Z","name":"DJI_20261002224012_0131_D.DNG","size":27399100,"src":"DCIM/DJI_001/DJI_20261002224012_0131_D.DNG","captureUtc":"2026-10-02T22:40:12Z","status":"New","why":"unticked","set":null}""" },
        { "decision", """{"t":"decision","v":1,"id":"b02","machine":"DESKTOP-A","run":"8f1c","at":"2026-09-27T21:40:00Z","kind":"assumedImported","name":"PANO_0001.DNG","size":13751808,"src":"DCIM/PANORAMA/001_0087/PANO_0001.DNG","captureUtc":"2026-05-25T13:30:28Z","why":"confirmed by you","set":"001_0087"}""" },
        { "revoke", """{"t":"revoke","v":1,"id":"r1","machine":"LAPTOP-B","at":"2026-10-05T02:00:00Z","decision":"a91"}""" },
        { "torn", """{"t":"torn","v":1,"id":"t1","machine":"DESKTOP-A","at":"2026-10-06T18:02:11Z","line":412}""" },
        { "cardDelete", """{"t":"cardDelete","v":1,"id":"c1","machine":"DESKTOP-A","run":"c4e2","at":"2026-10-12T19:30:05Z","name":"DJI_20260725232655_0117_D.MP4","size":1234567890,"src":"DCIM/DJI_001/DJI_20260725232655_0117_D.MP4","unit":"DCIM/DJI_001/DJI_20260725232655_0117_D.MP4","captureUtc":"2026-07-26T03:26:55Z","evidence":"InLedger","reason":"in the history, verified","mode":"beforeDate","card":{"serial":"1A2B3C4D","label":null,"fs":"exFAT","model":"FC9113","inventoryHash":"9f3c0a6d12e4b7a1"},"set":null}""" },
        { "run", RunLine },
    };

    [Theory]
    [MemberData(nameof(LedgerLines))]
    public void LedgerRecords_RoundTripThroughTheSourceGeneratedContext(string kind, string line)
    {
        var record = JsonSerializer.Deserialize(line, LedgerJsonContext.Default.LedgerRecord)!;
        var once = JsonSerializer.Serialize(record, LedgerJsonContext.Default.LedgerRecord);
        var twice = JsonSerializer.Serialize(JsonSerializer.Deserialize(once, LedgerJsonContext.Default.LedgerRecord)!,
                                             LedgerJsonContext.Default.LedgerRecord);
        Assert.StartsWith($$"""{"t":"{{kind}}",""", once, StringComparison.Ordinal);
        Assert.Equal(once, twice);
        Assert.DoesNotContain("\n", once, StringComparison.Ordinal);
        Assert.Equal(1, record.V);
    }

    [Fact]
    public void FileRecord_KeepsEveryField()
    {
        var r = Assert.IsType<FileRecord>(JsonSerializer.Deserialize(FileLine,
            LedgerJsonContext.Default.LedgerRecord));
        Assert.Equal(89_612_345, r.Size);
        Assert.Equal(new DateOnly(2026, 9, 27), r.LocalDate);
        Assert.Equal(DateTimeKind.Utc, r.CaptureUtc!.Value.Kind);
        Assert.Equal(57.5504421, r.Lat);
        Assert.Null(r.Set);
    }

    [Fact]
    public void RunRecord_KeepsCountsKeysVerbatim()
    {
        var r = Assert.IsType<RunRecord>(JsonSerializer.Deserialize(RunLine,
            LedgerJsonContext.Default.LedgerRecord));
        Assert.Equal(17, r.Counts["VerifiedThisRun"]);
        Assert.Equal("FC9113", r.Card.Model);
    }

    [Fact]
    public void Discriminator_MayComeAfterOtherProperties()
    {
        var r = JsonSerializer.Deserialize("""{"v":1,"id":"t1","machine":"DESKTOP-A","t":"torn","at":"2026-10-06T18:02:11Z","line":412}""",
            LedgerJsonContext.Default.LedgerRecord);
        Assert.Equal(412, Assert.IsType<TornRecord>(r).Line);
    }

    [Fact]
    public void Settings_SerialiseCamelCaseWithoutALedgerDirKey()
    {
        var s = new Settings(1, @"C:\Users\u\OneDrive\Pictures\UAS Videos", @"C:\Users\u\OneDrive\Pictures\UAS Videos\Picture Offload", [],
            50, 1, StoredClockMode.Zone, "America/New_York", true,
            new MapSettings("streets", "https://tiles.openfreemap.org/styles/liberty", "https://tiles.openfreemap.org/styles/dark",
                "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}",
                ImmutableDictionary<string, string>.Empty.Add("USGS", "https://basemap.nationalmap.gov/arcgis/rest/services/USGSImageryOnly/MapServer/tile/{z}/{y}/{x}")),
            new LayoutSettings(380, 0.45), true);
        var json = JsonSerializer.Serialize(s, CoreJsonContext.Default.Settings);
        Assert.Contains("\"rootsConfirmed\": true", json, StringComparison.Ordinal);
        Assert.Contains("\"droneClockMode\": \"Zone\"", json, StringComparison.Ordinal);
        Assert.Contains("\"USGS\":", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ledgerDir", json, StringComparison.OrdinalIgnoreCase);
        var back = JsonSerializer.Deserialize(json, CoreJsonContext.Default.Settings)!;
        Assert.Equal(s.VideoRoot, back.VideoRoot);
        Assert.Equal(0.45, back.Layout.MapHeightRatio);
    }

    [Fact]
    public void Draft_WithClosedEdits_RoundTrips()
    {
        var a = new ItemId("DCIM/DJI_001/DJI_20260725232655_0117_D.MP4");
        var b = new ItemId("DCIM/DJI_001/DJI_20260726235645_0001_D.MP4");
        var draft = new Draft(1, "vol-1A2B3C4D", "9f3c0a6d12e4b7a1", new DateTime(2026, 9, 27, 21, 0, 0, DateTimeKind.Utc), new Tuning(25, 1),
            [new Merge(a, b), new SplitBefore(b), new Rename(b, "Anvil Mountain", [b]),
             new Retarget(a, new AppendTo(@"C:\V\2026\2026-07\2026-07-25 Council Road"), false, []), new SetDayIncluded(new DateOnly(2026, 7, 26), false)]);
        var json = JsonSerializer.Serialize(draft, CoreJsonContext.Default.Draft);
        Assert.Contains("\"inA\": \"DCIM/DJI_001/DJI_20260725232655_0117_D.MP4\"", json, StringComparison.Ordinal);
        Assert.Contains("\"t\": \"appendTo\"", json, StringComparison.Ordinal);
        var back = JsonSerializer.Deserialize(json, CoreJsonContext.Default.Draft)!;
        Assert.Equal(json, JsonSerializer.Serialize(back, CoreJsonContext.Default.Draft));
        Assert.IsType<Merge>(back.Edits[0]);
        var retarget = Assert.IsType<Retarget>(back.Edits[3]);
        Assert.Equal(@"C:\V\2026\2026-07\2026-07-25 Council Road", Assert.IsType<AppendTo>(retarget.Choice).FolderFullPath);
        Assert.Equal(b, Assert.Single(Assert.IsType<Rename>(back.Edits[2]).PinnedMembers));
    }

    [Fact]
    public void GeoPoint_IsWrittenAsLonLat()
    {
        var json = JsonSerializer.Serialize(new GeoPoint(64.5627, -165.3696), CoreJsonContext.Default.GeoPoint);
        Assert.Equal("[-165.3696,64.5627]", string.Concat(json.Where(c => !char.IsWhiteSpace(c))));
        Assert.Equal(new GeoPoint(64.5627, -165.3696), JsonSerializer.Deserialize(json, CoreJsonContext.Default.GeoPoint));
    }

    [Fact]
    public void CleanupReport_Serialises()
    {
        var r = new CleanupReport(1, "c4e2", new CardIdentity(0x1A2B3C4D, null, "exFAT", 256_060_514_304),
            new CleanupRequest(CleanupMode.BeforeDate, new DateOnly(2026, 7, 26), null, false),
            new CleanupCutoff(new DateOnly(2026, 7, 26), null, null, null, 0, 0, null),
            [new CleanupReportLine("DCIM/DJI_001/a.MP4", 10, "DCIM/DJI_001/a.MP4", "Deleted", "Evidence", "InLedger", "in the history, verified", null, true)],
            [], null, 12_400_000_000, 12_400_131_072, VerdictLevel.Safe);
        var json = JsonSerializer.Serialize(r, CoreJsonContext.Default.CleanupReport);
        Assert.Contains("\"mode\": \"BeforeDate\"", json, StringComparison.Ordinal);
        Assert.Contains("\"ledgerRecorded\": true", json, StringComparison.Ordinal);
    }
}
