namespace UasSort.Platform.Tests;

public sealed class LedgerStoreWriteTests
{
    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 9, 27, 21, 0, 0, TimeSpan.Zero));

    private static LedgerStore Store(TestEnv env, Settings? settings = null, TimeProvider? clock = null)
        => new(settings ?? env.Settings, env.AppData, TestEnv.Machine, env.Facts, new WindowsDirectoryLister(), clock ?? Clock);

    private static bool Pinned(string path) => ((uint)File.GetAttributes(path) & 0x80000) != 0;

    [Fact]
    public void EnsureFolder_CreatesTheFolderOnly_AndPinsIt()
    {
        using var env = new TestEnv();
        var store = Store(env);
        store.EnsureFolder();
        var folder = Path.Join(env.VideoRoot, ".uas-sort");
        Assert.True(Directory.Exists(folder));
        Assert.Empty(Directory.EnumerateFileSystemEntries(folder));
        Assert.True(Pinned(folder));
        Assert.Equal([folder], Directory.EnumerateFileSystemEntries(env.VideoRoot).ToArray());
    }

    [Fact]
    public void EnsureFolder_OnAnExistingUnpinnedFolder_OnlyPins_AndKeepOnDevicePinsNothingElse()
    {
        using var env = new TestEnv();
        var b = env.Temp.File(@"video\.uas-sort\ledger-B.jsonl", 64);
        var before = File.ReadAllBytes(b);
        Store(env).EnsureFolder();
        Store(env).KeepOnDevice();
        Assert.True(Pinned(Path.Join(env.VideoRoot, ".uas-sort")));
        Assert.False(Pinned(b));
        Assert.False(Pinned(env.VideoRoot));
        Assert.Equal(before, File.ReadAllBytes(b));
    }

    [Fact]
    public void OpenOwn_AppendsFlushedLines_Mirrors_AndIsTheSingleWriter()
    {
        using var env = new TestEnv();
        var store = Store(env);
        store.EnsureFolder();
        using (var w = store.OpenOwn())
        {
            w.Append(LedgerRecords.Run("TESTPC", "r1"));
            w.Append(LedgerRecords.Run("TESTPC", "r2"));
            Assert.ThrowsAny<IOException>(() => new FileStream(store.OwnFile, FileMode.Open, FileAccess.Write, FileShare.ReadWrite));
            using var reader = new FileStream(store.OwnFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }
        var own = File.ReadAllBytes(store.OwnFile);
        Assert.Equal((byte)'\n', own[^1]);
        Assert.Equal(2, own.Count(b => b == (byte)'\n'));
        Assert.Equal(own, File.ReadAllBytes(Path.Join(store.BackupDir, "ledger-TESTPC.jsonl")));
        Assert.StartsWith(Path.Join(env.AppData, "ledger-backup"), store.BackupDir, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OpenOwn_RepairsATornTail_WithATornRecord()
    {
        using var env = new TestEnv();
        var store = Store(env);
        store.EnsureFolder();
        File.WriteAllText(store.OwnFile, LedgerRecords.Line(LedgerRecords.Run("TESTPC", "r1")) + "\n"
                                         + LedgerRecords.Line(LedgerRecords.Run("TESTPC", "r2")) + "\n" + "{\"t\":\"file\",\"v\":1,\"id\":\"ab");
        using (var w = store.OpenOwn()) w.Append(LedgerRecords.Run("TESTPC", "r3"));
        var lines = File.ReadAllText(store.OwnFile).Split('\n');
        Assert.Equal(6, lines.Length);                       // 5 lines + the empty string after the final \n
        Assert.StartsWith("{\"t\":\"file\"", lines[2], StringComparison.Ordinal);
        Assert.Contains("\"t\":\"torn\"", lines[3], StringComparison.Ordinal);
        Assert.Contains("\"line\":3", lines[3], StringComparison.Ordinal);
        var snapshot = store.Load();
        Assert.Equal(3, snapshot.Runs.Length);
        Assert.Empty(snapshot.ParseIssues);
    }

    [Fact]
    public void OpenOwn_OnACloudOnlyOwnFile_IsCloudOnly()
    {
        using var env = new TestEnv();
        env.Temp.File(@"video\.uas-sort\ledger-TESTPC.jsonl", attributes: FileAttributes.Offline);
        Assert.Throws<CloudOnlyFileException>(() => Store(env).OpenOwn());
    }

    [Fact]
    public void SnapshotToBackup_CopiesLoadedFiles_AndKeepsTwenty()
    {
        using var env = new TestEnv();
        LedgerRecords.Write(Path.Join(env.VideoRoot, ".uas-sort", "ledger-B.jsonl"), LedgerRecords.Run("B", "rb"));
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 27, 21, 0, 0, TimeSpan.Zero));
        var store = Store(env, clock: clock);
        store.Load();
        for (var i = 0; i < 22; i++)
        {
            store.SnapshotToBackup($"{i:D2}aaaaaa-bbbb");
            clock.Advance(TimeSpan.FromSeconds(1));
        }
        var snapshots = Directory.GetDirectories(Path.Join(store.BackupDir, "snapshots")).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(20, snapshots.Length);
        Assert.EndsWith("20260927-210021-21aaaaaa", snapshots[^1], StringComparison.Ordinal);
        Assert.Equal(File.ReadAllBytes(Path.Join(env.VideoRoot, ".uas-sort", "ledger-B.jsonl")),
                     File.ReadAllBytes(Path.Join(snapshots[^1], "ledger-B.jsonl")));
    }

    [Fact]
    public void CopyInto_AppendsTheUnionKeepingIds_SkipsTorn_AndIsIdempotent()
    {
        using var env = new TestEnv();
        var oldFolder = Path.Join(env.VideoRoot, ".uas-sort");
        LedgerRecords.Write(Path.Join(oldFolder, "ledger-B.jsonl"), LedgerRecords.Run("B", "b1"), LedgerRecords.Run("B", "b2"));
        var old = Store(env);
        old.EnsureFolder();
        using (var w = old.OpenOwn()) w.Append(LedgerRecords.Run("TESTPC", "t1"));
        File.AppendAllText(old.OwnFile, "{\"t\":\"run\"");                  // torn tail …
        using (old.OpenOwn()) { }                                              // … repaired with a torn record
        var current = old.Load();
        var newRoot = env.Temp.Sub("video2");
        old.CopyInto(newRoot, current);
        old.CopyInto(newRoot, current);
        var newStore = Store(env, env.Settings with { VideoRoot = newRoot });
        var lines = File.ReadAllLines(newStore.OwnFile);
        Assert.Equal(3, lines.Length);
        Assert.DoesNotContain(lines, l => l.Contains("\"t\":\"torn\"", StringComparison.Ordinal));
        Assert.Equal(["b1", "b2", "t1"], newStore.Load().Runs.Select(r => r.Run).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(current.Runs.Select(r => r.Machine).Order(), newStore.Load().Runs.Select(r => r.Machine).Order());
        Assert.True(Pinned(Path.Join(newRoot, ".uas-sort")));
        Assert.Equal(3, File.ReadAllLines(Path.Join(newStore.BackupDir, "ledger-TESTPC.jsonl")).Length);
    }

    [Fact] // F8: the app calls CopyInto on the NEW root's store (the new root is saved first, Ref §9.14) with the old root's snapshot
    public void CopyInto_OnTheNewRootsStore_ReadsTheOldRootsLedgers()
    {
        using var env = new TestEnv();
        LedgerRecords.Write(Path.Join(env.VideoRoot, ".uas-sort", "ledger-B.jsonl"), LedgerRecords.Run("B", "b1"));
        var old = Store(env);
        old.EnsureFolder();
        using (var w = old.OpenOwn()) w.Append(LedgerRecords.Run("TESTPC", "t1"));
        var current = old.Load();
        var newRoot = env.Temp.Sub("video2");
        var newStore = Store(env, env.Settings with { VideoRoot = newRoot });

        newStore.CopyInto(newRoot, current);

        Assert.Equal(["b1", "t1"], newStore.Load().Runs.Select(r => r.Run).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(2, File.ReadAllLines(newStore.OwnFile).Length);
    }

    [Fact] // F8: every source is read and cleared before the new own file is created, so a refusal leaves nothing behind
    public void CopyInto_ARefusedSource_CreatesNoOwnFile()
    {
        using var env = new TestEnv();
        var old = Store(env);
        old.EnsureFolder();
        using (var w = old.OpenOwn()) w.Append(LedgerRecords.Run("TESTPC", "t1"));
        var stray = env.Temp.File(@"elsewhere\ledger-X.jsonl");                  // outside every root: the guard refuses it
        var current = old.Load() is var s ? s with { SourceFiles = [.. s.SourceFiles, stray] } : null!;
        var newRoot = env.Temp.Sub("video2");
        var newStore = Store(env, env.Settings with { VideoRoot = newRoot });

        Assert.Throws<UnsafeIoException>(() => newStore.CopyInto(newRoot, current));

        Assert.False(File.Exists(newStore.OwnFile));
        Assert.False(Directory.Exists(Path.Join(newRoot, ".uas-sort")));
        Assert.Equal(LedgerFolderState.Missing, newStore.Check().State);          // the "No history found" prompt can come back
    }

    [Fact]
    public void LoadFromBackup_IsLatestSnapshotUnionMirror()
    {
        using var env = new TestEnv();
        LedgerRecords.Write(Path.Join(env.VideoRoot, ".uas-sort", "ledger-B.jsonl"), LedgerRecords.Run("B", "b1"));
        var store = Store(env);
        store.EnsureFolder();
        store.Load();
        store.SnapshotToBackup("run00001");
        using (var w = store.OpenOwn()) w.Append(LedgerRecords.Run("TESTPC", "after-snapshot"));
        var backup = Store(env).LoadFromBackup();
        Assert.Equal(["after-snapshot", "b1"], backup.Runs.Select(r => r.Run).Order(StringComparer.Ordinal).ToArray());
        Assert.All(backup.SourceFiles, f => Assert.StartsWith(env.AppData, f, StringComparison.OrdinalIgnoreCase));
    }
}
