namespace UasSort.Core;

public enum AuditCategory { VerifiedThisRun, InLedger, ConfirmedByYou, NameSizeMatch, SkippedByRule, AssumedByRule, Unaccounted } // ascending badness
public enum VerdictLevel { Safe, SafeWithAssumptions, NotSafe }
