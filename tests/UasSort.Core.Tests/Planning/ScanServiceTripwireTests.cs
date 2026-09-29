using UasSort.Core.Geo;
using UasSort.Core.Planning;
using UasSort.Testing.Planning;

namespace UasSort.Core.Tests.Planning;

public sealed class ScanServiceTripwireTests
{
    private const string VideoRoot = @"C:\lib\UAS Videos";
    private const string PhotoRoot = @"C:\lib\UAS Videos\Picture Offload";
    private const string Ledger = @"C:\lib\UAS Videos\.uas-sort";
    private const uint CloudOnlyLibrary = 0x401620;          // RecallOnDataAccess | Offline | … (Ref §13)
    private static readonly DateTime T0 = new(2026, 9, 27, 18, 2, 57, DateTimeKind.Utc);
    private static readonly CardIdentity Card = new(0x1A2B3C4D, "DJI", "exFAT", 256_060_514_304);

    private sealed class NoVolumes : IVolumeProvider { public IReadOnlyList<VolumeInfo> GetVolumes() => []; }

    private sealed class ListProgress : IProgress<ScanProgress>
    {
        public List<ScanPhase> Phases { get; } = [];
        public void Report(ScanProgress value) { lock (Phases) Phases.Add(value.Phase); }
    }

    private static Settings SettingsFor() => new(1, VideoRoot, PhotoRoot, [], 50, 1, StoredClockMode.Zone, "America/New_York", true,
        new MapSettings("streets", "", "", "", ImmutableDictionary<string, string>.Empty), new LayoutSettings(420, 0.55), true);

