// tests/UasSort.Review.Tests/Fakes/Eventually.cs
namespace UasSort.Review.Tests;

/// <summary>Polls a condition (running queued UI work each time) until it holds or 5 s pass.</summary>
internal static class Eventually
{
    public static async Task TrueAsync(Func<bool> condition, FakeUiDispatcher? ui = null)
    {
        var ct = TestContext.Current.CancellationToken;
        for (var i = 0; i < 500; i++)
        {
            ui?.RunAll();
            if (condition()) return;
            await Task.Delay(10, ct);
        }
        ui?.RunAll();
        Assert.True(condition(), "condition not met within 5 s");
    }
}
