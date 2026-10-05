// tests/UasSort.Review.Tests/PhotoCleanupEngineTests.cs
using static UasSort.Testing.PhotoCleanupFixtures;

namespace UasSort.Review.Tests;

public sealed class PhotoCleanupEngineTests
{
    private const string Lr = @"X:\Lightroom";
    private static readonly DateTime Shot = new(2026, 6, 1, 12, 10, 0);

    private sealed class Rig
    {
        public Rig()
        {
            Fs.AddDirectory(Lr);
            var ctx = FakeLayout.Context(cardRoot: null) with { LightroomFolder = Lr };
            Reader = new FakePhotoFileReader(Fs, ctx);
            Ledger = new FakeLedgerStore(Fs, FakeLayout.VideoRoot, FakeLayout.Machine);
            Recyclers = new FakePhotoRootRecyclerFactory(Fs, ctx);
        }

        public FakeFileSystem Fs { get; } = FakeLayout.NewFileSystem();
        public FakePhotoFileReader Reader { get; }
        public FakeLedgerStore Ledger { get; }
        public FakePhotoRootRecyclerFactory Recyclers { get; }
        public MemReportStore Reports { get; } = new();
        public Settings Settings { get; set; } = FakeLayout.Settings() with { LightroomFolder = Lr };

        public void Dng(string path, DateTime dto) => Fs.AddFile(path, new SyntheticDngBuilder().WithDateTimeOriginal(dto).Build(), Mtime);

        public PhotoCleanupEngine Engine(PhotoRootThumbnails? thumbnails = null) => PhotoCleanupEngines.Create(
            new PhotoCleanupPorts(Settings, PhotoRoot, Fs, Reader, Ledger,
                PhotoCaptureClock.For(Settings, new GeoTimeZoneResolver(), Zones.Find("America/Anchorage")),
                new PhotoCleanupEnvironment(Recyclers, Fs, Ledger, new FakeOffloadLock(), new FakePowerRequest(),
                                            new FakeTimeProvider(new DateTimeOffset(2026, 10, 4, 20, 0, 0, TimeSpan.Zero)), FakeLayout.Machine),
                p => Fs.Metadata(p) is { IsDirectory: true }, Reports, new FakeShellLauncher()) { Thumbnails = thumbnails });
    }

    private static Task<PhotoCleanupPreparation> Prepare(PhotoCleanupEngine e) => e.Prepare(new Progress<PhotoScanProgress>(), CancellationToken.None);

    [Fact]
    public async Task Engine_PreparesVerifiesRunsAndReports_EndToEnd()
    {
        var rig = new Rig();
        rig.Dng(PhotoRoot + @"\A.DNG", Shot);
        rig.Dng(PhotoRoot + @"\B.DNG", Shot.AddSeconds(30));
        rig.Dng(Lr + @"\2026\2026-06-01\Damian_20260601_001.dng", Shot);
        var thumbnails = new PhotoRootThumbnails(rig.Reader);
        var engine = rig.Engine(thumbnails);

        var prep = await Prepare(engine);
        Assert.Null(prep.BlockingText);
        Assert.Null(prep.VerifyUnavailableText);
        Assert.Equal(Lr, prep.LightroomFolder);
        Assert.Equal(["A.DNG", "B.DNG"], prep.Survey!.Items.Select(i => i.RelPath));
        Assert.False((await thumbnails.GetAsync(PhotoRootThumbnails.KeyOf(prep.Survey.Items[0]), CancellationToken.None)).IsEmpty);

        var plan = await engine.Plan(prep.Survey, new PhotoCleanupRequest(PhotoCleanupMode.Verify, new DateOnly(2026, 6, 30), Lr),
                                     new Progress<PhotoScanProgress>(), CancellationToken.None);
        Assert.Equal(["A.DNG"], plan.DefaultDelete());
        var confirmed = plan.Confirm(new PhotoCleanupAck(plan.Fingerprint, plan.DefaultDelete(), true, false), TimeProvider.System);
        var result = await engine.Run(confirmed, new Progress<PhotoCleanupProgress>(), CancellationToken.None);

        Assert.IsType<PhotoRecycled>(Assert.Single(result.Outcomes));
        Assert.False(rig.Fs.Exists(PhotoRoot + @"\A.DNG"));
        Assert.True(rig.Fs.Exists(PhotoRoot + @"\B.DNG"));
        Assert.Equal("lightroom", Assert.IsType<PhotoDeleteRecord>(Assert.Single(rig.Ledger.Writer.Records)).Evidence);
        Assert.EndsWith("-photos.json", engine.SaveReport(PhotoCleanupReports.Build(result)), StringComparison.Ordinal);
        Assert.Single(rig.Reports.Photos);
        Assert.Empty(rig.Fs.HydrationViolations);
    }

    [Fact]
    public async Task Engine_Prepare_SaysWhyVerifyOrThePageIsUnavailable()
    {
        Assert.Equal("Set a Lightroom library folder in Settings to verify against Lightroom",
                     (await Prepare(new Rig { Settings = FakeLayout.Settings() }.Engine())).VerifyUnavailableText);
        Assert.Equal(@"The Lightroom folder Y:\Missing is not available",
                     (await Prepare(new Rig { Settings = FakeLayout.Settings() with { LightroomFolder = @"Y:\Missing" } }.Engine())).VerifyUnavailableText);

        var noRoot = new Rig();
        noRoot.Fs.RemoveUnguarded(PhotoRoot);
        Assert.Equal($"The photo folder {PhotoRoot} is not available", (await Prepare(noRoot.Engine())).BlockingText);

        var cloud = new Rig();
        cloud.Ledger.StatusOverride = new LedgerFolderStatus(LedgerPaths.For(FakeLayout.VideoRoot), LedgerFolderState.CloudOnly, true, true, false,
                                                             true, [], [], []);
        var prep = await Prepare(cloud.Engine());
        Assert.StartsWith("Set ", prep.BlockingText, StringComparison.Ordinal);
        Assert.NotNull(prep.KeepOnDevice);
    }
}
