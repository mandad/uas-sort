using System.Text.Json.Serialization;

namespace UasSort.Core.StackProof;

// Stack-proof canaries (Ref §14 step 1). They stay in Core permanently: the cross-assembly exhaustiveness test
// (Review.Tests) and the Native AOT selftest's closed-record JSON check use them.

/// <summary>A C# 15 closed hierarchy that is serialised (like PlanEdit, TargetChoice, LedgerRecord).</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "t")]
[JsonDerivedType(typeof(ProbeCopied), "copied")]
[JsonDerivedType(typeof(ProbeSkipped), "skipped")]
[JsonDerivedType(typeof(ProbeConflict), "conflict")]
public closed record class ProbeOutcome;

public sealed record class ProbeCopied(string Path, long Bytes) : ProbeOutcome;

public sealed record class ProbeSkipped(string Reason) : ProbeOutcome;

public sealed record class ProbeConflict(string Existing, long ExistingSize) : ProbeOutcome;

public sealed record ProbeFix(double Lat, double Lon);

public sealed record ProbeNoFix(string Reason);

/// <summary>A C# 15 union kept in memory only (like GpsProbe, VerifyResult, EditResult).</summary>
public union ProbeGps(ProbeFix, ProbeNoFix);
