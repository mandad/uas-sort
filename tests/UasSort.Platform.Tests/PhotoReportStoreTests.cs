// tests/UasSort.Platform.Tests/PhotoReportStoreTests.cs
using System.Text.Json;

namespace UasSort.Platform.Tests;

public sealed class PhotoReportStoreTests
{
    [Fact]
    public void ReportStore_SavesAPhotoCleanupReport_AsTimestampRun8Photos()
    {
        using var env = new TestEnv();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 4, 20, 15, 30, TimeSpan.Zero));
        var at = clock.GetUtcNow().UtcDateTime;
        var report = new PhotoCleanupReport(1, "ab12cd34ef56", env.PhotoRoot, PhotoCleanupMode.BeforeDate, new DateOnly(2026, 6, 30), null,
                                            [], [], [], null, [], at, at);
        var path = new ReportStore(env.AppData, TestEnv.Machine, env.Facts, clock).Save(report);
        Assert.Equal("20261004-201530-ab12cd34-photos.json", Path.GetFileName(path));
        Assert.Equal("ab12cd34ef56", JsonSerializer.Deserialize(File.ReadAllText(path), CoreJsonContext.Default.PhotoCleanupReport)!.RunId);
    }
}
