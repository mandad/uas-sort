// src/UasSort.Review/Services/UnhandledText.cs
namespace UasSort.Review;

/// <summary>The unhandled-exception dialog (Ref §12 "Log; dialog listing what this run copied (from the ledger)").</summary>
public static class UnhandledText
{
    public const string Title = "Something went wrong";
    public const int MaxListed = 10;

    /// <summary>The file names the ledger records for this run, in name order (the ledger stays authoritative; this is a reminder).</summary>
    public static IReadOnlyList<string> CopiedThisRun(LedgerSnapshot ledger, string runId)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        return [.. ledger.Files.Values.Where(f => string.Equals(f.Run, runId, StringComparison.Ordinal))
                                      .Select(f => Path.GetFileName(f.Dest))
                                      .Order(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>copiedThisRun: null when no run is in progress or shown (nothing is said about copies); else the files this run copied.</summary>
    public static string Body(string message, IReadOnlyList<string>? copiedThisRun)
    {
        var body = "uas-sort hit an unexpected error: " + message + "\n\nYour edits are kept as a draft, and every file this run "
                 + "copied is recorded in the history. Restart uas-sort to continue.";
        if (copiedThisRun is null) return body;
        if (copiedThisRun.Count == 0) return body + "\n\nCopied this run: nothing yet";
        var more = copiedThisRun.Count > MaxListed ? string.Create(CultureInfo.InvariantCulture, $" +{copiedThisRun.Count - MaxListed} more") : "";
        return body + $"\n\nCopied this run: {Fmt.Count(copiedThisRun.Count, "file", "files")}: "
               + string.Join(", ", copiedThisRun.Take(MaxListed)) + more;
    }
}
