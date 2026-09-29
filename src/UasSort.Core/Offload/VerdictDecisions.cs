using System.Collections.Immutable;

namespace UasSort.Core.Offload;

public enum NotCopiedKind { Video, Photo, Set, Unknown }

public sealed record NotCopiedRow(ItemId Unit, NotCopiedKind Kind, bool Truncated, DateOnly? LocalDate, int Files, long Bytes,
                                  AuditCategory Category, string Detail);

public sealed record DecisionCheck(bool Ok, string? Refusal);

/// <summary>Ref §10.5 "Verdict page": the "Not copied" list, the two decisions, their confirmation text, and undo.</summary>
public static class VerdictDecisions
{
    public static ImmutableArray<NotCopiedRow> NotCopied(FormatVerdict verdict, Plan plan)
    {
        ArgumentNullException.ThrowIfNull(verdict);
        ArgumentNullException.ThrowIfNull(plan);
        var units = plan.Base.Scan.Inventory.Units.ToDictionary(u => u.Id);
        var items = plan.Base.Items.ToDictionary(i => i.Raw.Unit.Id);
        var rows = ImmutableArray.CreateBuilder<NotCopiedRow>();
        foreach (var u in verdict.Units.Where(u => u.Worst is AuditCategory.Unaccounted or AuditCategory.AssumedByRule))
        {
            units.TryGetValue(u.Unit, out var unit);
            items.TryGetValue(u.Unit, out var item);
            var kind = unit switch
            {
                VideoUnit => NotCopiedKind.Video,
                PhotoUnit => NotCopiedKind.Photo,
                SetUnit => NotCopiedKind.Set,
                _ => NotCopiedKind.Unknown,
            };
            bool truncated = unit is VideoUnit { HasTrinf: true } || (item?.Flags.HasFlag(ItemFlags.Truncated) ?? false);
            var detail = u.Lines.First(l => l.Category == u.Worst).Detail;
            rows.Add(new NotCopiedRow(u.Unit, kind, truncated, kind == NotCopiedKind.Unknown ? null : item?.Time.LocalDate,
                                      u.Lines.Length, u.Lines.Sum(l => l.Size), u.Worst, detail));
        }
        return rows.ToImmutable();
    }

    public static ImmutableArray<NotCopiedRow> Day(IEnumerable<NotCopiedRow> rows, DateOnly localDate)
        => [.. rows.Where(r => r.Kind is NotCopiedKind.Photo or NotCopiedKind.Set && r.LocalDate == localDate)];

    public static DecisionCheck Check(DecisionKind kind, IReadOnlyList<NotCopiedRow> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        if (selected.Count == 0) return new DecisionCheck(false, "Nothing is selected");
        if (kind == DecisionKind.AssumedImported && selected.Any(r => r.Kind is not (NotCopiedKind.Photo or NotCopiedKind.Set)))
            return new DecisionCheck(false, "Only photos and sets can be recorded as imported");
        if (selected.Count > 1 && selected.Any(r => r.Kind is NotCopiedKind.Video or NotCopiedKind.Unknown))
            return new DecisionCheck(false, "Videos and unknown files are marked one at a time");
        return new DecisionCheck(true, null);
    }

    public static string Confirmation(DecisionKind kind, IReadOnlyList<NotCopiedRow> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        static string N(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";
        var parts = new List<string>();
        void Part(NotCopiedKind k, string one, string many)
        {
            int n = selected.Count(r => r.Kind == k);
            if (n > 0) parts.Add(N(n, one, many));
        }
        Part(NotCopiedKind.Video, "video", "videos");
        Part(NotCopiedKind.Photo, "photo", "photos");
        Part(NotCopiedKind.Set, "set", "sets");
        Part(NotCopiedKind.Unknown, "unknown file", "unknown files");
        var what = $"{string.Join(", ", parts)} ({OffloadPaths.Gb(selected.Sum(r => r.Bytes))})";
        return kind == DecisionKind.AssumedImported ? $"Record {what} as already imported?" : $"Mark {what} as not needed?";
    }

    public static ImmutableArray<DecisionRecord> Records(DecisionKind kind, IReadOnlyList<NotCopiedRow> selected, Plan plan,
                                                         string? runId, string machine, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(clock);
        var check = Check(kind, selected);
        if (!check.Ok) throw new InvalidOperationException(check.Refusal);
        var inventory = plan.Base.Scan.Inventory;
        var units = inventory.Units.ToDictionary(u => u.Id);
        var items = plan.Base.Items.ToDictionary(i => i.Raw.Unit.Id);
        var at = clock.GetUtcNow().UtcDateTime;
        var kindText = kind == DecisionKind.Dismissed ? "dismissed" : "assumedImported";
        var records = ImmutableArray.CreateBuilder<DecisionRecord>();
        foreach (var row in selected)
        {
            units.TryGetValue(row.Unit, out var unit);
            items.TryGetValue(row.Unit, out var item);
            IReadOnlyList<CardEntry> files;
            string? set = null;
            switch (unit)
            {
                case VideoUnit v: files = [v.Mp4]; break;
                case PhotoUnit p: files = p.JpgTwin is { } t ? [p.Primary, t] : [p.Primary]; break;
                case SetUnit s: files = s.Members; set = s.SetName; break;
                default:
                    files = [.. inventory.Entries.Where(e => string.Equals(OffloadPaths.NormRel(e.RelPath), OffloadPaths.NormRel(row.Unit.CardRelPath),
                                                                           StringComparison.OrdinalIgnoreCase))];
                    break;
            }
            var why = kind == DecisionKind.Dismissed ? "not needed"
                    : item?.Newness is ProbablyImported p2 ? p2.Why : "confirmed by you";
            foreach (var f in files)
                records.Add(new DecisionRecord(OffloadRecords.Version, OffloadRecords.NewId(), machine, runId, at, kindText,
                    OffloadPaths.FileName(f.RelPath), f.Size, f.RelPath, item?.Time.CaptureUtc, why, set));
        }
        return records.ToImmutable();
    }

    public static ImmutableArray<RevokeRecord> Revokes(IEnumerable<string> decisionIds, string machine, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(decisionIds);
        ArgumentNullException.ThrowIfNull(clock);
        var at = clock.GetUtcNow().UtcDateTime;
        return [.. decisionIds.Select(id => new RevokeRecord(OffloadRecords.Version, OffloadRecords.NewId(), machine, at, id))];
    }

    public static LedgerSnapshot Apply(LedgerSnapshot ledger, IEnumerable<DecisionRecord> made, IEnumerable<RevokeRecord> revoked)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(made);
        ArgumentNullException.ThrowIfNull(revoked);
        var decisions = ledger.Decisions.ToBuilder();
        foreach (var r in made)
        {
            var key = OffloadPaths.Key(r.Name, r.Size);
            var kind = r.Kind == "dismissed" ? DecisionKind.Dismissed : DecisionKind.AssumedImported;
            decisions[key] = new LedgerDecision(r.Id, key, kind, r.At, r.Machine, r.Set, r.Why);
        }
        var gone = revoked.Select(x => x.Decision).ToHashSet(StringComparer.Ordinal);
        foreach (var kv in decisions.Where(kv => gone.Contains(kv.Value.Id)).ToList()) decisions.Remove(kv.Key);
        return ledger with { Decisions = decisions.ToImmutable() };
    }
}
