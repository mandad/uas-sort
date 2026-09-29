// src/UasSort.Core/Settings/SettingsRecovery.cs
using UasSort.Core.Ledger;
using UasSort.Core;

namespace UasSort.Core.Config;

/// <summary>Roots offered after a settings recovery: the latest run record in the local backup mirrors (Ref §11).</summary>
public static class SettingsRecovery
{
    public static RunRoots? RootsFromLastRun(IReadOnlyList<LedgerFileText> mirrors)
    {
        ArgumentNullException.ThrowIfNull(mirrors);
        if (mirrors.Count == 0) return null;
        LedgerSnapshot snap = LedgerReader.Read(mirrors, LedgerSnapshots.Detached("", mirrors.Select(m => m.FullPath)));
        LedgerRun? last = snap.Runs.OrderByDescending(r => r.EndUtc).ThenByDescending(r => r.StartUtc).FirstOrDefault();
        return last is null ? null : new RunRoots(last.VideoRoot, last.PhotoRoot);
    }
}
