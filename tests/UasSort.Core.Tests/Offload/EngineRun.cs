// tests/UasSort.Core.Tests/Offload/EngineRun.cs
using Microsoft.Extensions.Time.Testing;
using UasSort.Core;
using UasSort.Core.Offload;
using UasSort.Testing;
using UasSort.Testing.Offload;

namespace UasSort.Core.Tests.Offload;

internal static class EngineRun
{
    public static FakeTimeProvider Clock() => new(new DateTimeOffset(OffloadPlanBuilder.T0.AddHours(1)));

    public static Task<OffloadResult> RunEngineAsync(this OffloadRig rig, TimeProvider? clock = null, CancellationToken? ct = null /* null = the test's own token (xUnit1051) */,
                                                     IProgress<OffloadProgress>? progress = null)
    {
        var engine = new CopyEngine(clock ?? Clock(),
            new CopyEngineOptions(FakeLayout.Machine, OffloadCompiler.Describe(rig.Plan, rig.Batch), rig.Volumes));
        var writer = rig.Ledger.OpenOwn();
        return engine.RunAsync(rig.Batch, rig.Reader, rig.Files, writer, progress ?? new ListProgress<OffloadProgress>(),
                               ct ?? TestContext.Current.CancellationToken);
    }

    public static string[] Kinds(this OffloadResult r) => [.. r.Outcomes.Select(o => o.GetType().Name)];
}
