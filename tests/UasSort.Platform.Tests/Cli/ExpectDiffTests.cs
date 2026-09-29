using UasSort.Cli;

namespace UasSort.Platform.Tests.Cli;

public sealed class ExpectDiffTests
{
    private static string Clip(int n, string day = "20260726") => $"DJI_{day}120000_{n:0000}_D.MP4";

    private static GroupJson Group(string relPath, GroupTargetKind kind, params string[] clipNames) =>
        new($"DCIM/DJI_001/{clipNames[0]}", kind, relPath, kind == GroupTargetKind.Append ? Confidence.High : null, null,
            new DateOnly(2026, 7, 25), new DateOnly(2026, 7, 26), null,
            clipNames.Select(c => CliSamples.Video($"DCIM/DJI_001/{c}")).ToList(), [], []);

    private static ExpectedFolderJson Folder(string relPath, params string[] clips) => new(relPath, clips, null);

    private const string Council = @"2026\2026-07\2026-07-25 Council Road";
    private const string Anvil = @"2026\2026-07\2026-07-26 Anvil Mountain";

    [Fact]
    public void ExactMatch_IsZeroEdits()
    {
        var groups = new[] { Group(Council, GroupTargetKind.NewFolder, Clip(1), Clip(2)), Group(Anvil, GroupTargetKind.NewFolder, Clip(3)) };
        var expected = new ExpectedFileJson([Folder(Council, Clip(1), Clip(2)), Folder(Anvil, Clip(3))]);
        var r = ExpectDiff.Compare(expected, groups);
        Assert.Equal(0, r.Edits);
        Assert.Empty(r.Differences);
        Assert.True(r.Passes);
    }

    [Fact]
    public void CouncilAnvilMerged_IsOneSplitPlusOneRename_AndPasses()
    {
        // Scenario D shape: the plan appends Anvil's clips to Council Road; the user expects two folders.
        var groups = new[] { Group(Council, GroupTargetKind.Append, Clip(1), Clip(2), Clip(3), Clip(4)) };
        var expected = new ExpectedFileJson([Folder(Council, Clip(1), Clip(2)), Folder(Anvil, Clip(3), Clip(4))]);
        var r = ExpectDiff.Compare(expected, groups);
        Assert.Equal(2, r.Edits);
        Assert.Contains(r.Differences, d => d.Kind == ExpectDifferenceKind.SplitOrMove && d.Edits == 1);
        Assert.Contains(r.Differences, d => d.Kind == ExpectDifferenceKind.RenameOrRetarget && d.Subject == Anvil);
        Assert.True(r.Passes);
    }

    [Fact]
    public void OneExpectedFolderInTwoGroups_IsOneMerge()
    {
        var groups = new[] { Group(Council, GroupTargetKind.NewFolder, Clip(1)), Group(@"2026\2026-07\2026-07-26", GroupTargetKind.NewFolder, Clip(2)) };
        var r = ExpectDiff.Compare(new ExpectedFileJson([Folder(Council, Clip(1), Clip(2))]), groups);
        Assert.Equal(1, r.Edits);
        Assert.Equal(ExpectDifferenceKind.Merge, Assert.Single(r.Differences).Kind);
    }

    [Fact]
    public void DifferentDescription_IsOneRename()
    {
        var groups = new[] { Group(@"2026\2026-07\2026-07-26", GroupTargetKind.NewFolder, Clip(3)) };
        var r = ExpectDiff.Compare(new ExpectedFileJson([Folder(Anvil, Clip(3))]), groups);
        Assert.Equal(1, r.Edits);
        var d = Assert.Single(r.Differences);
        Assert.Equal(ExpectDifferenceKind.RenameOrRetarget, d.Kind);
        Assert.Contains(@"2026\2026-07\2026-07-26", d.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpectedTargetKind_IsCompared()
    {
        var groups = new[] { Group(Council, GroupTargetKind.Append, Clip(1)) };
        var r = ExpectDiff.Compare(new ExpectedFileJson([new ExpectedFolderJson(Council, [Clip(1)], GroupTargetKind.NewFolder)]), groups);
        Assert.Equal(1, r.Edits);
        Assert.Equal(0, ExpectDiff.Compare(new ExpectedFileJson([new ExpectedFolderJson(Council, [Clip(1)], GroupTargetKind.Append)]), groups).Edits);
    }

    [Fact]
    public void CaseAndSeparators_DoNotCount()
    {
        var groups = new[] { Group(Council, GroupTargetKind.NewFolder, Clip(1)) };
        var expected = new ExpectedFileJson([Folder("2026/2026-07/2026-07-25 council road/", Clip(1).ToLowerInvariant())]);
        Assert.Equal(0, ExpectDiff.Compare(expected, groups).Edits);
    }

    [Fact]
    public void ClipsNotOnCard_AreListed_WithoutEdits_AndUnlistedCardClipsAreIgnored()
    {
        var groups = new[] { Group(Council, GroupTargetKind.NewFolder, Clip(1), Clip(9)) };
        var r = ExpectDiff.Compare(new ExpectedFileJson([Folder(Council, Clip(1), Clip(7))]), groups);
        Assert.Equal(0, r.Edits);
        var d = Assert.Single(r.Differences);
        Assert.Equal(ExpectDifferenceKind.NotOnCard, d.Kind);
        Assert.Equal(Clip(7), d.Subject);
    }

    [Fact]
    public void AClipListedTwice_IsReported()
    {
        var groups = new[] { Group(Council, GroupTargetKind.NewFolder, Clip(1)) };
        var r = ExpectDiff.Compare(new ExpectedFileJson([Folder(Council, Clip(1)), Folder(Anvil, Clip(1))]), groups);
        Assert.Contains(r.Differences, d => d.Kind == ExpectDifferenceKind.ListedTwice && d.Subject == Clip(1));
    }

    [Fact]
    public void ThreeEdits_Fail()
    {
        var groups = new[] { Group("a", GroupTargetKind.NewFolder, Clip(1), Clip(2), Clip(3)) };
        var r = ExpectDiff.Compare(new ExpectedFileJson([Folder("x", Clip(1)), Folder("y", Clip(2)), Folder("z", Clip(3))]), groups);
        Assert.Equal(5, r.Edits);   // 2 splits + 3 renames
        Assert.False(r.Passes);
    }

    [Fact]
    public void Write_EndsWithTheEditCountAndVerdict()
    {
        var groups = new[] { Group(Council, GroupTargetKind.Append, Clip(1), Clip(2), Clip(3), Clip(4)) };
        var r = ExpectDiff.Compare(new ExpectedFileJson([Folder(Council, Clip(1), Clip(2)), Folder(Anvil, Clip(3), Clip(4))]), groups);
        var w = new StringWriter();
        ExpectDiff.Write(r, w);
        string[] lines = w.ToString().TrimEnd().Split(Environment.NewLine);
        Assert.Equal("EXPECT edits: 2 (passes at <= 2): PASS", lines[^1]);
        Assert.Contains(lines, l => l.StartsWith("EXPECT split/move:", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("EXPECT rename/retarget:", StringComparison.Ordinal));
    }
}
