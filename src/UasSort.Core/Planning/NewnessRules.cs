// src/UasSort.Core/Planning/NewnessRules.cs
using UasSort.Core.Library;

namespace UasSort.Core.Planning;

/// <summary>Newness rules of Ref §7 (videos §7.2; photos and sets §7.3 in NewnessRules.Photo.cs).</summary>
public static partial class NewnessRules
{
    public static Newness Video(VideoUnit v, LibraryIndex lib, LedgerSnapshot ledger)
    {
        var name = PlanKeys.FileName(v.Mp4.RelPath);
        var key = PlanKeys.Key(name, v.Mp4.Size);
        var listed = lib.Match(key);
        ledger.Files.TryGetValue(key, out var lf);
        if (!listed.IsEmpty || lf is not null)
        {
            var inFolder = listed.FirstOrDefault(f => f.EventFolder is not null) ?? (listed.IsEmpty ? null : listed[0]);
            var by = lf is null ? Evidence.LibraryNameSize
                   : lf.Verify == VerifyKind.NameSize ? Evidence.LedgerNameSize : Evidence.LedgerVerified;
            var why = inFolder is not null
                ? $"in library: {inFolder.FullPath}"
                : $"copied on {PlanText.ShortDate(DateOnly.FromDateTime(lf!.AtUtc))}; no longer in the library";
            return new Imported(by, inFolder?.EventFolder, why);
        }
        if (ledger.Decisions.TryGetValue(key, out var d))
            return new Decided(d.Kind, d.AtUtc, d.Machine);
        return FindConflict(key, lib, ledger) ?? (Newness)new IsNew(NewReason.NoMatch, null);
    }

    /// <summary>Same NormName, different size, in any listed root or the ledger (callers have already ruled out a same-size match).</summary>
    internal static Conflict? FindConflict(FileKey key, LibraryIndex lib, LedgerSnapshot ledger)
    {
        var other = lib.SameNameOtherSize(key.NormName, key.Size);
        if (!other.IsEmpty) return new Conflict(other[0].FullPath, other[0].Key.Size);
        foreach (var (k, f) in ledger.Files)
            if (k.NormName == key.NormName && k.Size != key.Size)
                return new Conflict(f.Dest, k.Size);
        return null;
    }
}
