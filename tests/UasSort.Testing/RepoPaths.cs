namespace UasSort.Testing;

/// <summary>Locates the repository (the folder holding uas-sort.slnx) from a test's output folder.</summary>
public static class RepoPaths
{
    private static readonly Lazy<string> RootLazy = new(FindRoot);
    private static readonly HashSet<string> SkippedFolders =
        new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", "artifacts", "node_modules", "TestResults" };

    public static string Root => RootLazy.Value;

    public static string Of(string relativePath) => Path.GetFullPath(Path.Join(Root, relativePath));

    /// <summary>Files matching <paramref name="searchPattern"/> under the given top folders (repo-relative).</summary>
    public static IEnumerable<string> EnumerateFiles(string searchPattern, params string[] topFolders)
    {
        foreach (var top in topFolders)
        {
            var start = Of(top);
            if (!Directory.Exists(start))
            {
                continue;
            }

            var pending = new Stack<string>();
            pending.Push(start);
            while (pending.Count > 0)
            {
                var dir = pending.Pop();
                foreach (var file in Directory.EnumerateFiles(dir, searchPattern))
                {
                    yield return file;
                }

                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    if (!SkippedFolders.Contains(Path.GetFileName(sub)))
                    {
                        pending.Push(sub);
                    }
                }
            }
        }
    }

    public static string Relative(string fullPath) => Path.GetRelativePath(Root, fullPath).Replace('\\', '/');

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Join(dir.FullName, "uas-sort.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("uas-sort.slnx not found above " + AppContext.BaseDirectory);
    }
}
