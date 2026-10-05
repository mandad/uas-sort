// tests/UasSort.Core.Tests/Cleanup/Photos/PhotoCleanupSurveyTests.cs
using UasSort.Core.Tests.Ledger;
using static UasSort.Core.Tests.Ledger.LedgerLines;
using static UasSort.Testing.PhotoCleanupFixtures;

namespace UasSort.Core.Tests.Cleanup.Photos;

public sealed class PhotoCleanupSurveyTests
{
    private static readonly DateTime Shot = new(2026, 6, 1, 12, 10, 0);
    private static readonly DateTime May30Utc = new(2026, 5, 30, 20, 0, 0, DateTimeKind.Utc);
    private static readonly TimeZoneInfo JuneauPc = Zones.Find("America/Juneau");

    private sealed class Rig
    {
        public Rig()
        {
            Reader = new FakePhotoFileReader(Fs, FakeLayout.Context(cardRoot: null));
            Exif = new PhotoExifCache(Reader);
        }

        public FakeFileSystem Fs { get; } = FakeLayout.NewFileSystem();
        public FakePhotoFileReader Reader { get; }
        public PhotoExifCache Exif { get; }
        public LedgerSnapshot Ledger { get; set; } = TestLedger.Empty();
        public Settings Settings { get; set; } = FakeLayout.Settings();        // drone clock: Zone America/New_York

        public string Dng(string rel, DateTime dto, uint attributes = 0x20, string? offset = null, bool gps = true)
        {
            var path = PhotoRoot + @"\" + rel;
            var builder = new SyntheticDngBuilder { OffsetTimeOriginal = offset }.WithDateTimeOriginal(dto);
            Fs.AddFile(path, (gps ? builder : builder with { Gps = null }).Build(), Mtime, attributes);
            return path;
        }

        public long SizeOf(string path) => Fs.Metadata(path)!.Size;

        public PhotoSurvey Survey()
            => PhotoCleanupPlanner.Survey(PhotoRoot, Fs.Enumerate(PhotoRoot, true, PhotoCleanupPlanner.Excludes), Ledger, Exif,
                                          PhotoCaptureClock.For(Settings, new GeoTimeZoneResolver(), JuneauPc), null, CancellationToken.None);
    }

    [Fact]
    public void Survey_PairsDngAndJpg_FindsSetFolders_AndLeavesEverythingElseUntouched()
    {
        var rig = new Rig();
        rig.Dng("A.DNG", Shot);
        rig.Dng("A.JPG", Shot);
        rig.Dng("B.JPG", Shot.AddSeconds(3));
        rig.Fs.AddFile(PhotoRoot + @"\notes.txt", 10, Mtime);
        rig.Fs.AddFile(PhotoRoot + @"\desktop.ini", 10, Mtime);
        rig.Dng(@"001_0087\PANO_0001.DNG", Shot);
        rig.Dng(@"001_0087\PANO_0002.DNG", Shot.AddSeconds(2));
        rig.Dng(@"Exports\x.jpg", Shot);
        rig.Dng(@"002_0003\sub\x.DNG", Shot);

        var s = rig.Survey();

        Assert.Equal(["001_0087", "A.DNG", "B.JPG"], s.Items.Select(i => i.RelPath));
        Assert.Equal(["A.DNG", "A.JPG"], s.Items.Single(i => i.RelPath == "A.DNG").Members.Select(m => m.RelPath));
        var set = s.Items.Single(i => i.RelPath == "001_0087");
        Assert.Equal((PhotoItemKind.Set, PhotoSetKind.Unknown, "001_0087"), (set.Kind, set.SetKind, set.SetName));
        Assert.Equal([@"001_0087\PANO_0001.DNG", @"001_0087\PANO_0002.DNG"], set.Members.Select(m => m.RelPath));
        Assert.Equal(["002_0003", "desktop.ini", "Exports", "notes.txt"], s.NotTouched);
    }

    [Fact]
    public void Survey_ALedgerDate_WinsAndTheFileIsNotOpened()
    {
        var rig = new Rig();
        var path = rig.Dng("A.DNG", Shot);
        rig.Ledger = TestLedger.Snapshot(FileRec("f1", "A.DNG", rig.SizeOf(path), path, root: "photo", kind: "photo", captureUtc: May30Utc));
        var m = Assert.Single(Assert.Single(rig.Survey().Items).Members);
        Assert.Equal((CaptureSource.Ledger, (DateOnly?)new DateOnly(2026, 5, 30), (DateTime?)May30Utc), (m.Source, m.LocalDate, m.CaptureUtc));
        Assert.NotNull(m.Ledger);
        Assert.Null(m.Stamp);
        Assert.Empty(rig.Reader.Opened);
    }

    [Fact]
    public void Survey_OnlyTheRecordForThisDestAndSize_Dates()
    {
        var rig = new Rig();
        var path = rig.Dng("A.DNG", Shot);
        rig.Ledger = TestLedger.Snapshot(                                   // same name and size, another destination
            FileRec("f1", "A.DNG", rig.SizeOf(path), PhotoRoot + @"\Old\A.DNG", root: "photo", kind: "photo", captureUtc: May30Utc));
        var other = Assert.Single(Assert.Single(rig.Survey().Items).Members);
        Assert.Equal((CaptureSource.Exif, (LedgerFile?)null), (other.Source, other.Ledger));
        rig.Ledger = TestLedger.Snapshot(                                   // this destination, but the file there was replaced
            FileRec("f2", "A.DNG", rig.SizeOf(path) + 1, path, root: "photo", kind: "photo", captureUtc: May30Utc));
        Assert.Equal(CaptureSource.Exif, Assert.Single(Assert.Single(rig.Survey().Items).Members).Source);
    }

