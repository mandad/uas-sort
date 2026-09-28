// src/UasSort.Core/Json/LedgerJsonContext.cs
using System.Text.Json.Serialization;

namespace UasSort.Core.Json;

/// <summary>Ledger lines (Ref §11): one compact JSON object per line, "t" discriminator, camelCase, enums as names.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true,
    WriteIndented = false, AllowOutOfOrderMetadataProperties = true)]
[JsonSerializable(typeof(LedgerRecord))]
public sealed partial class LedgerJsonContext : JsonSerializerContext
{
}
