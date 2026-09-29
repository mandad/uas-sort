// tests/UasSort.Core.Tests/Config/SettingsLoadPolicyTests.cs
using UasSort.Core.Config;
using UasSort.Core.Ledger;
using UasSort.Core;
using static UasSort.Core.Tests.Ledger.LedgerLines;

namespace UasSort.Core.Tests.Config;

public sealed class SettingsLoadPolicyTests
{
    private const string SettingsPath = @"C:\Users\u\AppData\Local\uas-sort\settings.json";
    private const string Backup = @"C:\Users\u\AppData\Local\uas-sort\ledger-backup\";
    private static readonly Settings Defaults = SettingsDefaults.Derive(@"C:\Users\u\Pictures");
    private static readonly DateTime Now = new(2026, 10, 4, 20, 11, 0, DateTimeKind.Utc);

    private static RunRoots? MustNotBeCalled() => throw new InvalidOperationException("the read-only load must not look for roots");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SettingsLoad_MissingFile_GivesFirstRunDefaults(bool readOnly)
    {
        SettingsLoadDecision d = SettingsLoadPolicy.Decide(SettingsPath, null, readOnly, Defaults, Now, MustNotBeCalled);
        Assert.Equal(Defaults, d.Load.Settings);
        Assert.False(d.Load.Settings.RootsConfirmed);
        Assert.False(d.Load.Recovered);
        Assert.Null(d.Load.CorruptCopyPath);
        Assert.Null(d.MoveCorruptTo);
    }

    [Fact]
    public void SettingsLoad_ReadableFile_IsUsed()
    {
        string text = SettingsCodec.Serialize(Defaults with { RootsConfirmed = true, RadiusMiles = 40 });
        SettingsLoadDecision d = SettingsLoadPolicy.Decide(SettingsPath, text, false, Defaults, Now, MustNotBeCalled);
        Assert.Equal(40, d.Load.Settings.RadiusMiles);
        Assert.True(d.Load.Settings.RootsConfirmed);
        Assert.False(d.Load.Recovered);
        Assert.Null(d.MoveCorruptTo);
    }

    [Fact]
    public void SettingsLoad_CorruptFile_RecoversBlockingAndOffersRootsFromTheLastRun()
    {
        var roots = new RunRoots(@"D:\Vids", @"D:\Vids\Picture Offload");
        SettingsLoadDecision d = SettingsLoadPolicy.Decide(SettingsPath, "{ \"schema\": 1, \"videoRoot\": ", false, Defaults, Now, () => roots);
        Assert.True(d.Load.Recovered);
        Assert.False(d.Load.Settings.RootsConfirmed);
        Assert.Equal(Defaults.VideoRoot, d.Load.Settings.VideoRoot);
        Assert.Equal(SettingsPath + ".corrupt-20261004-201100", d.Load.CorruptCopyPath);
        Assert.Equal(d.Load.CorruptCopyPath, d.MoveCorruptTo);
        Assert.Equal(roots, d.Load.RootsFromLastRun);
    }

    [Fact]
    public void SettingsLoad_ReadOnly_CorruptFile_RenamesNothingAndLooksUpNothing()
    {
        SettingsLoadDecision d = SettingsLoadPolicy.Decide(SettingsPath, "{bad", readOnly: true, Defaults, Now, MustNotBeCalled);
        Assert.Equal(Defaults, d.Load.Settings);
        Assert.True(d.Load.Recovered);
        Assert.Null(d.Load.CorruptCopyPath);
        Assert.Null(d.Load.RootsFromLastRun);
        Assert.Null(d.MoveCorruptTo);
    }

    [Fact]
    public void SettingsRecovery_RootsFromTheLatestRunAcrossMirrors()
    {
        var mirrors = new[]
        {
            new LedgerFileText(Backup + @"0123456789abcdef\ledger-DESKTOP-A.jsonl", Text(
                Run("u1", "run-a", Utc(2026, 9, 27, 21, 0), Utc(2026, 9, 27, 21, 30), @"C:\Old\UAS Videos", @"C:\Old\UAS Videos\Picture Offload"))),
            new LedgerFileText(Backup + @"fedcba9876543210\ledger-DESKTOP-A.jsonl", "garbage\n" + Text(
                Run("u2", "run-b", Utc(2026, 10, 4, 18, 0), Utc(2026, 10, 4, 18, 45), @"D:\Vids", @"D:\Vids\Picture Offload"),
                Run("u3", "run-c", Utc(2026, 9, 1, 18, 0), Utc(2026, 9, 1, 18, 45), @"E:\Older", @"E:\Older\Pics"))),
        };
        Assert.Equal(new RunRoots(@"D:\Vids", @"D:\Vids\Picture Offload"), SettingsRecovery.RootsFromLastRun(mirrors));
    }

    [Fact]
    public void SettingsRecovery_NoRunRecords_GivesNull()
    {
        Assert.Null(SettingsRecovery.RootsFromLastRun([]));
        Assert.Null(SettingsRecovery.RootsFromLastRun(
            [new LedgerFileText(Backup + @"0123456789abcdef\ledger-DESKTOP-A.jsonl", Text(Seen("s1", "X.DNG", 1, Utc(2026, 10, 4))))]));
    }
}
