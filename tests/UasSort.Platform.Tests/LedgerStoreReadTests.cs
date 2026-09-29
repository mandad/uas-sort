using System.Security.AccessControl;

namespace UasSort.Platform.Tests;

public sealed class LedgerStoreReadTests
{
    internal static LedgerStore Store(TestEnv env)
        => new(env.Settings, env.AppData, TestEnv.Machine, env.Facts, new WindowsDirectoryLister(),
               new FakeTimeProvider(new DateTimeOffset(2026, 9, 27, 21, 0, 0, TimeSpan.Zero)));

    [Fact]
    public void Check_VideoRootMissing()
    {
        using var env = new TestEnv();
        Directory.Delete(env.VideoRoot);
        Assert.Equal(LedgerFolderState.VideoRootMissing, Store(env).Check().State);
    }

    [Fact]
    public void Check_MissingFolder_IsWritableAndNotPinned()
    {
        using var env = new TestEnv();
        var s = Store(env).Check();
        Assert.Equal(LedgerFolderState.Missing, s.State);
        Assert.False(s.Exists);
        Assert.True(s.Writable);
        Assert.Equal(env.C(Path.Join(env.VideoRoot, ".uas-sort")), s.Folder);
    }

    [Fact] // F2: the folder is there but can't be listed (ERROR_ACCESS_DENIED): Unlistable, never Empty, and Load opens nothing
    public void Check_FolderThatCantBeListed_IsUnlistable()
    {
        using var env = new TestEnv();
        env.Temp.File(@"video\.uas-sort\ledger-B.jsonl");
        TempDir.Deny(Path.Join(env.VideoRoot, ".uas-sort"), FileSystemRights.ListDirectory);
        var store = Store(env);
        Assert.Equal(LedgerFolderState.Unlistable, store.Check().State);
        var snapshot = store.Load();
        Assert.Empty(snapshot.SourceFiles);
        Assert.Equal(LedgerFolderState.Unlistable, snapshot.Status.State);
    }

    [Fact]
    public void Check_EmptyFolder()
    {
        using var env = new TestEnv();
        env.Temp.Sub("video", ".uas-sort");
        env.Temp.File(@"video\.uas-sort\notes.txt");
        Assert.Equal(LedgerFolderState.Empty, Store(env).Check().State);
    }

    [Fact]
    public void Check_Ok_ListsTopLevelLedgersAndOtherMachines()
    {
        using var env = new TestEnv();
        foreach (var n in new[] { "ledger-TESTPC.jsonl", "ledger-B.jsonl", "LEDGER-B-DESKTOP-A.JSONL", "notes.txt", @"sub\ledger-C.jsonl" })
            env.Temp.File(@"video\.uas-sort\" + n);
        var s = Store(env).Check();
        Assert.Equal(LedgerFolderState.Ok, s.State);
        Assert.Equal(["LEDGER-B-DESKTOP-A.JSONL", "ledger-B.jsonl", "ledger-TESTPC.jsonl"],
                     s.LedgerFiles.Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(2, s.OtherMachineFiles.Length);
        Assert.Empty(s.CloudOnlyFiles);
        Assert.False(s.InSyncRoot);
    }

    [Fact]
    public void Check_CloudOnlyLedger_IsBlockingState()
    {
        using var env = new TestEnv();
        env.Temp.File(@"video\.uas-sort\ledger-TESTPC.jsonl");
        var b = env.Temp.File(@"video\.uas-sort\ledger-B.jsonl", attributes: FileAttributes.Offline);
        var s = Store(env).Check();
        Assert.Equal(LedgerFolderState.CloudOnly, s.State);
        Assert.Equal(env.C(b), Assert.Single(s.CloudOnlyFiles), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Check_DenyAddFile_IsUnwritable()
    {
        using var env = new TestEnv();
        var folder = env.Temp.Sub("video", ".uas-sort");
        env.Temp.File(@"video\.uas-sort\ledger-B.jsonl");
        TempDir.Deny(folder, FileSystemRights.CreateFiles);
        var s = Store(env).Check();
        Assert.False(s.Writable);
        Assert.Equal(LedgerFolderState.Unwritable, s.State);
    }

    [Fact]
    public void Load_ReadsTopLevelLedgers_NeverOpensAnythingElse()
    {
        using var env = new TestEnv();
        var dir = Path.Join(env.VideoRoot, ".uas-sort");
        LedgerRecords.Write(Path.Join(dir, "ledger-TESTPC.jsonl"), LedgerRecords.Run("TESTPC", "run-a"));
        LedgerRecords.Write(Path.Join(dir, "ledger-B.jsonl"), LedgerRecords.Run("B", "run-b"));
        LedgerRecords.Write(Path.Join(dir, "sub", "ledger-C.jsonl"), LedgerRecords.Run("C", "run-c"));
        var notes = env.Temp.File(@"video\.uas-sort\notes.txt");
        using var lockNotes = new FileStream(notes, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var lockSub = new FileStream(Path.Join(dir, "sub", "ledger-C.jsonl"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var snapshot = Store(env).Load();
        Assert.Equal(["run-a", "run-b"], snapshot.Runs.Select(r => r.Run).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(LedgerFolderState.Ok, snapshot.Status.State);
        Assert.Equal(2, snapshot.SourceFiles.Length);
    }

    [Fact]
    public void Load_WithACloudOnlyLedger_OpensNoLedgerFile()
    {
        using var env = new TestEnv();
        var own = Path.Join(env.VideoRoot, ".uas-sort", "ledger-TESTPC.jsonl");
        LedgerRecords.Write(own, LedgerRecords.Run("TESTPC", "run-a"));
        env.Temp.File(@"video\.uas-sort\ledger-B.jsonl", attributes: FileAttributes.Offline);   // cloud-only: listed, never opened
        using var lockOwn = new FileStream(own, FileMode.Open, FileAccess.ReadWrite, FileShare.None);   // any open of it would throw
        var snapshot = Store(env).Load();
        Assert.Equal(LedgerFolderState.CloudOnly, snapshot.Status.State);
        Assert.Empty(snapshot.Runs);
        Assert.Empty(snapshot.SourceFiles);
    }
}
