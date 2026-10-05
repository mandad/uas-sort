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

    [Fact]
    public void OnlyTheWindowsPhotoRootRecycler_RecyclesUnderThePhotoRoot()
    {
        static IEnumerable<string> Code(string f)
            => File.ReadLines(f).Select(l => l.IndexOf("//", StringComparison.Ordinal) is var c and >= 0 ? l[..c] : l);
        static HashSet<string> Files(params string[] rel) => rel.Select(RepoPaths.Of).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var recycler = Files("src/UasSort.Platform/Io/WindowsPhotoRootRecycler.cs", "src/UasSort.Platform/Win32/FileOperationCom.cs");
        string[] markers = ["IFileOperation", "SHFileOperation", "FOFX_RECYCLEONDELETE", "FofxRecycleOnDelete", "FOF_ALLOWUNDO", "FofAllowUndo",
                            "SHCreateItemFromParsingName", "RecycleOption"];
        var movers = SourceFiles("src").Where(f => !recycler.Contains(Path.GetFullPath(f)))
            .Where(f => Code(f).Any(l => markers.Any(m => l.Contains(m, StringComparison.Ordinal))))
            .Select(RepoPaths.Relative).ToList();
        Assert.True(movers.Count == 0, "Recycle Bin moves outside WindowsPhotoRootRecycler: " + string.Join(", ", movers));

        var guarded = Files("src/UasSort.Core/Guard/IoGuardPolicy.cs", "src/UasSort.Platform/Io/WindowsPhotoRootRecycler.cs");
        var opUsers = SourceFiles("src").Where(f => !guarded.Contains(Path.GetFullPath(f)))
            .Where(f => Code(f).Any(l => l.Contains("IoOp.PhotoRootRecycle", StringComparison.Ordinal)))
            .Select(RepoPaths.Relative).ToList();
        Assert.True(opUsers.Count == 0, "IoOp.PhotoRootRecycle used outside the guard and the recycler: " + string.Join(", ", opUsers));
        Assert.Single(File.ReadAllLines(RepoPaths.Of("src/UasSort.Platform/Io/WindowsPhotoRootRecycler.cs")),
                      l => l.Contains("IoGate.Require(IoOp.PhotoRootRecycle,", StringComparison.Ordinal));

        // CardClassifier only *skips* a card's own $RECYCLE.BIN folder (Ref §5 system rule); it never touches the PC's Recycle Bin.
        var purge = Files("src/UasSort.Platform/Stores/RecycleBinPurge.cs", "src/UasSort.Core/Card/CardClassifier.cs");
        var binTouchers = SourceFiles("src").Where(f => !purge.Contains(Path.GetFullPath(f)))
            .Where(f => Code(f).Any(l => l.Contains("$Recycle.Bin", StringComparison.OrdinalIgnoreCase)))
            .Select(RepoPaths.Relative).ToList();
        Assert.True(binTouchers.Count == 0, "Recycle Bin contents touched outside RecycleBinPurge: " + string.Join(", ", binTouchers));

        var purgeCallers = Files("src/UasSort.Platform/Stores/RecycleBinPurge.cs", "src/UasSort.Platform/Stores/SelfTestSandbox.RecycleBin.cs");
        var callers = SourceFiles("src").Where(f => !purgeCallers.Contains(Path.GetFullPath(f)))
            .Where(f => Code(f).Any(l => l.Contains("RecycleBinPurge", StringComparison.Ordinal)))
            .Select(RepoPaths.Relative).ToList();
        Assert.True(callers.Count == 0, "RecycleBinPurge reached outside SelfTestSandbox.PurgeOwnRecycleBinItems: " + string.Join(", ", callers));
    }
}
