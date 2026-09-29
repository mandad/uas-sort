// tests/UasSort.Core.Tests/Planning/DeriveBenchmarkTests.cs
using System.Diagnostics;
using System.Reflection;
using UasSort.Testing.Planning;

namespace UasSort.Core.Tests.Planning;

public sealed class DeriveBenchmarkTests
{
    private static bool IsOptimized =>
        typeof(UasSort.Core.Planning.Planner).Assembly.GetCustomAttribute<DebuggableAttribute>() is not { IsJITOptimizerDisabled: true };

    [Fact]
    public void Derive_500Items_MedianUnder50ms()
    {
        var b = BenchmarkScenario.Build().Prepare();
        Assert.Equal(500, b.Items.Length);
        var planner = PlanScenario.CreatePlanner();
        var t = new Tuning(50, 1);
        planner.Derive(b, t, [], new SessionFlags(false), 0, TestContext.Current.CancellationToken);        // warm-up (JIT)
        var times = new List<double>();
        for (var i = 1; i <= 20; i++)
        {
            var sw = Stopwatch.StartNew();
            var p = planner.Derive(b, t, [], new SessionFlags(false), i, TestContext.Current.CancellationToken);
            sw.Stop();
            Assert.Equal(12, p.Groups.Length);
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        var median = (times[9] + times[10]) / 2;
        var limit = IsOptimized ? 50 : 150;
        Assert.True(median < limit, $"median derive {median:F1} ms ≥ {limit} ms ({(IsOptimized ? "Release" : "Debug")})");
    }
}
