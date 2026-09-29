using System.Globalization;

namespace UasSort.Core.Offload;

/// <summary>The verdict's wording (Ref §10.5): the card named, the evidence split, the reasons.</summary>
public static class VerdictText
{
    public static string CameraName(string? model) => model switch
    {
        null or "" => "DJI drone",
        "FC9113" => "DJI Air 3S",
        _ => model,
    };

    public static string Serial(CardIdentity id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var x = id.VolumeSerial.ToString("X8", CultureInfo.InvariantCulture);
        return $"{x[..4]}-{x[4..]}";
    }

    public static string CardName(CardInventory inventory, CardIdentity id)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        return $"{Drive(inventory)} · {CameraName(inventory.CameraModel)} · serial {Serial(id)}";
    }

    private static string Drive(CardInventory inventory) => OffloadPaths.DriveLabel(OffloadPaths.VolumeRoot(inventory.Source.Root));

    private static string N(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";

    public static string Headline(VerdictLevel level, CardInventory inventory, CardIdentity card, bool identityChanged, int forcesNotSafe,
                                  bool ledgerIssuesAccepted, IReadOnlyDictionary<AuditCategory, int> counts, AuditUnits audit)
    {
        ArgumentNullException.ThrowIfNull(counts);
        ArgumentNullException.ThrowIfNull(audit);
        var name = CardName(inventory, card);
        switch (level)
        {
            case VerdictLevel.NotSafe:
            {
                if (identityChanged) return $"{name}: Don't format yet: The card in {Drive(inventory)} is not the one that was offloaded";
                var reasons = new List<string>();
                if (forcesNotSafe > 0) reasons.Add(N(forcesNotSafe, "part of the card couldn't be read", "parts of the card couldn't be read"));
                int Kind(UnaccountedKind k) => audit.Unaccounted.Values.Count(v => v == k);
                if (Kind(UnaccountedKind.Failed) is > 0 and var failed) reasons.Add(N(failed, "file failed", "files failed"));
                if (Kind(UnaccountedKind.UnfinishedNotCopied) is > 0 and var unfinished)
                    reasons.Add(N(unfinished, "unfinished recording not copied", "unfinished recordings not copied"));
                if (Kind(UnaccountedKind.NotCopied) is > 0 and var notCopied) reasons.Add(N(notCopied, "new file not copied", "new files not copied"));
                if (Kind(UnaccountedKind.ChangedSinceScan) is > 0 and var changed)
                    reasons.Add(N(changed, "file changed since the scan", "files changed since the scan"));
                if (Kind(UnaccountedKind.Unrecognised) is > 0 and var unknown) reasons.Add(N(unknown, "unrecognised file", "unrecognised files"));
                return $"{name}: Don't format yet: {string.Join(", ", reasons)}";
            }
            case VerdictLevel.SafeWithAssumptions:
            {
                var reasons = new List<string>();
                if (Count(counts, AuditCategory.AssumedByRule) is > 0 and var assumed)
                    reasons.Add($"{N(assumed, "photo was", "photos were")} assumed already imported");
                if (audit.TruncatedAssumed > 0)
                    reasons.Add($"{N(audit.TruncatedAssumed, "unfinished recording", "unfinished recordings")} copied as-is; "
                                + $"the drone may still be able to repair {(audit.TruncatedAssumed == 1 ? "it" : "them")}");
                if (ledgerIssuesAccepted) reasons.Add("unreadable history lines were accepted");
                return $"{name}: Safe, with assumptions: {string.Join(", ", reasons)}; {Evidence(counts, audit)}";
            }
            default:
                return $"{name}: Safe to format: {Evidence(counts, audit)}";
        }
    }

    private static int Count(IReadOnlyDictionary<AuditCategory, int> counts, AuditCategory c) => counts.TryGetValue(c, out var n) ? n : 0;

    private static string Evidence(IReadOnlyDictionary<AuditCategory, int> counts, AuditUnits audit)
    {
        int verified = Count(counts, AuditCategory.VerifiedThisRun) + Count(counts, AuditCategory.InLedger);
        var parts = new List<string>
        {
            $"{verified} verified" + (audit.CachedVerifies > 0 ? $" ({audit.CachedVerifies} with buffered read-back)" : ""),
        };
        if (Count(counts, AuditCategory.NameSizeMatch) is > 0 and var nameSize) parts.Add($"{nameSize} matched by name+size only");
        if (Count(counts, AuditCategory.ConfirmedByYou) is > 0 and var confirmed) parts.Add($"{confirmed} confirmed by you");
        return string.Join(", ", parts);
    }
}
