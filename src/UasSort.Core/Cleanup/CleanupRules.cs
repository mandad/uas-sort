// src/UasSort.Core/Cleanup/CleanupRules.cs
namespace UasSort.Core.Cleanup;

/// <summary>Everything Ref §10.6 eligibility rows 2–20 look at, for one card file of a unit (defined here).</summary>
public sealed record FileFacts(CardEntry Entry, ItemKind UnitKind, bool IsCompanion, bool ChangedSinceScan,
    bool UnderEnumerationError, bool UnitProbeError, bool UnitTruncated, AuditCategory Category, Newness? Newness,
    LedgerDecision? Decision, bool Listed, LedgerFile? LedgerRecord, string TzId);

/// <summary>A file's eligibility, reason text, not-in-library reason and evidence source (defined here).</summary>
public sealed record FileVerdict(CleanupEligibility Eligibility, string Reason, NotInLibraryReason? NotInLibrary, EvidenceSource? Source);

public static class CleanupRules
{
    /// <summary>The prefix of every AuditLine.Detail CardAudit gives a file added, removed or changed since the scan
    /// (Ref §10.5; Part 07's CardDiffResult.ChangedDetail, "changed since scan").</summary>
    public const string ChangedSinceScanDetail = CardDiffResult.ChangedDetail;
    private const uint ReadOnlyAttribute = 0x1;

    private static FileVerdict Never(string reason) => new(CleanupEligibility.Never, reason, null, null);
    private static FileVerdict Nil(NotInLibraryReason why, string reason) => new(CleanupEligibility.NotInLibrary, reason, why, null);
    private static FileVerdict Proven(EvidenceSource src, string reason) => new(CleanupEligibility.Evidence, reason, null, src);

    public static FileVerdict? Classify(FileFacts f)
    {
        ArgumentNullException.ThrowIfNull(f);
        var e = f.Entry;
        if (f.ChangedSinceScan) return Never("changed since the scan");                                  // row 2
        if (f.UnderEnumerationError) return Never("part of the card couldn't be read");                  // row 3
        if ((e.RawAttributes & ReadOnlyAttribute) != 0) return Never("marked read-only on the card");     // row 4
        if (f.IsCompanion) return null;                                                                   // row 8
        if (e.Class == EntryClass.Unknown) return Never("unknown file");                                  // row 5
        if (f.UnitProbeError) return Never("its metadata couldn't be read");                             // row 6
        if (e.Class == EntryClass.Skip) return Never(SkipReason(e.RelPath));                             // row 7

        if (f.Category == AuditCategory.ConfirmedByYou)                                                   // rows 10–11 (gap G1: before row 9)
        {
            var decided = f.Newness as Decided;
            var kind = f.Decision?.Kind ?? decided?.Kind ?? DecisionKind.Dismissed;
            DateTime? at = f.Decision?.AtUtc ?? decided?.AtUtc;
            var on = at is { } a ? " on " + CleanupFormat.DayOf(a, f.TzId) : "";
            return kind == DecisionKind.Dismissed
                ? Nil(NotInLibraryReason.Dismissed, "you marked it not needed" + on)
                : Nil(NotInLibraryReason.RecordedAsImported, "you recorded it as imported" + on + "; not verified");
        }

        if (f.UnitTruncated)                                                                              // row 9
            return Nil(NotInLibraryReason.Unfinished, f.Listed || f.Category == AuditCategory.NameSizeMatch
                ? "unfinished (a same-size copy is in your library; the drone may still repair the card copy)"
                : "unfinished recording; the drone may still repair it");

        if (f.Category is AuditCategory.VerifiedThisRun or AuditCategory.InLedger or AuditCategory.NameSizeMatch)
        {
            if (f.Listed) return Proven(EvidenceSource.Listed, f.Category switch                          // rows 12, 14
            {
                AuditCategory.VerifiedThisRun => "copied and verified today",
                AuditCategory.InLedger => "in the history, verified",
                _ => "same name and size in the library",
            });
            var day = f.LedgerRecord is { } r ? CleanupFormat.DayOf(r.AtUtc, f.TzId) : null;
            if (f.UnitKind == ItemKind.Video)                                                             // row 13
                return Nil(NotInLibraryReason.NoLongerInLibrary,
                    day is null ? "no longer in your library" : $"copied on {day}, no longer in your library");
            if (f.LedgerRecord is { Verify: VerifyKind.Unbuffered or VerifyKind.Cached })                 // row 15
                return Proven(EvidenceSource.HistoryOnly, $"copied and verified on {day}; Lightroom may have moved it");
            if (f.LedgerRecord is { Verify: VerifyKind.NameSize })                                        // row 16
                return Nil(NotInLibraryReason.NoLongerInLibrary, $"matched by name and size on {day}, no longer in your library");
        }

        if (f.Category == AuditCategory.AssumedByRule)                                                    // row 17
            return Nil(NotInLibraryReason.ProbablyImported, "probably imported, not proven");
        if (f.Category == AuditCategory.Unaccounted && f.Newness is IsNew)                               // row 18
            return Nil(NotInLibraryReason.New, "new: not in your library");
        if (f.Category == AuditCategory.Unaccounted && f.Newness is Conflict)                            // row 19
            return Nil(NotInLibraryReason.Conflict, $"a different file named {CleanupPaths.Name(e.RelPath)} is in the library");
        return Never("not proven either way");                                                            // row 20
    }

    /// <summary>Reason for a card file outside every unit: always Never (rows 2–5, 7, 20).</summary>
    public static string LooseReason(CardEntry e, bool changedSinceScan, bool underEnumerationError)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (changedSinceScan) return "changed since the scan";
        if (underEnumerationError) return "part of the card couldn't be read";
        if ((e.RawAttributes & ReadOnlyAttribute) != 0) return "marked read-only on the card";
        return e.Class switch
        {
            EntryClass.Unknown => "unknown file",
            EntryClass.Skip => SkipReason(e.RelPath),
            _ => "not proven either way",
        };
    }

    private static string SkipReason(string relPath)
    {
        var rel = CleanupPaths.Rel(relPath);
        if (CleanupPaths.IsUnder(rel, "MISC")) return "DJI system file";
        var name = CleanupPaths.Name(rel);
        foreach (var ext in (ReadOnlySpan<string>)[".lrf", ".srt", ".trinf", ".avc1"])
            if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return "not tied to a clip";
        return "system file";
    }
}
