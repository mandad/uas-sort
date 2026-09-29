// tests/UasSort.Core.Tests/Testing/CleanupFakeAdditionsTests.cs
namespace UasSort.Core.Tests.Testing;

public class CleanupFakeAdditionsTests
{
    private static readonly DateTime T = new(2026, 7, 20, 20, 1, 30, DateTimeKind.Utc);
    private const string Clip = "DCIM/DJI_001/DJI_20260720120000_0101_D.MP4";
    private static readonly string ClipFull = PathRules.Join(FakeLayout.CardRoot, Clip);
    private static readonly CardSource Detected = new(FakeLayout.CardRoot, FakeLayout.CardId, false, false);

    [Fact]
    public void Touch_changes_size_and_mtime_in_place()
    {
        var fs = FakeLayout.NewFileSystem();
        fs.AddFile(ClipFull, 1_000, T);
        fs.Touch(ClipFull, mtimeUtc: T.AddMinutes(1));
        Assert.Equal((1_000L, T.AddMinutes(1)), (fs.Metadata(ClipFull)!.Size, fs.Metadata(ClipFull)!.MtimeUtc));
        fs.Touch(ClipFull, size: 2_000);
        Assert.Equal((2_000L, T.AddMinutes(1)), (fs.Metadata(ClipFull)!.Size, fs.Metadata(ClipFull)!.MtimeUtc));
    }

    [Fact]
    public void OnCardDelete_runs_after_the_guard_and_before_the_delete_and_AllDeleted_spans_every_eraser()
    {
        var fs = FakeLayout.NewFileSystem();
        fs.AddFile(ClipFull, 1_000, T);
        fs.AddFile(@"E:\DCIM\DJI_001\other.MP4", 1_000, T);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 12, 19, 30, 0, TimeSpan.Zero));
        var plan = CleanupPlanFixtures.Confirmed(FakeLayout.CardRoot, FakeLayout.CardId, [Clip], [], clock);
        var factory = new FakeCardEraserFactory(fs);
        var seen = new List<(string Path, bool StillOnCard)>();
        fs.Faults.OnCardDelete = p => seen.Add((p, fs.Exists(PathRules.Join(FakeLayout.CardRoot, p))));

        using (var eraser = factory.Open(Detected, FakeLayout.CardId, plan))
            Assert.True(eraser.DeleteFile(Clip) is EraseOk);
        Assert.Equal(Clip, Assert.Single(seen).Path);
        Assert.True(seen[0].StillOnCard);                                     // called before the delete applies
        Assert.Equal(new[] { Clip }, factory.AllDeleted);

        using (var raw = factory.OpenUnchecked(FakeLayout.Context()))           // no plan: the guard refuses first
            Assert.Throws<UnsafeIoException>(() => raw.DeleteFile("DCIM/DJI_001/other.MP4"));
        Assert.Single(seen);                                                  // a refused delete never reaches the hook
        Assert.Equal(1, factory.OpenCount);
        Assert.Single(fs.CardDeleteViolations);
    }

    [Fact]
    public void Disposer_runs_its_action_once()
    {
        var n = 0;
        var d = new Disposer(() => n++);
        d.Dispose();
        d.Dispose();
        Assert.Equal(1, n);
    }
}
