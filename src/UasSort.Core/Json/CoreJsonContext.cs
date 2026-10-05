// src/UasSort.Core/Json/CoreJsonContext.cs
using System.Text.Json.Serialization;

namespace UasSort.Core.Json;

/// <summary>Settings, drafts, reports and the closed edit types (Ref §3 JSON, §11).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true,
    WriteIndented = true, AllowOutOfOrderMetadataProperties = true)]
[JsonSerializable(typeof(Settings))]
[JsonSerializable(typeof(Draft))]
[JsonSerializable(typeof(PlanEdit))]
[JsonSerializable(typeof(TargetChoice))]
[JsonSerializable(typeof(OffloadReport))]
[JsonSerializable(typeof(CleanupReport))]
[JsonSerializable(typeof(PhotoCleanupReport))]
[JsonSerializable(typeof(GeoPoint))]
public sealed partial class CoreJsonContext : JsonSerializerContext
{
}
