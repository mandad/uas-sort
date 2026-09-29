using System.Collections.Immutable;

namespace UasSort.Core.Offload;

/// <summary>Ref §10.5: the "safe to format?" verdict for the card in the reader. Listing only; opens nothing.</summary>
public static class CardAudit
{
    private static readonly string[] ChangePrefixes = ["added: ", "removed: ", "changed: "];

    public static FormatVerdict Audit(CardInventory inventory, ListingResult relisted, CardIdentity? now, Plan plan,
                                      OffloadResult? offload, LedgerSnapshot ledger, CardIdentity? pinned = null)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(relisted);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(ledger);
        var expected = pinned ?? inventory.Source.Identity;
        bool identityChanged = now is null || (expected is not null && now != expected);
        var card = expected ?? now ?? new CardIdentity(0, null, "", 0);

        var diff = CardDiff.Compare(inventory.Entries, relisted);
        var audit = AuditCategorizer.Categorize(inventory, plan, offload, ledger, diff);
        var counts = Enum.GetValues<AuditCategory>().ToImmutableDictionary(c => c, c => audit.Units.Count(u => u.Worst == c));
        int forces = inventory.Warnings.Concat(plan.Base.Scan.Warnings).Distinct().Count(w => w.ForcesNotSafe);
        bool ledgerAccepted = plan.Issues.Any(i => i.Code == IssueCode.LedgerParseIssue && i.Severity != IssueSeverity.Blocking);

        var level = identityChanged || forces > 0 || counts[AuditCategory.Unaccounted] > 0 ? VerdictLevel.NotSafe
                  : counts[AuditCategory.AssumedByRule] > 0 || audit.TruncatedAssumed > 0 || ledgerAccepted ? VerdictLevel.SafeWithAssumptions
                  : VerdictLevel.Safe;

        var volumes = offload?.VolumesNeedingSafeRemoval ?? [];
        var note = volumes.IsEmpty ? null
                 : $"Safely remove {string.Join(" and ", volumes.Select(OffloadPaths.DriveLabel))} before formatting the card";
        var headline = VerdictText.Headline(level, inventory, card, identityChanged, forces, ledgerAccepted, counts, audit);
        return new FormatVerdict(level, card, headline, counts, counts[AuditCategory.NameSizeMatch], audit.CachedVerifies, audit.Units,
                                 CardChanges(diff), note);
    }

    private static ImmutableArray<string> CardChanges(CardDiffResult diff)
    {
        var lines = new List<string>();
        lines.AddRange(diff.Added.Select(e => "added: " + e.RelPath));
        lines.AddRange(diff.Removed.Select(e => "removed: " + e.RelPath));
        lines.AddRange(diff.Changed.Select(e => "changed: " + e.RelPath));
        if (diff.LastAccessOnly > 0) lines.Add($"OS updated last-access times on {diff.LastAccessOnly} files (not counted)");
        return [.. lines];
    }

    public static ImmutableArray<string> ChangedPaths(FormatVerdict verdict)
    {
        ArgumentNullException.ThrowIfNull(verdict);
        return [.. verdict.CardChanges
            .Select(c => ChangePrefixes.FirstOrDefault(prefix => c.StartsWith(prefix, StringComparison.Ordinal)) is { } hit ? c[hit.Length..] : null)
            .OfType<string>()];
    }
}
