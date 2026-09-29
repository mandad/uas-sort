using UasSort.Core.Planning;
using UasSort.Testing.Planning;
using static UasSort.Testing.Planning.ReplayTruth;

namespace UasSort.Core.Tests.Planning;

public sealed class GoldenReplayTests
{
    private const string Council = "2026-07-25 Council Road";
    private const string Anvil = "2026-07-26 Anvil Mountain";
    private const string Zachar = "2026-09-27 Zachar Bay";
    private static readonly ReplayFixture F = ReplayFixture.Load();

    private static bool InFolder(ReplayEntry e, string leaf) => e.RelPath.Contains($"/{leaf}/", StringComparison.Ordinal);
    private static string Full(string rel) => $@"{PlanScenario.VideoRoot}\{rel}";
    private static VideoGroup GroupWith(Plan p, string leaves) => p.Groups.Single(g => Leaves(F, g) == leaves);

    private static ItemId FirstAnvil(PlanBase b)
    {
        var anvil = In(F, Anvil).Select(ReplayFixture.NameOf).ToHashSet();
        return b.Items.First(i => anvil.Contains(i.Raw.Name)).Raw.Unit.Id;          // Items are in (CaptureUtc, Id) order
    }

    [Fact] // A0: clustering alone
    public void A0_EveryClipEmptyLibrary_SevenGroups_SplitGivesTheUsersEight()
    {
        var b = F.Scenario(F.Mp4s, _ => false).Prepare();
        var p = PlanScenario.Derive(b);

        Assert.Equal(7, p.Groups.Length);
        Assert.All(p.Groups, g => Assert.IsType<NewFolder>(g.Target));
        Assert.Equal(7, p.Issues.Count(i => i.Code == IssueCode.EmptyFolderName && i.Severity == IssueSeverity.Blocking));
        Assert.Equal(Expected(F, l => l == Anvil ? Council : l), Partition(p));

        var ca = GroupWith(p, $"{Council}+{Anvil}");
        Assert.Equal(25, ca.Videos.Length);
        var ds = Assert.Single(ca.DaySplits);
        Assert.True(ds.Emphasised);
        Assert.Equal((new DateOnly(2026, 7, 25), new DateOnly(2026, 7, 26)), (ds.From, ds.To));
        var w = Assert.Single(p.Issues, i => i.Code == IssueCode.EmphasisedDaySplit);
        Assert.True(w.RequiresAckAtPreflight);
        Assert.Equal(ds.FirstOfDay, w.Anchor);

        Assert.Equal(new NewFolder(@"2026\2026-09\2026-09-27"), GroupWith(p, Zachar).Target);
        Assert.Equal(new NewFolder(@"2026\2026-07\2026-07-25"), ca.Target);
        Assert.Equal(new NewFolder(@"2026\2026-05\2026-05-22"), GroupWith(p, "2026-05-22 Kodiak").Target);
        Assert.Equal(new NewFolder(@"2022\2022-03\2022-03-27"), GroupWith(p, "2022-03-27 Makaha Valley").Target);

        var split = PlanScenario.Derive(b, edits: [new SplitBefore(FirstAnvil(b))]);
        Assert.Equal(8, split.Groups.Length);
        Assert.Equal(Expected(F, l => l), Partition(split));
        var anvilIdx = split.Groups.IndexOf(GroupWith(split, Anvil));
        Assert.Equal(BoundaryCause.UserSplit, split.Boundaries[anvilIdx - 1].Cause);
    }

    [Theory] // A0 R sweep (replay50.py: 8 groups for R 8–33 mi, 7 for R 40–60 mi)
    [InlineData(8, 8)]
    [InlineData(10, 8)]
    [InlineData(20, 8)]
    [InlineData(30, 8)]
    [InlineData(33, 8)]
    [InlineData(40, 7)]
    [InlineData(50, 7)]
    [InlineData(60, 7)]
    public void A0_RadiusSweep(int radiusMiles, int groups)
    {
        var b = F.Scenario(F.Mp4s, _ => false).Prepare();
        Assert.Equal(groups, PlanScenario.Derive(b, new Tuning(radiusMiles, 1)).Groups.Length);
    }

    [Fact] // A: card = library
    public void A_CardEqualsLibrary_EightAlreadyImported_AnvilUnfolded()
    {
        var b = F.Scenario(F.Mp4s, _ => true).Prepare();
        var p = PlanScenario.Derive(b);
        Assert.Equal(8, p.Groups.Length);
        Assert.All(p.Groups, g => Assert.Equal(Leaves(F, g), Leaf(Assert.IsType<AlreadyImported>(g.Target).Folder.FullPath.Replace('\\', '/'))));
        Assert.Equal(7, p.Groups.Count(g => g.Foldable));
        var anvil = GroupWith(p, Anvil);
        Assert.False(anvil.Foldable);
        Assert.Equal(2, anvil.Videos.Count(v => b.Items.Single(i => i.Raw.Unit.Id == v).Flags.HasFlag(ItemFlags.Truncated)));
        Assert.Equal(new DateTime(2026, 9, 27, 18, 24, 16, DateTimeKind.Utc), b.WatermarkUtc);
    }

