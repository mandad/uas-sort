// src/UasSort.Core/Naming/SetFolderNamer.cs
using System.Globalization;
using UasSort.Core.Library;
using UasSort.Core.Planning;

namespace UasSort.Core.Naming;

/// <summary>The user's set-clash rule (Ref §8.8).</summary>
public static class SetFolderNamer
{
    private static readonly TimeSpan MtimeTolerance = TimeSpan.FromSeconds(2);

    /// <summary>photoRoot: where the offload writes set folders (&lt;photoRoot&gt;\&lt;FolderName&gt;). Only a listing of exactly that folder can
    /// be resumed; an existing subset anywhere else (a previous photo root, the video root) is a clash, as is any different set.</summary>
    public static SetPlacement Resolve(SetUnit set, Item first, LibraryIndex lib, LedgerSnapshot ledger, ISet<string> batchTaken,
                                       string photoRoot)
    {
        ArgumentNullException.ThrowIfNull(photoRoot);
        var card = set.Members.Select(m => (Name: PlanKeys.FileName(m.RelPath), m.Size, m.MtimeUtc)).ToList();

        if (ledger.SetsByName.TryGetValue(set.SetName, out var known)
            && known.Any(k => SameMemberList(k, card) && (k.FirstFrameCaptureUtc - first.Time.CaptureUtc).Duration() <= MtimeTolerance))
            return new SetPlacement(set.Id, set.SetName, SetResolution.Imported, []);

        foreach (var candidate in Candidates(set.SetName, first.Time.LocalDate))
        {
            if (batchTaken.Contains(candidate)) continue;
            var existing = lib.SetFolder(candidate).Where(l => !l.Members.IsEmpty).ToList();
            if (existing.Count == 0)
            {
                batchTaken.Add(candidate);
                return new SetPlacement(set.Id, candidate,
                    string.Equals(candidate, set.SetName, StringComparison.OrdinalIgnoreCase) ? SetResolution.Plain : SetResolution.DateSuffixed,
                    [.. card.Select(c => c.Name)]);
            }
            SetPlacement? resume = null;
            foreach (var l in existing)
            {
                var cardAllMatch = card.All(c => l.Members.Any(m => Same(m, c)));
                var existingAllMatch = l.Members.All(m => card.Any(c => Same(m, c)));
                if (cardAllMatch)                                             // equal, or card ⊂ existing
                {
                    batchTaken.Add(candidate);
                    return new SetPlacement(set.Id, candidate, SetResolution.Imported, []);
                }
                if (existingAllMatch && PathRules.Equal(l.FullPath, PathRules.Join(photoRoot, candidate)))   // existing ⊂ card: copy the missing
                    resume ??= new SetPlacement(set.Id, candidate, SetResolution.Resume,
                        [.. card.Where(c => !l.Members.Any(m => Same(m, c))).Select(c => c.Name)]);
            }
            if (resume is not null)
            {
                batchTaken.Add(candidate);
                return resume;
            }
            // clash → next candidate
        }
        throw new InvalidOperationException("Candidate sequence is infinite; unreachable.");
    }

    private static IEnumerable<string> Candidates(string plain, DateOnly firstFrameLocalDate)
    {
        yield return plain;
        var dated = $"{plain} {firstFrameLocalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
        yield return dated;
        for (var n = 2; ; n++) yield return $"{dated} ({n.ToString(CultureInfo.InvariantCulture)})";
    }

    private static bool Same((string Member, long Size, DateTime MtimeUtc) existing, (string Name, long Size, DateTime MtimeUtc) card) =>
        string.Equals(existing.Member, card.Name, StringComparison.OrdinalIgnoreCase)
        && existing.Size == card.Size
        && (existing.MtimeUtc - card.MtimeUtc).Duration() <= MtimeTolerance;

    private static bool SameMemberList(LedgerSet k, List<(string Name, long Size, DateTime MtimeUtc)> card) =>
        k.Members.Length == card.Count
        && card.All(c => k.Members.Any(m => string.Equals(m.Member, c.Name, StringComparison.OrdinalIgnoreCase) && m.Size == c.Size));
}
