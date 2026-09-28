using System.Text.Json.Serialization;
using UasSort.Core.StackProof;

namespace UasSort.Core.Json;

/// <summary>Source-generated context for the stack-proof canaries (Ref §3: source-generated contexts only).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, AllowOutOfOrderMetadataProperties = true)]
[JsonSerializable(typeof(ProbeOutcome))]
[JsonSerializable(typeof(List<ProbeOutcome>))]
public sealed partial class StackProofJsonContext : JsonSerializerContext
{
}