    [Fact] // B: library minus the Zachar Bay folder
    public void B_WithoutZacharFolder_NewFolderOf13()
    {
        var p = PlanScenario.Derive(F.Scenario(F.Mp4s, e => !InFolder(e, Zachar)).Prepare());
        Assert.Equal(7, p.Groups.Count(g => g.Target is AlreadyImported));
        var z = GroupWith(p, Zachar);
        Assert.Equal((new NewFolder(@"2026\2026-09\2026-09-27"), 13), (z.Target, z.Videos.Length));
        Assert.Contains(p.Issues, i => i.Code == IssueCode.EmptyFolderName && i.Severity == IssueSeverity.Blocking && i.Anchor == z.Id.Anchor);
    }

    [Fact] // C: library minus Zachar 0140–0148
    public void C_WithoutLastZacharClips_AppendHigh()
    {
        static bool Late(ReplayEntry e)
        {
            var name = ReplayFixture.NameOf(e);
            return InFolder(e, Zachar) && name.StartsWith("DJI_", StringComparison.Ordinal) && name.Length > 23
                   && int.Parse(name.AsSpan(19, 4), System.Globalization.CultureInfo.InvariantCulture) is >= 140 and <= 148;
        }
        var b = F.Scenario(F.Mp4s, e => !Late(e)).Prepare();
        var p = PlanScenario.Derive(b);
        Assert.Equal(7, p.Groups.Count(g => g.Target is AlreadyImported));
        var z = GroupWith(p, Zachar);
        var a = Assert.IsType<Append>(z.Target);
        Assert.Equal((Full(@"2026\2026-09\2026-09-27 Zachar Bay"), Confidence.High, "same day as clips already in this folder"),
                     (a.Folder.FullPath, a.Confidence, a.Why));
        Assert.Equal(13, z.Videos.Length);
        Assert.Equal(4, z.Videos.Count(v => b.Items.Single(i => i.Raw.Unit.Id == v).Newness is IsNew));
    }

    [Fact] // D: Council leftovers + Anvil clips, library lacks Anvil Mountain
    public void D_CouncilLeftovers_AppendMediumWithHint_EitherClickSplits()
    {
        var b = F.Scenario(In(F, Council).Concat(In(F, Anvil)), e => !InFolder(e, Anvil)).Prepare();
        var p = PlanScenario.Derive(b);
        var g = Assert.Single(p.Groups);
        Assert.Equal(25, g.Videos.Length);
        var a = Assert.IsType<Append>(g.Target);
        Assert.Equal((Full(@"2026\2026-07\2026-07-25 Council Road"), Confidence.Medium, "different day, 34 mi from Council Road"),
                     (a.Folder.FullPath, a.Confidence, a.Why));
        var firstAnvil = FirstAnvil(b);
        Assert.Equal(new SplitBefore(firstAnvil), Assert.Single(a.Hint!.Fix));
        Assert.True(Assert.Single(p.Issues, i => i.Code == IssueCode.MediumAppend).RequiresAckAtPreflight);
        var split = Assert.Single(p.Issues, i => i.Code == IssueCode.EmphasisedDaySplit);
        Assert.True(split.RequiresAckAtPreflight);

        foreach (var fix in new[] { a.Hint!.Fix, split.QuickFixes[0].Edits })
        {
            var after = PlanScenario.Derive(b, edits: [.. fix]);
            Assert.Equal(2, after.Groups.Length);
            Assert.Equal("Council Road", Assert.IsType<AlreadyImported>(after.Groups[0].Target).Folder.Description);
            Assert.Equal(new NewFolder(@"2026\2026-07\2026-07-26"), after.Groups[1].Target);
            Assert.Contains(after.Issues, i => i.Code == IssueCode.EmptyFolderName && i.Anchor == after.Groups[1].Id.Anchor);
        }
    }

    [Fact] // E: Anvil clips only, library lacks Anvil Mountain
    public void E_AnvilOnly_NewFolder_CouncilOffered()
    {
        var p = PlanScenario.Derive(F.Scenario(In(F, Anvil), e => !InFolder(e, Anvil)).Prepare());
        var g = Assert.Single(p.Groups);
        Assert.Equal(new NewFolder(@"2026\2026-07\2026-07-26"), g.Target);
        Assert.Contains(p.Issues, i => i.Code == IssueCode.EmptyFolderName);
        Assert.Contains(Planner.AppendCandidates(p, g.Id), f => f.Ref.Description == "Council Road");
    }
}
