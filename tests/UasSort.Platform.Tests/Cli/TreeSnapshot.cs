using System.Globalization;

namespace UasSort.Platform.Tests.Cli;

/// <summary>A listing of every entry under a root: relative path, kind, size, last-write ticks, attributes. Opens no file.</summary>
internal static class TreeSnapshot
{
    /// <param name="skipTopFolder">A folder directly under the root left out with everything in it (never entered), or null.</param>
    public static IReadOnlyList<string> Take(string root, string? skipTopFolder = null)
    {
        var dir = new DirectoryInfo(root);
        if (!dir.Exists) return ["(absent)"];
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = false };
        var top = new EnumerationOptions { RecurseSubdirectories = false, AttributesToSkip = 0, IgnoreInaccessible = false };
        IEnumerable<FileSystemInfo> entries = skipTopFolder is null
            ? dir.EnumerateFileSystemInfos("*", options)
            : dir.EnumerateFileSystemInfos("*", top)
                 .Where(e => !(e is DirectoryInfo && string.Equals(e.Name, skipTopFolder, StringComparison.OrdinalIgnoreCase)))
                 .SelectMany(e => e is DirectoryInfo d ? [e, .. d.EnumerateFileSystemInfos("*", options)] : (IEnumerable<FileSystemInfo>)[e]);
        return entries
            .Select(e => string.Create(CultureInfo.InvariantCulture,
                $"{Path.GetRelativePath(root, e.FullName)}|{(e is FileInfo f ? f.Length : -1)}|{e.LastWriteTimeUtc.Ticks}|{(int)e.Attributes}"))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
