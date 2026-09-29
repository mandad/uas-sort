namespace UasSort.Platform.Tests;

public sealed class IoGateTests
{
    [Fact]
    public void AppDataFile_IsAllowed_AndReturnsAttributes()
    {
        using var env = new TestEnv();
        var f = env.Temp.File(@"appdata\settings.json");
        Assert.NotNull(IoGate.Require(IoOp.ReadData, env.C(f), env.Context()));
    }

    [Fact]
    public void PreExistingLibraryFile_ReadIsRefused()
    {
        using var env = new TestEnv();
        var f = env.Temp.File(@"video\2026\2026-09\2026-09-27 Zachar Bay\DJI_0001.MP4");
        Assert.Throws<UnsafeIoException>(() => IoGate.Require(IoOp.ReadData, env.C(f), env.Context()));
    }

    [Fact]
    public void OfflineLedgerFile_IsCloudOnly_NotAViolation()
    {
        using var env = new TestEnv();
        var f = env.Temp.File(@"video\.uas-sort\ledger-B.jsonl", attributes: FileAttributes.Offline);
        var ex = Assert.Throws<CloudOnlyFileException>(() => IoGate.Require(IoOp.ReadData, env.C(f), env.Context()));
        Assert.Equal(env.C(f), ex.Path, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void OfflineFileAnywhereElse_IsRefusedAsHydration()
    {
        using var env = new TestEnv();
        var f = env.Temp.File(@"appdata\drafts\x.json", attributes: FileAttributes.Offline);
        var ex = Assert.Throws<UnsafeIoException>(() => IoGate.Require(IoOp.ReadData, env.C(f), env.Context()));
        Assert.Contains("placeholder", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LookAlikeLedgerFolder_IsRefused()
    {
        using var env = new TestEnv();
        var f = env.Temp.File(@"video\.uas-sort2\ledger-A.jsonl");
        Assert.Throws<UnsafeIoException>(() => IoGate.Require(IoOp.ReadData, env.C(f), env.Context()));
    }

    [Fact]
    public void JunctionIntoLedgerFolder_GetsTheSameRules()
    {
        using var env = new TestEnv();
        env.Temp.File(@"video\.uas-sort\ledger-A.jsonl");
        env.Temp.File(@"video\.uas-sort\notes.txt");
        var link = Path.Join(env.Temp.Path, "link");
        Assert.Equal(0, Cmd.Run($"mklink /J \"{link}\" \"{Path.Join(env.VideoRoot, ".uas-sort")}\""));
        Assert.NotNull(IoGate.Require(IoOp.ReadData, env.C(Path.Join(link, "ledger-A.jsonl")), env.Context()));
        Assert.Throws<UnsafeIoException>(() => IoGate.Require(IoOp.ReadData, env.C(Path.Join(link, "notes.txt")), env.Context()));
    }

    [Fact]
    public void UnreadableAttributes_AreRefusedBeforeThePolicy()
    {
        using var env = new TestEnv();
        Assert.Throws<UnsafeIoException>(() => PlaceholderGuard.ReadAttributes(Path.Join(env.AppData, "bad|name")));
        Assert.Null(PlaceholderGuard.ReadAttributes(Path.Join(env.AppData, "missing.json")));
    }

    [Fact]
    public void GuardContexts_AreCanonicalAndUnverified()
    {
        using var env = new TestEnv();
        var ctx = env.Context(env.CardRoot);
        Assert.Equal(env.C(env.VideoRoot), ctx.VideoRoot);
        Assert.Equal(env.C(env.CardRoot), ctx.CardRoot);
        Assert.False(ctx.CardIsVerifiedCardVolume);
        Assert.Null(ctx.Cleanup);
        Assert.Equal(TestEnv.Machine, ctx.Machine);
    }
}
