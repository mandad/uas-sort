// src/UasSort.Core/Cleanup/CleanupPaths.cs
namespace UasSort.Core.Cleanup;

/// <summary>Card-relative path helpers for Card cleanup (defined here). Card paths use '/', like ItemId.</summary>
public static class CleanupPaths
{
    public static string Rel(string relPath)
    {
        ArgumentNullException.ThrowIfNull(relPath);
        return relPath.Replace('\\', '/').Trim('/');
    }

    public static string Name(string relPath)
    {
        var r = Rel(relPath);
        var i = r.LastIndexOf('/');
        return i < 0 ? r : r[(i + 1)..];
    }

    public static string Dir(string relPath)
    {
        var r = Rel(relPath);
        var i = r.LastIndexOf('/');
        return i < 0 ? "" : r[..i];
    }

    public static bool IsUnder(string relPath, string relDir)
    {
        var r = Rel(relPath);
        var d = Rel(relDir);
        return d.Length == 0
            || r.Equals(d, StringComparison.OrdinalIgnoreCase)
            || r.StartsWith(d + "/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Canonical Windows path of a card file: "&lt;root&gt;\a\b". Null for an empty, "." or ".." segment.</summary>
    public static string? Full(string cardRoot, string relPath)
    {
        ArgumentNullException.ThrowIfNull(cardRoot);
        ArgumentNullException.ThrowIfNull(relPath);
        var root = cardRoot.Replace('/', '\\').TrimEnd('\\');
        var raw = relPath.Replace('\\', '/');
        if (raw.StartsWith('/')) raw = raw[1..];
        var segments = raw.Split('/');
        foreach (var s in segments)
            if (s.Length == 0 || s == "." || s == "..") return null;
        return root + "\\" + string.Join('\\', segments);
    }

    /// <summary>⌈size / cluster⌉ × cluster; 0 for an empty file (Ref §10.6 Order and size).</summary>
    public static long Allocated(long size, int clusterBytes)
    {
        if (size <= 0) return 0;
        if (clusterBytes <= 0) return size;
        return (size + clusterBytes - 1) / clusterBytes * clusterBytes;
    }
}

/// <summary>FileKey for Card cleanup's fresh-listing proof (defined here): Part 02's FileKey rule (Ref §7.1), so the key of a
/// card file equals the ledger's and the library index's key for the same file.</summary>
public static class CleanupKeys
{
    public static string NormName(string fileName) => FileKey.NormalizeName(fileName);

    public static FileKey Key(string relPath, long size) => FileKey.OfPath(relPath, size);
}
