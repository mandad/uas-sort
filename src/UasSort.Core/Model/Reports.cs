namespace UasSort.Core;

public sealed record ReportLine(string CardRelPath, string? Dest, string Outcome, CopyPhase? Phase, string? Error, string? Xxh128, string? Verify);
public sealed record OffloadReport(int V, string RunId, Settings SettingsSnapshot, string PlanSummary, ImmutableArray<ReportLine> Files,
                                   ImmutableArray<AuditLine> Audit, ImmutableArray<string> CardChanges, VerdictLevel Verdict,
                                   string Headline, StopReason? Stop);
public sealed record CleanupReportLine(string CardRelPath, long Size, string Unit, string Outcome, string Eligibility, string Evidence,
                                       string Reason, int? Win32Error, bool LedgerRecorded);
public sealed record CleanupReport(int V, string RunId, CardIdentity Card, CleanupRequest Request, CleanupCutoff Cutoff,
                                   ImmutableArray<CleanupReportLine> Files, ImmutableArray<CleanupKept> NotDeletable, CleanupStop? Stop,
                                   long FreeBefore, long FreeAfter, VerdictLevel VerdictAfter /* NotSafe if the rescan failed */);
