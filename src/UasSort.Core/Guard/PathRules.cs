namespace UasSort.Core;

/// <summary>Windows-style path comparison shared by the guard, the validator and the fakes.
/// Pure string logic: '\' or '/', case-insensitive, "\\?\" stripped, no trailing separator except on a drive root.</summary>
public static class PathRules
{
    public static string Normalize(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var p = path.Replace('/', '\\');
        if (p.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) p = @"\\" + p[8..];
        else if (p.StartsWith(@"\\?\", StringComparison.Ordinal)) p = p[4..];
        while (p.Length > 1 && p.EndsWith('\\') && !IsDriveRoot(p)) p = p[..^1];
        if (p.Length == 2 && p[1] == ':') p += "\\";
        return p;
    }

    public static bool Equal(string a, string b)
        => string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    public static bool IsSameOrUnder(string path, string root)
    {
        var p = Normalize(path);
        var r = Normalize(root);
        if (string.Equals(p, r, StringComparison.OrdinalIgnoreCase)) return true;
        var prefix = r.EndsWith('\\') ? r : r + "\\";
        return p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsStrictlyUnder(string path, string root) => IsSameOrUnder(path, root) && !Equal(path, root);

    public static bool Overlaps(string a, string b) => IsSameOrUnder(a, b) || IsSameOrUnder(b, a);

    public static string? Parent(string path)
    {
        var p = Normalize(path);
        if (IsDriveRoot(p)) return null;
        var i = p.LastIndexOf('\\');
        if (i < 0) return null;
        if (i == 2 && p[1] == ':') return p[..3];
        return i <= 1 ? null : p[..i];
    }

    public static string FileName(string path)
    {
        var p = Normalize(path);
        var i = p.LastIndexOf('\\');
        return i < 0 ? p : p[(i + 1)..];
    }

    public static string Join(string root, string relative)
    {
        ArgumentNullException.ThrowIfNull(relative);
        var r = Normalize(root);
        var rel = relative.Replace('/', '\\').TrimStart('\\');
        if (rel.Length == 0) return r;
        return Normalize(r.EndsWith('\\') ? r + rel : r + "\\" + rel);
    }

    /// <summary>The '/'-separated path of <paramref name="path"/> relative to <paramref name="root"/>.</summary>
    public static string RelativeCardPath(string path, string root)
    {
        if (!IsSameOrUnder(path, root)) throw new ArgumentException($"{path} is not under {root}", nameof(path));
        var p = Normalize(path);
        var r = Normalize(root);
        if (p.Length == r.Length) return "";
        var start = r.EndsWith('\\') ? r.Length : r.Length + 1;
        return p[start..].Replace('\\', '/');
    }

    public static bool SetContains(IEnumerable<string> set, string path)
    {
        ArgumentNullException.ThrowIfNull(set);
        foreach (var s in set)
            if (Equal(s, path)) return true;
        return false;
    }

    private static bool IsDriveRoot(string p) => p.Length == 3 && p[1] == ':' && p[2] == '\\';
}
