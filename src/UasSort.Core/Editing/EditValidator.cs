// src/UasSort.Core/Editing/EditValidator.cs
using UasSort.Core.Naming;
using UasSort.Core.Planning;

namespace UasSort.Core.Editing;

/// <summary>Refused edits (Ref §8.9), validated against the plan that includes every earlier edit.</summary>
public static class EditValidator
{
    public static Rejected? Validate(Plan current, PlanEdit edit)
    {
        var items = current.Base.Items.ToDictionary(i => i.Raw.Unit.Id);
        var videoGroup = new Dictionary<ItemId, int>();
        for (var gi = 0; gi < current.Groups.Length; gi++)
            foreach (var v in current.Groups[gi].Videos) videoGroup[v] = gi;

        var refs = PlanEditRefs.Referenced(edit);
        var needsVideo = edit is not (SetIncluded or SetDayIncluded);
        if (refs.Any(r => needsVideo ? !videoGroup.ContainsKey(r) : !items.ContainsKey(r)))
            return new Rejected(RejectReason.ItemsNotFound, "Some clips in this change are no longer on the card.");

        switch (edit)
        {
            case Merge m:
            {
                int a = videoGroup[m.InA], b = videoGroup[m.InB];
                if (a == b) return null;
                var walls = current.Groups.Skip(Math.Min(a, b)).Take(Math.Abs(a - b) + 1)
                    .Select(g => g.Wall?.FullPath.ToUpperInvariant()).Where(w => w is not null).Distinct().Count();
                return walls > 1 ? new Rejected(RejectReason.MergeAcrossLibraryFolders, "These clips are already in two different folders") : null;
            }
            case SplitBefore s:
                return current.Groups[videoGroup[s.First]].Videos[0] == s.First
                    ? new Rejected(RejectReason.SplitAtGroupStart, "This clip already starts its group")
                    : null;
            case MoveToNewGroup mv when mv.Items.Any(id => items[id].Newness is Imported):
            case MoveToGroup mt when mt.Items.Any(id => items[id].Newness is Imported):
                return new Rejected(RejectReason.MoveImportedItem, "Clips already in the library stay in their folder");
            case Rename r:
            {
                var target = current.Groups[videoGroup[r.InGroup]].Target;
                var existing = target is AlreadyImported || (target is Append a && a.Why != FolderNamer.FolderExistsWhy);
                return existing
                    ? new Rejected(RejectReason.RenameExistingFolder, "Appending to an existing folder; choose New folder to name a new one")
                    : null;
            }
            case Retarget { Choice: AppendTo to } rt:
                return ValidateAppendTo(current, current.Groups[videoGroup[rt.InGroup]], to.FolderFullPath, rt.ConfirmedBeforeFolderDate);
            default:
                return null;
        }
    }

    private static Rejected? ValidateAppendTo(Plan current, VideoGroup g, string path, bool confirmed)
    {
        var s = current.Base.Scan.Settings;
        string[] reserved = [LedgerPaths.For(s.VideoRoot), s.PhotoRoot, .. s.PreviousPhotoRoots];
        if (reserved.Any(r => IsUnder(path, r)))
            return new Rejected(RejectReason.RetargetIntoReservedFolder, "That folder is reserved for uas-sort history or photos");
        if (!IsUnder(path, s.VideoRoot) || Norm(path) == Norm(s.VideoRoot))
            return new Rejected(RejectReason.RetargetOutsideVideoRoot, $"Pick a folder inside {Path.GetFileName(Norm(s.VideoRoot))}");
        var f = Planner.FolderRefFor(path, g.Start, current.Base.Scan.Library);
        if (f.NameDate > g.Start && !confirmed)
            return new Rejected(RejectReason.RetargetLaterDatedFolderUnconfirmed,
                                $"Folder is dated {PlanText.ShortDate(f.NameDate)}; these clips start {PlanText.ShortDate(g.Start)}");
        return null;
    }

    private static string Norm(string p) => p.Replace('/', '\\').TrimEnd('\\').ToUpperInvariant();

    public static bool IsUnder(string path, string root)
    {
        var p = Norm(path);
        var r = Norm(root);
        return p == r || p.StartsWith(r + "\\", StringComparison.Ordinal);
    }
}
