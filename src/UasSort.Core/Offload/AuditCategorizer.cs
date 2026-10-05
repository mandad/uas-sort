using System.Collections.Immutable;

namespace UasSort.Core.Offload;

/// <summary>A unit's most urgent reason for being Unaccounted (enum order = urgency); used for the NotSafe headline.</summary>
public enum UnaccountedKind { ChangedSinceScan, Failed, UnfinishedNotCopied, NotCopied, Unrecognised }

public sealed record AuditUnits(ImmutableArray<UnitAudit> Units, ImmutableDictionary<ItemId, UnaccountedKind> Unaccounted,
                                int TruncatedAssumed, int CachedVerifies);

/// <summary>Ref §10.5: one category per card file, the worst per unit.</summary>
public static class AuditCategorizer
{
    private enum Role { Video, Photo, Twin, SetMember }

    internal const string CardSwappedDetail = "not copied: the card was swapped";

    /// <summary>True for an Unaccounted line no ledger decision can clear: the file was added, removed or changed since the scan
    /// (the "changed since scan" prefix, also used for ChangedOnCard), or the card was swapped during the offload.</summary>
    internal static bool NeedsRescan(AuditLine line)
        => line.Category == AuditCategory.Unaccounted
           && (line.Detail.StartsWith(CardDiffResult.ChangedDetail, StringComparison.Ordinal)
               || string.Equals(line.Detail, CardSwappedDetail, StringComparison.Ordinal));

    public static AuditUnits Categorize(CardInventory inventory, Plan plan, OffloadResult? offload, LedgerSnapshot ledger, CardDiffResult diff)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(diff);
        var ctx = new Context(plan, offload, ledger, diff);
        var units = ImmutableArray.CreateBuilder<UnitAudit>();
        var kinds = ImmutableDictionary.CreateBuilder<ItemId, UnaccountedKind>();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = plan.Base.Items.ToDictionary(i => i.Raw.Unit.Id);
        int truncatedAssumed = 0;

        AuditCategory Add(ItemId id, List<(AuditLine Line, UnaccountedKind? Kind)> lines)
        {
            var worst = lines.Max(l => l.Line.Category);
            units.Add(new UnitAudit(id, worst, [.. lines.Select(l => l.Line)]));
            if (worst == AuditCategory.Unaccounted)
                kinds[id] = lines.Where(l => l.Kind is not null).Min(l => l.Kind!.Value);
            return worst;
        }

        foreach (var unit in inventory.Units)
        {
            items.TryGetValue(unit.Id, out var item);
            var lines = new List<(AuditLine Line, UnaccountedKind? Kind)>();
            switch (unit)
            {
                case VideoUnit v:
                    lines.Add(ctx.Media(v.Mp4, item, Role.Video, null));
                    break;
                case PhotoUnit p:
                    lines.Add(ctx.Media(p.Primary, item, Role.Photo, null));
                    if (p.JpgTwin is { } twin) lines.Add(ctx.Media(twin, item, Role.Twin, null));
                    break;
                case SetUnit s:
                    foreach (var m in s.Members) lines.Add(ctx.Media(m, item, Role.SetMember, s));
                    break;
            }
            foreach (var l in lines) claimed.Add(OffloadPaths.NormRel(l.Line.CardRelPath));
            var worst = Add(unit.Id, lines);
            if (Context.Truncated(item) || unit is VideoUnit { HasTrinf: true })
                if (worst is AuditCategory.VerifiedThisRun or AuditCategory.InLedger or AuditCategory.NameSizeMatch) truncatedAssumed++;
        }
        foreach (var e in inventory.Entries)
            if (!claimed.Contains(OffloadPaths.NormRel(e.RelPath))) Add(new ItemId(e.RelPath), [ctx.Loose(e)]);
        foreach (var added in diff.Added)
            Add(new ItemId(added.RelPath),
                [(new AuditLine(added.RelPath, added.Size, AuditCategory.Unaccounted, CardDiffResult.AddedDetail), UnaccountedKind.ChangedSinceScan)]);

