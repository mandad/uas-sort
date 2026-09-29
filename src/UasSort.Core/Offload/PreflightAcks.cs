using System.Collections.Immutable;

namespace UasSort.Core.Offload;

/// <summary>One acknowledgement checkbox on the preflight sheet (Ref §10.2, §9.10 "Ack at preflight").</summary>
public sealed record AckKey(IssueCode Code, ItemId? Anchor, string Message);

public static class PreflightAcks
{
    public static AckKey Key(Issue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        return new AckKey(issue.Code, issue.Anchor, issue.Message);
    }

    public static ImmutableArray<AckKey> Required(PreflightReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return [.. report.Issues.Where(i => i.RequiresAckAtPreflight).Select(Key).Distinct()];
    }

    /// <summary>Start offload is enabled only with no Blocking issue and every acknowledgement ticked.</summary>
    public static bool CanStart(PreflightReport report, IReadOnlySet<AckKey> acknowledged)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(acknowledged);
        return report.CanStart && Required(report).All(acknowledged.Contains);
    }
}
