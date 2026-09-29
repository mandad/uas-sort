// src/UasSort.Review/Settings/LedgerStatusText.cs
using System.Text.RegularExpressions;

namespace UasSort.Review;

/// <summary>The read-only ledger status line of Setup and Settings (Ref §9.1, §9.14, §11 folder status).</summary>
public static partial class LedgerStatusText
{
    [GeneratedRegex(@"^ledger-(?<m>[^.\-]+)(?:-[^.]*)?\.jsonl$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LedgerName();

    public static (string Text, InfoSeverity Severity, bool OfferKeepOnDevice) For(LedgerFolderStatus s)
    {
        var library = Path.GetFileName(Path.GetDirectoryName(s.Folder.TrimEnd('\\')) ?? s.Folder);
        return s.State switch
        {
            LedgerFolderState.VideoRootMissing => ("The video folder doesn't exist; pick an existing folder", InfoSeverity.Error, false),
            LedgerFolderState.CloudOnly => ($"Set {library}\\.uas-sort to Always keep on this device", InfoSeverity.Error, true),
            LedgerFolderState.Unwritable => ($"Can't write the history file in {s.Folder}", InfoSeverity.Error, false),
            LedgerFolderState.Missing or LedgerFolderState.Empty =>
                ("No history yet. It will be created at the first offload. If you've used uas-sort on another PC, let OneDrive finish syncing first",
                 InfoSeverity.Informational, false),
            LedgerFolderState.NotPinned => ($"{History(s)} · not kept on this device", InfoSeverity.Warning, true),
            _ => (History(s), InfoSeverity.Informational, false),
        };
    }

    private static string History(LedgerFolderStatus s)
    {
        var machines = s.LedgerFiles.Select(f => LedgerName().Match(Path.GetFileName(f)))
                                    .Where(m => m.Success).Select(m => m.Groups["m"].Value.ToUpperInvariant())
                                    .Distinct(StringComparer.Ordinal).Count();
        return machines == 1 ? "History: 1 PC's ledger found" : string.Create(CultureInfo.InvariantCulture, $"History: {machines} PCs' ledgers found");
    }
}
