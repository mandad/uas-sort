using System.Text.RegularExpressions;

namespace UasSort.Platform.Tests;

public sealed class SourceGuardTests
{
    private static IEnumerable<string> SourceFiles(string topFolder) => RepoPaths.EnumerateFiles("*.cs", topFolder);

    [Fact]
    public void DeleteFileW_And_RemoveDirectoryW_AreDeclaredOnlyInWindowsCardEraser()
    {
        var eraser = RepoPaths.Of("src/UasSort.Platform/Card/WindowsCardEraser.cs");
        var offenders = SourceFiles("src")
            .Where(f => !string.Equals(Path.GetFullPath(f), eraser, StringComparison.OrdinalIgnoreCase))
            .Where(f => File.ReadLines(f).Select(l => l.IndexOf("//", StringComparison.Ordinal) is var c and >= 0 ? l[..c] : l).Any(l => l.Contains("DeleteFileW", StringComparison.Ordinal) || l.Contains("RemoveDirectoryW", StringComparison.Ordinal)))
            .Select(RepoPaths.Relative)
            .ToList();
        Assert.True(offenders.Count == 0, "Card deletes outside WindowsCardEraser: " + string.Join(", ", offenders));

        var lines = File.ReadAllLines(eraser);
        var calls = lines.Select((l, i) => (l, i)).Where(x => x.l.Contains("= DeleteFileW(", StringComparison.Ordinal)
                                                             || x.l.Contains("= RemoveDirectoryW(", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, calls.Count);
        Assert.All(calls, c => Assert.Contains("// IO layer: Card cleanup, confirmed plan only", lines[c.i - 1], StringComparison.Ordinal));
    }

    [Fact]
    public void EveryRs0030Suppression_SaysWhyItIsAllowed()
    {
        var bad = SourceFiles("src")
            .SelectMany(f => File.ReadAllLines(f).Select((l, i) => (File: f, Line: i + 1, Text: l.Trim())))
            .Where(x => x.Text.StartsWith("#pragma warning disable RS0030", StringComparison.Ordinal)
                        && !Regex.IsMatch(x.Text, @"^#pragma warning disable RS0030 // IO layer: \S.{4,}$"))
            .Select(x => $"{RepoPaths.Relative(x.File)}:{x.Line}")
            .ToList();
        Assert.True(bad.Count == 0, "Unjustified RS0030 suppressions: " + string.Join(", ", bad));
        Assert.DoesNotContain(SourceFiles("src/UasSort.Core"),
                              f => File.ReadAllText(f).Contains("disable RS0030", StringComparison.Ordinal));
    }
}
