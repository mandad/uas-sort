using System.Globalization;

namespace UasSort.Platform.Tests.Cli;

/// <summary>A listing of every entry under a root: relative path, kind, size, last-write ticks, attributes. Opens no file.</summary>
internal static class TreeSnapshot
{
    public static IReadOnlyList<string> Take(string root)
    {
        var dir = new DirectoryInfo(root);
        if (!dir.Exists) return ["(absent)"];
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = false };
        return dir.EnumerateFileSystemInfos("*", options)
            .Select(e => string.Create(CultureInfo.InvariantCulture,
                $"{Path.GetRelativePath(root, e.FullName)}|{(e is FileInfo f ? f.Length : -1)}|{e.LastWriteTimeUtc.Ticks}|{(int)e.Attributes}"))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
