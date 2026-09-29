// tests/UasSort.Core.Tests/Naming/FolderNamerTests.cs
using UasSort.Core.Naming;

namespace UasSort.Core.Tests.Naming;

public sealed class FolderNamerTests
{
    // Ported test #37 (test_sanitize_and_naming → Clean_And_NewFolderRel)
    [Fact]
    public void Clean_And_NewFolderRel()
    {
        Assert.Equal("Newport, RI", FolderNamer.Clean("Newport, RI"));
        Assert.Equal("A B test", FolderNamer.Clean(" A/B: \"test\"?  "));
        Assert.Equal("Nome Rd", FolderNamer.Clean("Nome Rd..."));
        Assert.Equal(@"2026\2026-09\2026-09-27", FolderNamer.NewFolderRel(new DateOnly(2026, 9, 27), "  "));
        Assert.Equal(@"2026\2026-07\2026-07-25 Anvil", FolderNamer.NewFolderRel(new DateOnly(2026, 7, 25), "Anvil"));
    }

    [Fact]
    public void Clean_ReplacesControlAndReservedCharacters()
    {
        Assert.Equal("a b c d", FolderNamer.Clean("a<b>c|d"));
        Assert.Equal("tab here", FolderNamer.Clean("tab\there"));
        Assert.Equal("x y", FolderNamer.Clean("x\u0001y"));
        Assert.Equal("", FolderNamer.Clean(" . . "));
    }

    [Fact] // [Review Focus] #2: Unicode letters and emoji are kept; trailing dots/spaces stripped
    public void Clean_KeepsUnicodeAndEmoji_StripsTrailingDotsAndSpaces()
    {
        Assert.Equal("Café Ñandú", FolderNamer.Clean("Café Ñandú. . "));
        Assert.Equal("Sunset 🌅 Kodiak", FolderNamer.Clean("  Sunset   🌅 Kodiak... "));
        Assert.Equal("Øksfjord – 北海道", FolderNamer.Clean("Øksfjord – 北海道 ."));
    }

    [Fact] // [Review Focus] #2: the 80-char cap never splits a surrogate pair or leaves a trailing dot
    public void Clean_TruncatesAt80WithoutSplittingEmoji()
    {
        var s = new string('a', 79) + "🌅";                     // 79 + 2 UTF-16 units
        var cleaned = FolderNamer.Clean(s);
        Assert.Equal(new string('a', 79), cleaned);
        Assert.Equal(80, FolderNamer.Clean(new string('b', 100)).Length);
        Assert.Equal(new string('c', 77), FolderNamer.Clean(new string('c', 77) + "..." + new string('d', 10)).TrimEnd('d'));
    }

    [Fact]
    public void TempPath_AddsSuffix()
        => Assert.Equal(@"C:\v\x.MP4.uas-sort.tmp", FolderNamer.TempPath(@"C:\v\x.MP4"));

    [Fact] // Conflict (n) naming (Ref §7.4): stem (2).ext or the next free (n)
    public void ConflictName_PicksNextFreeCounter()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "X (2).DNG", "x (3).dng" };
        Assert.Equal("X (4).DNG", FolderNamer.ConflictName("X.DNG", taken.Contains));
        Assert.Equal("clip (2).MP4", FolderNamer.ConflictName("clip.MP4", _ => false));
        Assert.Equal("X (4).JPG", FolderNamer.TwinName("X (4).DNG", "X.JPG"));
    }
}
