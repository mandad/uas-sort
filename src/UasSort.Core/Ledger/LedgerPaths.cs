using System.Globalization;
using System.IO.Hashing;
using System.Text;

namespace UasSort.Core;

/// <summary>The ledger folder is DERIVED, never stored or configurable (Ref §11).</summary>
public static class LedgerPaths
{
    public const string FolderName = ".uas-sort";          // excluded from every library listing (Ref §7.1)
    public const string FilePattern = "ledger*.jsonl";     // read-only union, top level only

    public static string For(string videoRoot) => PathRules.Join(videoRoot, FolderName);

    public static string OwnFile(string videoRoot, string machine) => PathRules.Join(For(videoRoot), $"ledger-{machine}.jsonl");

    public static string BackupDir(string appDataDir, string canonicalVideoRoot)   // local only; one subfolder per video root
    {
        ArgumentNullException.ThrowIfNull(canonicalVideoRoot);
#pragma warning disable CA1308 // Ref §11: the key hashes the lowercase canonical root
        var key = XxHash64.HashToUInt64(Encoding.UTF8.GetBytes(PathRules.Normalize(canonicalVideoRoot).ToLowerInvariant()));
#pragma warning restore CA1308
        return PathRules.Join(PathRules.Join(appDataDir, "ledger-backup"), key.ToString("x16", CultureInfo.InvariantCulture));
    }

    public static bool IsLedgerFileName(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        return fileName.StartsWith("ledger", StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase);
    }
}
