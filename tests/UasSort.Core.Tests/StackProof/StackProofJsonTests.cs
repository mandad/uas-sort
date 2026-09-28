using System.Text.Json;
using UasSort.Core.Json;
using UasSort.Core.StackProof;

namespace UasSort.Core.Tests.StackProof;

/// <summary>Ref §3 JSON rule and §14 step 1: closed records round-trip through a source-generated context.</summary>
public sealed class StackProofJsonTests
{
    private const string ExpectedJson =
        """[{"t":"copied","path":"2026/2026-09/DJI_0001.MP4","bytes":1024},{"t":"skipped","reason":"dup"},{"t":"conflict","existing":"DJI_0002.MP4","existingSize":7}]""";

    private static readonly List<ProbeOutcome> Outcomes =
    [
        new ProbeCopied("2026/2026-09/DJI_0001.MP4", 1024),
        new ProbeSkipped("dup"),
        new ProbeConflict("DJI_0002.MP4", 7),
    ];

    [Fact]
    public void ClosedRecords_SerialiseCamelCaseWithTheDiscriminatorFirst()
    {
        var json = JsonSerializer.Serialize(Outcomes, StackProofJsonContext.Default.ListProbeOutcome);
        Assert.Equal(ExpectedJson, json);
    }

    [Fact]
    public void ClosedRecords_RoundTripThroughTheSourceGeneratedContext()
    {
        var back = JsonSerializer.Deserialize(ExpectedJson, StackProofJsonContext.Default.ListProbeOutcome);
        Assert.NotNull(back);
        Assert.Equal(Outcomes, back);
    }

    [Fact]
    public void Deserialise_AcceptsTheDiscriminatorAfterOtherProperties()
    {
        var one = JsonSerializer.Deserialize("""{"reason":"dup","t":"skipped"}""", StackProofJsonContext.Default.ProbeOutcome);
        Assert.Equal(new ProbeSkipped("dup"), one);
    }

    [Fact]
    public void Deserialise_UnknownDiscriminator_IsRejected()
    {
        var ex = Record.Exception(() => JsonSerializer.Deserialize("""{"t":"moved"}""", StackProofJsonContext.Default.ProbeOutcome));
        Assert.True(ex is JsonException or NotSupportedException, "unexpected " + ex?.GetType().Name);
    }
}
