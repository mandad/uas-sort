namespace UasSort.Core;

public enum AuditCategory { VerifiedThisRun, InLedger, ConfirmedByYou, NameSizeMatch, SkippedByRule, AssumedByRule, Unaccounted } // ascending badness
public enum VerdictLevel { Safe, SafeWithAssumptions, NotSafe }
public sealed record AuditLine(string CardRelPath, long Size, AuditCategory Category, string Detail);
public sealed record UnitAudit(ItemId Unit, AuditCategory Worst, ImmutableArray<AuditLine> Lines);
public sealed record FormatVerdict(VerdictLevel Level, CardIdentity Card, string Headline,
                                   ImmutableDictionary<AuditCategory, int> Counts, int NameSizeOnly, int CachedVerifies,
                                   ImmutableArray<UnitAudit> Units, ImmutableArray<string> CardChanges, string? SafeRemovalNote);
