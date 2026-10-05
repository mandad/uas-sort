// src/UasSort.Core/Cleanup/Photos/PhotoCleanupPaths.cs
namespace UasSort.Core.Cleanup;

/// <summary>What the recycler moves for a row (spec 2026-10-04 §5): a photo row's files, each directly in the photo root, or a set row's
/// folder, directly in the photo root with its members directly inside. Anything else throws (a VM or planner bug).</summary>
public static class PhotoCleanupPaths
{
    public static string Full(string photoRoot, string relPath) => PathRules.Join(photoRoot, relPath);

    public static ImmutableArray<string> Targets(string photoRoot, PhotoItem item)
    {
        ArgumentNullException.ThrowIfNull(photoRoot);
        ArgumentNullException.ThrowIfNull(item);
        Plain(item.RelPath);
        if (item.Members.IsDefaultOrEmpty) throw new InvalidOperationException($"{item.RelPath} has no files");
        if (item.Kind == PhotoItemKind.Photo)
        {
            foreach (var m in item.Members) Plain(m.RelPath);
            if (!string.Equals(item.Primary.RelPath, item.RelPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{item.RelPath}: the row is not named after its primary file");
            return [.. item.Members.Select(m => Full(photoRoot, m.RelPath))];
        }
        foreach (var m in item.Members)
        {
            var parts = m.RelPath.Split('\\');
            if (parts.Length != 2 || !string.Equals(parts[0], item.RelPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{m.RelPath} is not directly in the set folder {item.RelPath}");
            Plain(parts[1]);
        }
        return [Full(photoRoot, item.RelPath)];
    }

    private static void Plain(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment) || segment is "." or ".." || segment.IndexOfAny(['\\', '/', ':']) >= 0
            || segment.EndsWith('.') || segment.EndsWith(' '))
            throw new InvalidOperationException($"Not a plain name in the photo folder: '{segment}'");
    }
}