        int cached = offload?.Outcomes.Count(o => o is Verified { Mode: VerifyMode.Cached }) ?? 0;
        return new AuditUnits(units.ToImmutable(), kinds.ToImmutable(), truncatedAssumed, cached);
    }

    private sealed class Context
    {
        private readonly Plan _plan;
        private readonly LedgerSnapshot _ledger;
        private readonly CardDiffResult _diff;
        private readonly Dictionary<string, CopyOutcome> _outcomes = new(StringComparer.OrdinalIgnoreCase);

        public Context(Plan plan, OffloadResult? offload, LedgerSnapshot ledger, CardDiffResult diff)
        {
            _plan = plan;
            _ledger = ledger;
            _diff = diff;
            if (offload is not null)
                foreach (var o in offload.Outcomes) _outcomes[OffloadPaths.NormRel(o.Job.CardRelPath)] = o;
        }

        public static bool Truncated(Item? item)
            => item is not null && (item.Flags.HasFlag(ItemFlags.Truncated) || item.Raw.Unit is VideoUnit { HasTrinf: true });

        private static UnaccountedKind NotCopied(Item? item) => Truncated(item) ? UnaccountedKind.UnfinishedNotCopied : UnaccountedKind.NotCopied;

        private static (AuditLine Line, UnaccountedKind? Kind) Line(CardEntry e, AuditCategory category, string detail)
            => (new AuditLine(e.RelPath, e.Size, category, detail), null);

        private static (AuditLine Line, UnaccountedKind? Kind) Bad(CardEntry e, string detail, UnaccountedKind kind)
            => (new AuditLine(e.RelPath, e.Size, AuditCategory.Unaccounted, detail), kind);

        private static string DecisionText(LedgerDecision d)
            => d.Kind == DecisionKind.Dismissed ? "you marked it not needed" : "you recorded it as imported";

        public (AuditLine Line, UnaccountedKind? Kind) Loose(CardEntry e)
        {
            if (_diff.Touched(e.RelPath) is { } changed) return Bad(e, changed, UnaccountedKind.ChangedSinceScan);
            if (e.Class == EntryClass.Skip) return Line(e, AuditCategory.SkippedByRule, e.Rule ?? "skipped by rule");
            if (_ledger.Decisions.TryGetValue(OffloadPaths.Key(e.RelPath, e.Size), out var d))
                return Line(e, AuditCategory.ConfirmedByYou, DecisionText(d));
            return Bad(e, "not recognised by uas-sort; copy it by hand if you need it", UnaccountedKind.Unrecognised);
        }

        public (AuditLine Line, UnaccountedKind? Kind) Media(CardEntry e, Item? item, Role role, SetUnit? set)
        {
            if (_diff.Touched(e.RelPath) is { } changed) return Bad(e, changed, UnaccountedKind.ChangedSinceScan);
            if (_outcomes.TryGetValue(OffloadPaths.NormRel(e.RelPath), out var o))
            {
                // a file this run left uncopied stays Unaccounted until copied or individually decided (Ref §10.5)
                if ((o is Failed or Cancelled or NotStarted or ConflictAtRename) && Decision(e, set) is { } d)
                    return Line(e, AuditCategory.ConfirmedByYou, DecisionText(d));
                return FromOutcome(e, o, item);
            }
            return FromEvidence(e, item, role, set);
        }

        private static (AuditLine Line, UnaccountedKind? Kind) FromOutcome(CardEntry e, CopyOutcome o, Item? item) => o switch
        {
            Verified v => Line(e, AuditCategory.VerifiedThisRun, v.Mode == VerifyMode.Cached ? "verified this run (buffered read-back)" : "verified this run"),
            AlreadyThere => Line(e, AuditCategory.NameSizeMatch, "already at the destination with the same size"),
            Failed f => Bad(e, $"failed ({f.Phase}): {f.Error}", UnaccountedKind.Failed),
            ChangedOnCard => Bad(e, CardDiffResult.ChangedDetail + " (during the offload)", UnaccountedKind.ChangedSinceScan),
            CardSwapped => Bad(e, CardSwappedDetail, NotCopied(item)),
            Cancelled => Bad(e, "not copied: cancelled", NotCopied(item)),
            NotStarted => Bad(e, "not copied: the offload stopped first", NotCopied(item)),
            ConflictAtRename => Bad(e, "not copied: a file with this name appeared at the destination", NotCopied(item)),
        };

        private static bool Fits(SetUnit? set, string? recordSet)
            => set is null || string.Equals(recordSet, set.SetName, StringComparison.OrdinalIgnoreCase);

        private LedgerDecision? Decision(CardEntry e, SetUnit? set)
            => _ledger.Decisions.TryGetValue(OffloadPaths.Key(e.RelPath, e.Size), out var d) && Fits(set, d.Set) ? d : null;

        private LedgerPhotoDelete? PhotoDelete(CardEntry e, SetUnit? set)
            => _ledger.PhotoDeletes.TryGetValue(OffloadPaths.Key(e.RelPath, e.Size), out var d) && Fits(set, d.Set) ? d : null;

        private (AuditLine Line, UnaccountedKind? Kind) FromEvidence(CardEntry e, Item? item, Role role, SetUnit? set)
        {
            var key = OffloadPaths.Key(e.RelPath, e.Size);

            _ledger.Files.TryGetValue(key, out var file);
            if (file is not null && !Fits(set, file.Set)) file = null;
            if (file is { Verify: VerifyKind.Unbuffered or VerifyKind.Cached }) return Line(e, AuditCategory.InLedger, "in the history, verified");
            if (Decision(e, set) is { } d) return Line(e, AuditCategory.ConfirmedByYou, DecisionText(d));
            if (role != Role.Video && PhotoDelete(e, set) is { } removed)                               // Picture Offload cleanup (photoDelete)
                return Line(e, AuditCategory.InLedger, PhotoDeleteTexts.Evidence(removed));
            if (file is { Verify: VerifyKind.NameSize }) return Line(e, AuditCategory.NameSizeMatch, "in the history, matched by name and size");
            if (set is not null)
            {
                if (_plan.Base.Sets.TryGetValue(set.Id, out var placement)
                    && (placement.Resolution == SetResolution.Imported
                        || (placement.Resolution == SetResolution.Resume
                            && !placement.MembersToCopy.Contains(OffloadPaths.FileName(e.RelPath), StringComparer.OrdinalIgnoreCase))))
                    return Line(e, AuditCategory.NameSizeMatch, "in the library's set folder (name, size and time)");
            }
            else if (!_plan.Base.Scan.Library.Match(key).IsEmpty)
            {
                return Line(e, AuditCategory.NameSizeMatch, "same name and size in the library");
            }
            if (role == Role.Twin)
            {
                if (!_plan.Base.Scan.Settings.CopyJpgTwin) return Line(e, AuditCategory.SkippedByRule, "JPG twin: copying disabled in Settings");
                if (item?.Newness is Imported) return Line(e, AuditCategory.AssumedByRule, "JPG twin assumed imported with its DNG");
                if (item?.Newness is Decided) return Line(e, AuditCategory.ConfirmedByYou, "follows its DNG (confirmed by you)");
            }
            return item?.Newness switch
            {
                ProbablyImported p => Line(e, AuditCategory.AssumedByRule, $"probably imported: {p.Why}"),
                Decided => Line(e, AuditCategory.ConfirmedByYou, "confirmed by you"),
                Imported i when i.By == Evidence.LedgerVerified => Line(e, AuditCategory.InLedger, "in the history, verified"),
                Imported => Line(e, AuditCategory.NameSizeMatch, "matched by name and size"),
                Conflict => Bad(e, "a different file with this name is in the library; not copied", NotCopied(item)),
                IsNew => Bad(e, NewDetail(item!), NotCopied(item)),
                null => Bad(e, "not part of the plan", UnaccountedKind.Unrecognised),
            };
        }

        private static string NewDetail(Item item)
            => item.Raw.ProbeError is not null ? "new, metadata unreadable; not copied"
             : Truncated(item) ? "unfinished recording; not copied"
             : "new; not copied";
    }
}
