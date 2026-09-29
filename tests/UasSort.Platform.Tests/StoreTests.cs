using System.Text.Json;

namespace UasSort.Platform.Tests;

public sealed class StoreTests
{
    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 9, 27, 21, 7, 2, TimeSpan.Zero));

    private static SettingsStore Settings(TestEnv env)
        => new(env.AppData, env.Temp.Sub("pictures"), TestEnv.Machine, env.Facts, new WindowsDirectoryLister(), Clock);

    private static string[] Snapshot(string dir)
        => Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                    .Select(f => f + "|" + Convert.ToHexString(File.ReadAllBytes(f))).Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public void Settings_Missing_GivesUnconfirmedDefaults_NotRecovered_AndWritesNothing()
    {
        using var env = new TestEnv();
        var load = Settings(env).Load();
        Assert.False(load.Recovered);                       // decision 46: Recovered means an unreadable file only
        Assert.Null(load.CorruptCopyPath);
        Assert.False(load.Settings.RootsConfirmed);
        Assert.EndsWith(@"\pictures\UAS Videos", load.Settings.VideoRoot, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(env.AppData));
    }

    [Fact]
    public void Settings_SaveThenLoad_RoundTrips_AndKeepsABak()
    {
        using var env = new TestEnv();
        var store = Settings(env);
        store.Save(env.Settings);
        store.Save(env.Settings with { GapDays = 2 });
        var load = store.Load();
        Assert.False(load.Recovered);
        Assert.Equal(2, load.Settings.GapDays);
        Assert.Equal(env.Settings.VideoRoot, load.Settings.VideoRoot);
        Assert.Equal(1, SettingsCodec.Parse(File.ReadAllText(Path.Join(env.AppData, "settings.json.bak"))).Settings!.GapDays);
        Assert.False(File.Exists(Path.Join(env.AppData, "settings.json.tmp")));
    }

    [Fact]
    public void Settings_Corrupt_IsKept_DefaultsBlock_AndRootsComeFromTheLastRun()
    {
        using var env = new TestEnv();
        File.WriteAllText(Path.Join(env.AppData, "settings.json"), "{ not json");
        LedgerRecords.Write(Path.Join(env.AppData, "ledger-backup", "0123456789abcdef", "ledger-TESTPC.jsonl"),
                            LedgerRecords.Run("TESTPC", "r1", videoRoot: @"D:\Videos", photoRoot: @"D:\Photos"));
        var load = Settings(env).Load();
        Assert.True(load.Recovered);
        Assert.False(load.Settings.RootsConfirmed);
        Assert.Equal(env.C(Path.Join(env.AppData, "settings.json")) + ".corrupt-20260927-210702", load.CorruptCopyPath);
        Assert.Equal("{ not json", File.ReadAllText(load.CorruptCopyPath!));
        Assert.False(File.Exists(Path.Join(env.AppData, "settings.json")));
        Assert.Equal(new RunRoots(@"D:\Videos", @"D:\Photos"), load.RootsFromLastRun);
    }

    [Fact]
    public void Settings_Corrupt_WithNoMirrors_TakesRootsFromTheDerivedVideoRootLedger()
    {
        using var env = new TestEnv();
        var pictures = env.Temp.Sub("pictures");
        File.WriteAllText(Path.Join(env.AppData, "settings.json"), "{ not json");
        LedgerRecords.Write(Path.Join(LedgerPaths.For(SettingsDefaults.Derive(pictures).VideoRoot), "ledger-OTHERPC.jsonl"),
                            LedgerRecords.Run("OTHERPC", "r1", videoRoot: @"E:\Videos", photoRoot: @"E:\Photos"));
        Assert.False(Directory.Exists(Path.Join(env.AppData, "ledger-backup")));
        var load = Settings(env).Load();
        Assert.True(load.Recovered);
        Assert.Equal(new RunRoots(@"E:\Videos", @"E:\Photos"), load.RootsFromLastRun);
    }

    [Fact]
    public void Settings_ReadOnlyLoad_OfACorruptFile_ChangesNothing()
    {
        using var env = new TestEnv();
        File.WriteAllText(Path.Join(env.AppData, "settings.json"), "{\"schema\":7}");
        var before = Snapshot(env.AppData);
        var load = Settings(env).Load(readOnly: true);
        Assert.True(load.Recovered);
        Assert.Null(load.CorruptCopyPath);
        Assert.Equal(before, Snapshot(env.AppData));
    }

    [Fact]
    public void Settings_Placeholder_IsNeverOpened()
    {
        using var env = new TestEnv();
        env.Temp.File(@"appdata\settings.json", attributes: FileAttributes.Offline);
        Assert.Throws<UnsafeIoException>(() => Settings(env).Load());
    }

    [Fact]
    public void Drafts_SaveLoadDelete()
    {
        using var env = new TestEnv();
        var drafts = new DraftStore(env.AppData, TestEnv.Machine, env.Facts);
        var d = new Draft(1, "vol-1A2B3C4D", "9f3c0a6d12e4b7a1", new DateTime(2026, 9, 27, 21, 0, 0, DateTimeKind.Utc), new Tuning(40, 1),
                          [new SplitBefore(new ItemId("DCIM/DJI_001/DJI_20260927140627_0128_D.MP4"))]);
        Assert.Null(drafts.Load("vol-1A2B3C4D"));
        drafts.Save("vol-1A2B3C4D", d);
        var back = drafts.Load("vol-1A2B3C4D")!;
        Assert.Equal(d.InventoryHash, back.InventoryHash);
        Assert.Equal(d.Tuning, back.Tuning);
        Assert.Equal(d.Edits.Single(), back.Edits.Single());
        File.WriteAllText(Path.Join(env.AppData, "drafts", "vol-00000000.json"), "garbage");
        Assert.Null(drafts.Load("vol-00000000"));
        drafts.Delete("vol-1A2B3C4D");
        Assert.Null(drafts.Load("vol-1A2B3C4D"));
        Assert.Throws<ArgumentException>(() => drafts.Save(@"..\x", d));
    }

    [Fact]
    public void Reports_AreWrittenOnce_WithTimestampAndRun8()
    {
        using var env = new TestEnv();
        var reports = new ReportStore(env.AppData, TestEnv.Machine, env.Facts, Clock);
        var offload = new OffloadReport(1, "8f1c2d3e-aaaa-bbbb", env.Settings, "1 group", [], [], [], VerdictLevel.Safe, "Safe to format", null);
        var p1 = reports.Save(offload);
        var p2 = reports.Save(offload);
        var dir = Path.Join(env.C(env.AppData), "reports");
        Assert.Equal(Path.Join(dir, "20260927-210702-8f1c2d3e.json"), p1);
        Assert.Equal(Path.Join(dir, "20260927-210702-8f1c2d3e-2.json"), p2);
        Assert.Equal("Safe to format", JsonSerializer.Deserialize(File.ReadAllText(p1), CoreJsonContext.Default.OffloadReport)!.Headline);
        var cleanup = new CleanupReport(1, "c4e2aaaa-1111", new CardIdentity(0x1A2B3C4D, null, "exFAT", 256_060_514_304),
            new CleanupRequest(CleanupMode.BeforeDate, new DateOnly(2026, 7, 26), null, false),
            new CleanupCutoff(new DateOnly(2026, 7, 26), null, null, null, 0, 0, null), [], [], null, 1, 2, VerdictLevel.NotSafe);
        Assert.Equal(Path.Join(dir, "20260927-210702-c4e2aaaa-cleanup.json"), reports.Save(cleanup));
    }
}