    [Fact]
    public void Survey_ALocalFileWithoutARecord_IsDatedFromItsExif()
    {
        var rig = new Rig();
        rig.Dng("A.DNG", new DateTime(2026, 6, 1, 23, 30, 0), offset: "-08:00");     // GPS: Kodiak (America/Anchorage, UTC−8)
        var m = Assert.Single(Assert.Single(rig.Survey().Items).Members);
        Assert.Equal((CaptureSource.Exif, (DateOnly?)new DateOnly(2026, 6, 1)), (m.Source, m.LocalDate));
        Assert.Equal(new DateTime(2026, 6, 2, 7, 30, 0, DateTimeKind.Utc), m.CaptureUtc);
        Assert.Equal(new ExifStamp(new DateTime(2026, 6, 1, 23, 30, 0), null, "FC9113"), m.Stamp);
        Assert.Single(rig.Reader.Opened);
    }

    [Fact]
    public void Survey_WithoutAnOffset_UsesTheSavedDroneClock_ThenThePcZone_LikeTheScan()
    {
        var rig = new Rig { Settings = FakeLayout.Settings() with { DroneClockMode = StoredClockMode.Zone, DroneClockZone = "Etc/UTC" } };
        rig.Dng("A.DNG", new DateTime(2026, 7, 1, 3, 30, 0), gps: false);             // a UTC drone clock, no GPS: the PC zone (Juneau, UTC−8)
        var m = Assert.Single(Assert.Single(rig.Survey().Items).Members);
        Assert.Equal(new DateTime(2026, 7, 1, 3, 30, 0, DateTimeKind.Utc), m.CaptureUtc);
        Assert.Equal((CaptureSource.Exif, (DateOnly?)new DateOnly(2026, 6, 30)), (m.Source, m.LocalDate));   // not the naive Jul 1
    }

    [Fact] // [Review Focus 1]
    public void Survey_CloudOnlyPhoto_IsDateUnknown_AndNeverOpened()
    {
        var rig = new Rig();
        rig.Dng("A.DNG", Shot, FakeFileSystem.CloudOnlyPlaceholder);                       // no ledger record
        var dated = rig.Dng("B.DNG", Shot, FakeFileSystem.CloudOnlyPlaceholder);           // has a ledger record
        rig.Dng(@"001_0087\PANO_0001.DNG", Shot);
        rig.Dng(@"001_0087\PANO_0002.DNG", Shot, FakeFileSystem.CloudOnlyPlaceholder);
        rig.Ledger = TestLedger.Snapshot(FileRec("f1", "B.DNG", rig.SizeOf(dated), dated, root: "photo", kind: "photo", captureUtc: May30Utc));

        var s = rig.Survey();

        var a = s.Items.Single(i => i.RelPath == "A.DNG").Primary;
        Assert.Null(a.LocalDate);
        Assert.Equal("date unknown (cloud-only)", a.DateProblem);
        Assert.Equal(new DateOnly(2026, 5, 30), s.Items.Single(i => i.RelPath == "B.DNG").Primary.LocalDate);
        Assert.False(s.Items.Single(i => i.RelPath == "001_0087").DateKnown);
        Assert.Empty(rig.Fs.HydrationViolations);
        Assert.Equal([PhotoRoot + @"\001_0087\PANO_0001.DNG"], rig.Reader.Opened);
    }

    [Fact]
    public void Survey_SetKindAndNameComeFromTheLedger()
    {
        var rig = new Rig();
        var hyper = rig.Dng(@"001_0042 2026-06-01\HYPERLAPSE_0001.DNG", Shot);
        var other = rig.Dng(@"Kodiak pano\PANO_0001.DNG", Shot);
        rig.Ledger = TestLedger.Snapshot(
            FileRec("f1", "HYPERLAPSE_0001.DNG", 1, hyper, root: "photo", kind: "setMember", set: "001_0042", captureUtc: May30Utc)
                with { Src = "DCIM/HYPERLAPSE/001_0042/HYPERLAPSE_0001.DNG" },
            FileRec("f2", "PANO_0001.DNG", 2, other, root: "photo", kind: "setMember", set: "003_0004", captureUtc: May30Utc)
                with { Src = "DCIM/PANORAMA/003_0004/PANO_0001.DNG" });
        var s = rig.Survey();
        var h = s.Items.Single(i => i.RelPath == "001_0042 2026-06-01");
        Assert.Equal((PhotoSetKind.Hyperlapse, "001_0042"), (h.SetKind, h.SetName));
        var p = s.Items.Single(i => i.RelPath == "Kodiak pano");
        Assert.Equal((PhotoSetKind.Panorama, "003_0004"), (p.SetKind, p.SetName));
    }

    [Fact]
    public void Survey_AnUnreadableLocalFile_HasADateProblem()
    {
        var rig = new Rig();
        rig.Fs.AddFile(PhotoRoot + @"\C.DNG", [1, 2, 3, 4, 5, 6, 7, 8], Mtime);
        var m = Assert.Single(Assert.Single(rig.Survey().Items).Members);
        Assert.Null(m.LocalDate);
        Assert.StartsWith("its capture time couldn't be read", m.DateProblem, StringComparison.Ordinal);
    }
}
