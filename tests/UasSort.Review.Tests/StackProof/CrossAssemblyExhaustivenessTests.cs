using System.Globalization;
using UasSort.Core.StackProof;

namespace UasSort.Review.Tests.StackProof;

/// <summary>
/// Ref §2.1 / §14 step 1: C# 15 exhaustiveness across assemblies. The switches below have NO default arm; they
/// compile only because the compiler treats Core's closed hierarchy and union as complete from another assembly.
/// If Core adds a case, this assembly stops compiling (CS8509 is an error under TreatWarningsAsErrors).
/// </summary>
public sealed class CrossAssemblyExhaustivenessTests
{
    private static readonly string[] ExpectedOutcomeCases = ["ProbeConflict", "ProbeCopied", "ProbeSkipped"];

    [Fact]
    public void ClosedHierarchyFromCore_SwitchWithoutDefaultArm_HandlesEveryCase()
    {
        Assert.Equal("copied DJI_0001.MP4 (1024 bytes)", Describe(new ProbeCopied("DJI_0001.MP4", 1024)));
        Assert.Equal("skipped: dup", Describe(new ProbeSkipped("dup")));
        Assert.Equal("conflict with DJI_0002.MP4 (7 bytes)", Describe(new ProbeConflict("DJI_0002.MP4", 7)));
    }

    [Fact]
    public void UnionFromCore_SwitchWithoutDefaultArm_HandlesEveryCase()
    {
        Assert.Equal("57.5368,-153.7484", Describe(new ProbeFix(57.5368, -153.7484)));
        Assert.Equal("no fix: NoDjmdTrack", Describe(new ProbeNoFix("NoDjmdTrack")));
    }

    [Fact]
    public void CoreDeclaresExactlyTheCasesThisSwitchCovers()
    {
        var cases = typeof(ProbeOutcome).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(ProbeOutcome)))
            .Select(t => t.Name)
            .Order(StringComparer.Ordinal);
        Assert.Equal(ExpectedOutcomeCases, cases);
    }

    private static string Describe(ProbeOutcome outcome) => outcome switch
    {
        ProbeCopied c => string.Create(CultureInfo.InvariantCulture, $"copied {c.Path} ({c.Bytes} bytes)"),
        ProbeSkipped s => "skipped: " + s.Reason,
        ProbeConflict k => string.Create(CultureInfo.InvariantCulture, $"conflict with {k.Existing} ({k.ExistingSize} bytes)"),
    };

    private static string Describe(ProbeGps gps) => gps switch
    {
        ProbeFix f => string.Create(CultureInfo.InvariantCulture, $"{f.Lat:F4},{f.Lon:F4}"),
        ProbeNoFix n => "no fix: " + n.Reason,
    };
}