    private static FakeFileSystem Fixture(bool cloudOnlyLedgerB)
    {
        var ctx = new GuardContext(VideoRoot, PhotoRoot, [], @"E:\", @"C:\appdata\uas-sort", "PC1",
                                   new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), @"C:\", false, null);
        var fs = new FakeFileSystem(ctx);
        fs.AddCardVolume(@"E:\", Card, FakeLayout.CardSpace);
        foreach (var n in new[] { "DJI_20260927140127_0123_D.MP4", "DJI_20260927140144_0124_D.MP4" })
        {
            fs.AddFile($@"E:\DCIM\DJI_001\{n}", new byte[4096], T0);                                              // probe errors are fine
            fs.AddFile($@"{VideoRoot}\2026\2026-09\2026-09-27 Zachar Bay\{n}", new byte[4096], T0, CloudOnlyLibrary);  // must never be opened
        }
        fs.AddFile($@"{PhotoRoot}\DJI_20260927141000_0125_D.DNG", new byte[16], T0, CloudOnlyLibrary);
        fs.AddFile($@"{Ledger}\ledger-A.jsonl", [], T0);                                                          // local, readable
        if (cloudOnlyLedgerB) fs.AddFile($@"{Ledger}\ledger-B.jsonl", [], T0, 0x400000);
        fs.AddFile($@"{Ledger}\notes.txt", [1, 2, 3], T0);
        fs.AddFile($@"{Ledger}\sub\ledger-C.jsonl", [], T0);
        fs.AddFile($@"{Ledger}\DJI_20260927150000_0999_D.MP4", new byte[16], T0);                              // stray media: never indexed
        return fs;
    }

    private static ScanService Service(FakeFileSystem fs) =>
        new(SettingsFor(), fs, new FakeLedgerStore(fs, VideoRoot, "PC1"), new NoVolumes(), new GeoTimeZoneResolver(),
            TimeZoneInfo.FindSystemTimeZoneById("America/Anchorage"), new FakeTimeProvider(new DateTimeOffset(T0.AddDays(7))));

    /// <summary>Every path the scan opened (any mode): the guarded ReadData / AppendOwnLedger calls of the fake file system.</summary>
    private static List<string> Opened(FakeFileSystem fs) =>
        [.. fs.GuardLog.Where(c => c.Op is IoOp.ReadData or IoOp.AppendOwnLedger).Select(c => c.Path)];

    [Fact] // fake-FS hydration tripwire, scan + plan half (Ref §13)
    public async Task ScanAndPlan_OpenNoLibraryFile_OnlyTheLocalLedger()
    {
        var fs = Fixture(cloudOnlyLedgerB: false);
        var progress = new ListProgress();
        var scan = await Service(fs).ScanAsync(new CardSource(@"E:\", Card, false, false), new FakeCardReaderFactory(fs), progress,
                                               TestContext.Current.CancellationToken);
        var plan = PlanScenario.Derive(PlanScenario.CreatePlanner().Prepare(scan));
        var opened = Opened(fs);

        Assert.Equal(2, scan.Raw.Length);
        Assert.Contains(@$"{Ledger}\ledger-A.jsonl", opened, StringComparer.OrdinalIgnoreCase);
        Assert.All(opened, path => Assert.True(
            path.StartsWith(@"E:\", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, $@"{Ledger}\ledger-A.jsonl", StringComparison.OrdinalIgnoreCase), $"opened {path}"));
        Assert.DoesNotContain(opened, p => p.EndsWith("notes.txt", StringComparison.OrdinalIgnoreCase) || p.Contains(@"\sub\", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(fs.HydrationViolations);
        Assert.Empty(fs.CardDeleteViolations);
        Assert.Empty(scan.Library.Match(PlanKeys.Key("DJI_20260927150000_0999_D.MP4", 16)));    // .uas-sort is not indexed
        Assert.Single(scan.Library.Match(PlanKeys.Key("DJI_20260927140127_0123_D.MP4", 4096)));
        Assert.All(plan.Groups, g => Assert.IsType<AlreadyImported>(g.Target));
        Assert.Contains(ScanPhase.ListingCard, progress.Phases);
        Assert.Contains(ScanPhase.ReadingLedger, progress.Phases);
    }

    [Fact] // a cloud-only ledger-B opens nothing and gives Blocking CloudOnly
    public async Task CloudOnlyLedger_OpensNoLedgerFile_BlockingIssue()
    {
        var fs = Fixture(cloudOnlyLedgerB: true);
        var scan = await Service(fs).ScanAsync(new CardSource(@"E:\", Card, false, false), new FakeCardReaderFactory(fs), new ListProgress(),
                                               TestContext.Current.CancellationToken);
        Assert.Equal(LedgerFolderState.CloudOnly, scan.Ledger.Status.State);
        Assert.DoesNotContain(Opened(fs), p => p.StartsWith(Ledger, StringComparison.OrdinalIgnoreCase));
        var plan = PlanScenario.Derive(PlanScenario.CreatePlanner().Prepare(scan));
        Assert.Equal(IssueSeverity.Blocking, Assert.Single(plan.Issues, i => i.Code == IssueCode.LedgerCloudOnly).Severity);
        Assert.Empty(fs.CardDeleteViolations);
    }

    [Fact] // F2: a .uas-sort folder whose listing fails is never "no history": Blocking LedgerUnlistable, nothing loaded
    public async Task UnlistableLedgerFolder_IsBlocking_NeverANoHistoryPlan()
    {
        var fs = Fixture(cloudOnlyLedgerB: false);
        fs.Faults.EnumerationErrors[Ledger] = 362;
        var scan = await Service(fs).ScanAsync(new CardSource(@"E:\", Card, false, false), new FakeCardReaderFactory(fs), new ListProgress(),
                                               TestContext.Current.CancellationToken);
        Assert.Equal(LedgerFolderState.Unlistable, scan.Ledger.Status.State);
        Assert.DoesNotContain(Opened(fs), p => p.StartsWith(Ledger, StringComparison.OrdinalIgnoreCase));
        var plan = PlanScenario.Derive(PlanScenario.CreatePlanner().Prepare(scan));
        Assert.Equal(IssueSeverity.Blocking, Assert.Single(plan.Issues, i => i.Code == IssueCode.LedgerUnlistable).Severity);
        Assert.DoesNotContain(plan.Issues, i => i.Code == IssueCode.LedgerNoHistory);
    }
}
