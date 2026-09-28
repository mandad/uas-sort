using System.Text.Json.Serialization;

namespace UasSort.App.SelfTest;

/// <summary>One selftest check (Part 13 contract): Status is "pass", "fail" or "notApplicable".</summary>
internal sealed record SelfTestCheck(string Name, string Status, string Detail)
{
    public static SelfTestCheck Pass(string name, string detail) => new(name, "pass", detail);

    public static SelfTestCheck Fail(string name, string detail) => new(name, "fail", detail);

    public static SelfTestCheck NotApplicable(string name, string detail) => new(name, "notApplicable", detail);
}

/// <summary>
/// selftest-result.json (Ref §13, Part 13 contract): {"ok", "firstFrameMs", "checks": [{"name", "status", "detail"}]};
/// Ok is true when no check has Status "fail". No version or kind field. Part 11 modifies this file in place.
/// </summary>
internal sealed record SelfTestResult(bool Ok, double FirstFrameMs, IReadOnlyList<SelfTestCheck> Checks);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(SelfTestResult))]
internal sealed partial class SelfTestJsonContext : JsonSerializerContext
{
}
