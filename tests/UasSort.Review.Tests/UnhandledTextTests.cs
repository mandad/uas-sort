// tests/UasSort.Review.Tests/UnhandledTextTests.cs
namespace UasSort.Review.Tests;

public class UnhandledTextTests
{
    private static LedgerFile File(string name, string run)
    {
        var key = new FileKey(name.ToLowerInvariant(), 1_000);
        return new LedgerFile(key, "DCIM/DJI_001/" + name, DestRoot.Video, TestPlans.VideoRoot + @"\2026\2026-09\2026-09-27 Zachar Bay\" + name,
                              null, VerifyKind.Unbuffered, TestPlans.Utc(2026, 9, 27, 20, 0), null, null, null, null, null, null, "PC1", run);
    }

    private static LedgerSnapshot Ledger(params LedgerFile[] files)
        => TestPlans.Ledger() with { Files = files.ToImmutableDictionary(f => f.Key) };

    [Fact] // F16 (Ref §12 Unhandled exception: "dialog listing what this run copied (from the ledger)")
    public void CopiedThisRun_ListsOnlyThisRunsFiles_InNameOrder()
    {
        var ledger = Ledger(File("DJI_0002.MP4", "run-1"), File("DJI_0001.MP4", "run-1"), File("DJI_0009.MP4", "run-0"));
        Assert.Equal(["DJI_0001.MP4", "DJI_0002.MP4"], UnhandledText.CopiedThisRun(ledger, "run-1"));
        Assert.Empty(UnhandledText.CopiedThisRun(ledger, "run-7"));
    }

    [Fact]
    public void Body_ListsWhatThisRunCopied_CappedWithMore()
    {
        var names = Enumerable.Range(1, 12).Select(i => $"DJI_{i:0000}.MP4").ToList();
        var body = UnhandledText.Body("boom", names);
        Assert.StartsWith("uas-sort hit an unexpected error: boom", body, StringComparison.Ordinal);
        Assert.EndsWith("Copied this run: 12 files: DJI_0001.MP4, DJI_0002.MP4, DJI_0003.MP4, DJI_0004.MP4, DJI_0005.MP4, "
                        + "DJI_0006.MP4, DJI_0007.MP4, DJI_0008.MP4, DJI_0009.MP4, DJI_0010.MP4 +2 more", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Body_WithoutARun_SaysNothingAboutCopies()
    {
        var body = UnhandledText.Body("boom", null);
        Assert.DoesNotContain("Copied this run", body, StringComparison.Ordinal);
        Assert.Contains("Restart uas-sort to continue.", body, StringComparison.Ordinal);
        Assert.EndsWith("Copied this run: nothing yet", UnhandledText.Body("boom", []), StringComparison.Ordinal);
    }
}