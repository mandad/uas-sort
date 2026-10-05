using System.Buffers.Binary;
using System.Security.Principal;
using System.Text;

namespace UasSort.Platform.Stores;

/// <summary>Platform tests and --selftest only: removes from the current user's Recycle Bin exactly the items whose original path lies
/// under one of this run's own scratch folders (%TEMP%\uas-sort-test-* or %TEMP%\uas-sort-selftest-*). Each $I record names the original
/// path; its $R twin is the item. Nothing else is touched. Internal and outside IoGuardPolicy on purpose: the guard's model is the
/// library, card and app-data roots, and the Recycle Bin is none of them; instead this class refuses any root that is not this run's own
/// %TEMP% scratch folder, Platform.Tests reach it through InternalsVisibleTo, and the deployed app only through
/// SelfTestSandbox.PurgeOwnRecycleBinItems (source-guard test).</summary>
internal static class RecycleBinPurge
{
    public static int PurgeOwn(string scratchRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scratchRoot);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(scratchRoot));
        var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        var name = Path.GetFileName(root);
        var own = name.StartsWith("uas-sort-test-", StringComparison.OrdinalIgnoreCase)
                  || name.StartsWith(SelfTestSandbox.FolderPrefix, StringComparison.OrdinalIgnoreCase);
        if (!own || !string.Equals(Path.GetDirectoryName(root), temp, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("RecycleBinPurge only purges this run's own %TEMP% scratch items, not " + root);
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("The current user has no SID");
        var bin = Path.Join(Path.GetPathRoot(root), "$Recycle.Bin", sid);
        var purged = 0;
#pragma warning disable RS0030 // IO layer: tests and selftest only; deletes only Recycle Bin items whose $I record names this run's own %TEMP% scratch folder
        if (!Directory.Exists(bin)) return 0;
        foreach (var info in Directory.EnumerateFiles(bin, "$I*"))
        {
            if (OriginalPath(File.ReadAllBytes(info)) is not { } original
                || !original.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            var data = Path.Join(bin, "$R" + Path.GetFileName(info)[2..]);
            if (Directory.Exists(data)) Directory.Delete(data, recursive: true);
            else if (File.Exists(data)) File.Delete(data);
            File.Delete(info);
            purged++;
        }
#pragma warning restore RS0030
        return purged;
    }

    /// <summary>The original path in a $I record: version 2 (Windows 10 and later, length-prefixed UTF-16) or version 1 (260 chars).</summary>
    internal static string? OriginalPath(ReadOnlySpan<byte> info)
    {
        if (info.Length < 24) return null;
        var version = BinaryPrimitives.ReadInt64LittleEndian(info);
        if (version == 2 && info.Length >= 28)
        {
            var chars = BinaryPrimitives.ReadInt32LittleEndian(info[24..]);
            if (chars <= 0 || 28L + chars * 2L > info.Length) return null;
            return Encoding.Unicode.GetString(info.Slice(28, chars * 2)).TrimEnd('\0');
        }
        if (version == 1 && info.Length >= 24 + 520)
        {
            var text = Encoding.Unicode.GetString(info.Slice(24, 520));
            var end = text.IndexOf('\0', StringComparison.Ordinal);
            return end < 0 ? text : text[..end];
        }
        return null;
    }
}
