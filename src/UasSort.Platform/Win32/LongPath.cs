namespace UasSort.Platform.Win32;

/// <summary>The \\?\ prefix on every path handed to a P/Invoke (Ref §4.2 Platform row, §13 300-character test).</summary>
internal static class LongPath
{
    private const string Extended = @"\\?\";
    private const string ExtendedUnc = @"\\?\UNC\";

    public static string Prefix(string fullPath)
    {
        if (fullPath.StartsWith(Extended, StringComparison.Ordinal)) return fullPath;
        if (!Path.IsPathFullyQualified(fullPath)) throw new ArgumentException($"Not a full path: {fullPath}", nameof(fullPath));
        return fullPath.StartsWith(@"\\", StringComparison.Ordinal) ? ExtendedUnc + fullPath[2..] : Extended + fullPath;
    }

    public static string Strip(string path)
    {
        if (path.StartsWith(ExtendedUnc, StringComparison.OrdinalIgnoreCase)) return @"\\" + path[ExtendedUnc.Length..];
        if (path.StartsWith(Extended, StringComparison.Ordinal)) return path[Extended.Length..];
        return path;
    }
}
