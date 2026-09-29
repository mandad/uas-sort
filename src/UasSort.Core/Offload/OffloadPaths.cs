using System.Collections.Immutable;
using System.Globalization;

namespace UasSort.Core.Offload;

/// <summary>Pure path text for the offload (Windows syntax; never touches the file system).</summary>
public static class OffloadPaths
{
    public const string TempSuffix = ".uas-sort.tmp";
    public const int MaxTempPathLength = 400;

    public static string Join(string dir, string relative)
        => dir.TrimEnd('\\', '/') + "\\" + relative.Replace('/', '\\').Trim('\\');

    public static string FileName(string path)
    {
        int i = path.LastIndexOfAny(['\\', '/']);
        return i < 0 ? path : path[(i + 1)..];
    }

    public static string DirectoryOf(string fullPath)
    {
        int i = fullPath.LastIndexOf('\\');
        return i < 0 ? "" : fullPath[..i];
    }

    public static string TempOf(string finalPath) => finalPath + TempSuffix;

    public static bool IsTemp(string path) => path.EndsWith(TempSuffix, StringComparison.OrdinalIgnoreCase);

    public static string VolumeRoot(string fullPath)
    {
        var p = fullPath.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) ? @"\\" + fullPath[8..]
              : fullPath.StartsWith(@"\\?\", StringComparison.Ordinal) ? fullPath[4..]
              : fullPath;
        if (p.Length >= 2 && p[1] == ':') return char.ToUpperInvariant(p[0]) + @":\";
        if (p.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var parts = p[2..].Split('\\');
            if (parts.Length >= 2) return $@"\\{parts[0]}\{parts[1]}\";
        }
        return p;
    }

    public static string DriveLabel(string volumeRoot) => volumeRoot.TrimEnd('\\');

    public static bool Same(string a, string b) => PathRules.Equal(a, b);

    public static bool IsUnder(string path, string root) => PathRules.IsSameOrUnder(path, root);

    public static string NormRel(string relPath) => relPath.Replace('\\', '/');

    public static string NormName(string fileName) => FileKey.NormalizeName(FileName(fileName));

    public static FileKey Key(string pathOrName, long size) => FileKey.OfPath(pathOrName, size);

    public static string WithCopyNumber(string fileName, int n)
    {
        int dot = fileName.LastIndexOf('.');
        return dot <= 0 ? $"{fileName} ({n})" : $"{fileName[..dot]} ({n}){fileName[dot..]}";
    }

    public static ImmutableArray<string> NewFolderDirs(string videoRoot, string folder)
    {
        var root = PathRules.Normalize(videoRoot);
        var target = PathRules.Normalize(folder);
        if (!PathRules.IsSameOrUnder(target, root) || PathRules.Equal(target, root)) return [target];
        var dirs = ImmutableArray.CreateBuilder<string>();
        var current = root.TrimEnd('\\');
        foreach (var part in target[(root.TrimEnd('\\').Length + 1)..].Split('\\'))
        {
            current += "\\" + part;
            dirs.Add(current);
        }
        return dirs.ToImmutable();
    }

    public static string Gb(long bytes) => (bytes / 1e9).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
}
