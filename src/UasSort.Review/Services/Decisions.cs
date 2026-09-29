// src/UasSort.Review/Services/Decisions.cs
namespace UasSort.Review;

/// <summary>One card file a decision is recorded for; a set member carries its set name (Ref §7.3, §10.4).</summary>
public sealed record DecisionTarget(CardEntry File, DateTime? CaptureUtc, string? Set);

/// <summary>The card files a Review-side decision covers. Their ledger key is always Core's <see cref="FileKey.OfPath"/>.</summary>
public static class DecisionTargets
{
    /// <summary>The files a decision covers: a video's MP4, a photo's primary (its twin follows it), every set member.</summary>
    public static ImmutableArray<DecisionTarget> For(Item item) => item.Raw.Unit switch
    {
        VideoUnit v => [new DecisionTarget(v.Mp4, item.Time.CaptureUtc, null)],
        PhotoUnit p => [new DecisionTarget(p.Primary, item.Time.CaptureUtc, null)],
        SetUnit s => [.. s.Members.Select(m => new DecisionTarget(m, item.Time.CaptureUtc, s.SetName))],
    };

    public static DecisionTarget ForEntry(CardEntry e) => new(e, null, null);
}

/// <summary>Writes Verdict-page decisions and their revokes to this PC's own ledger file (Ref §10.4, §10.5).</summary>
public interface IDecisionService
{
    ImmutableArray<string> Record(DecisionKind kind, IReadOnlyList<DecisionTarget> targets, string why);
    void Revoke(IReadOnlyList<string> decisionIds);
    ImmutableArray<string> DecisionIdsFor(IReadOnlyList<DecisionTarget> targets, LedgerSnapshot ledger);
}

public sealed class LedgerDecisionService(ILedgerStore store, TimeProvider time, string machine) : IDecisionService
{
    public ImmutableArray<string> Record(DecisionKind kind, IReadOnlyList<DecisionTarget> targets, string why)
    {
        var at = time.GetUtcNow().UtcDateTime;
        var ids = ImmutableArray.CreateBuilder<string>();
        using var writer = store.OpenOwn();
        foreach (var t in targets)
        {
            var id = Guid.NewGuid().ToString("D");
            var name = t.File.RelPath[(t.File.RelPath.LastIndexOfAny(['/', '\\']) + 1)..];
            writer.Append(new DecisionRecord(1, id, machine, null, at, kind == DecisionKind.AssumedImported ? "assumedImported" : "dismissed",
                                             name, t.File.Size, t.File.RelPath, t.CaptureUtc, why, t.Set));
            ids.Add(id);
        }
        return ids.ToImmutable();
    }

    /// <summary>One revoke per decision, built by Part 07's VerdictDecisions.Revokes (the same records the Verdict page writes).</summary>
    public void Revoke(IReadOnlyList<string> decisionIds)
    {
        if (decisionIds.Count == 0) return;
        using var writer = store.OpenOwn();
        foreach (var r in VerdictDecisions.Revokes(decisionIds, machine, time)) writer.Append(r);
    }

    public ImmutableArray<string> DecisionIdsFor(IReadOnlyList<DecisionTarget> targets, LedgerSnapshot ledger)
    {
        var keys = targets.Select(t => FileKey.OfPath(t.File.RelPath, t.File.Size)).ToHashSet();
        return [.. ledger.Decisions.Values.Where(d => keys.Contains(d.Key)).Select(d => d.Id)];
    }
}
