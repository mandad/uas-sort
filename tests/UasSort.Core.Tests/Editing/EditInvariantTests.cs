// tests/UasSort.Core.Tests/Editing/EditInvariantTests.cs
using Microsoft.Extensions.Time.Testing;
using UasSort.Core.Editing;
using UasSort.Testing.Planning;

namespace UasSort.Core.Tests.Editing;

public sealed class EditInvariantTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PlanSession NewSession(PlanBase b) =>
        new(b, PlanScenario.CreatePlanner(), new Tuning(50, 1), new FakeTimeProvider(new DateTimeOffset(PlanScenario.NowUtc)));

    private static void AssertPartition(Plan p, IReadOnlyCollection<ItemId> videos)
    {
        Assert.All(p.Groups, g => Assert.NotEmpty(g.Videos));
        var all = p.Groups.SelectMany(g => g.Videos).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());                 // exactly one group each
        Assert.True(all.ToHashSet().SetEquals(videos));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task RandomEditSequences_KeepInvariants(int seed)
    {
        var rnd = new Random(seed);
        var b = InvariantScenario.Build().Prepare();
        var videos = b.Items.Where(i => i.Raw.Kind == ItemKind.Video).Select(i => i.Raw.Unit.Id).ToList();
        var dates = b.Items.ToDictionary(i => i.Raw.Unit.Id, i => i.Time.LocalDate);
        var s = NewSession(b);

        for (var step = 0; step < 40; step++)
        {
            var p = s.Current;
            VideoGroup G() => p.Groups[rnd.Next(p.Groups.Length)];
            switch (rnd.Next(10))
            {
                case 0 when p.Groups.Length > 1:
                {
                    var i = rnd.Next(p.Groups.Length - 1);
                    await s.ApplyAsync(new Merge(p.Groups[i].Id.Anchor, p.Groups[i + 1].Id.Anchor), Ct);
                    break;
                }
                case 1:
                {
                    var g = G();
                    if (g.Videos.Length > 1) await s.ApplyAsync(new SplitBefore(g.Videos[1 + rnd.Next(g.Videos.Length - 1)]), Ct);
                    break;
                }
                case 2:
                    await s.ApplyAsync(new MoveToNewGroup([videos[rnd.Next(videos.Count)]]), Ct);
                    break;
                case 3:
                    await s.ApplyAsync(new MoveToGroup([videos[rnd.Next(videos.Count)]], G().Id.Anchor), Ct);
                    break;
                case 4:
                {
                    var g = G();
                    await s.ApplyAsync(new Rename(g.Id.Anchor, $"Name {rnd.Next(3)}", g.Videos), Ct);
                    break;
                }
                case 5:
                {
                    var g = G();
                    TargetChoice c = rnd.Next(3) switch { 0 => new NewFolderTarget(), 1 => new SkipTarget(), _ => new AutoTarget() };
                    await s.ApplyAsync(new Retarget(g.Id.Anchor, c, false, g.Videos), Ct);
                    break;
                }
                case 6:
                    await s.ApplyAsync(new SetIncluded([b.Items[rnd.Next(b.Items.Length)].Raw.Unit.Id], rnd.Next(2) == 0), Ct);
                    break;
                case 7:
                    await s.PreviewAsync(new Tuning(5 + rnd.Next(96), rnd.Next(8)));
                    await s.CommitTuningAsync();
                    break;
                case 8:
                {
                    var fixes = p.Issues.SelectMany(x => x.QuickFixes).Where(q => !q.Edits.IsDefaultOrEmpty).ToList();
                    if (fixes.Count == 0) break;
                    var depth = s.UndoDepth;
                    if (await s.ApplyAllAsync(fixes[rnd.Next(fixes.Count)].Edits, Ct) is Applied)
                        Assert.Equal(depth + 1, s.UndoDepth);                     // a quick fix is one undo entry
                    break;
                }
                default:
                    if (rnd.Next(2) == 0) await s.UndoAsync();
                    else await s.RedoAsync();
                    break;
            }

            AssertPartition(s.Current, videos);
            if (s.CanUndo)
            {
                var before = PlanFingerprint.Of(s.Current);
                await s.UndoAsync();
                await s.RedoAsync();
                Assert.Equal(before, PlanFingerprint.Of(s.Current));           // undo then redo is the identity
            }
        }

        // replaying the draft on a copy of the scan gives the same plan
        var resumed = PlanSession.Resume(InvariantScenario.Build().Prepare(), s.ToDraft(), PlanScenario.CreatePlanner(), out var dropped);
        Assert.Equal(0, dropped);
        Assert.Equal(PlanFingerprint.Of(s.Current), PlanFingerprint.Of(resumed.Current));

        // edits survive every R from 5 to 100; local dates never change with R or G
        var planner = PlanScenario.CreatePlanner();
        var observed = 0;
        foreach (var g in new[] { 0, 1, 7 })
            for (var r = 5; r <= 100; r++)
            {
                var p = planner.Derive(b, new Tuning(r, g), s.Edits, new SessionFlags(false), 1, Ct);
                AssertPartition(p, videos);
                Assert.All(p.Groups, grp => Assert.Equal(grp.Videos.Min(v => dates[v]), grp.Start));
                Assert.All(p.Base.Items, i => Assert.Equal(dates[i.Raw.Unit.Id], i.Time.LocalDate));
                observed += AssertEditEffects(p, s.Edits);
                Assert.True(p.Included.SetEquals(s.Current.Included));           // inclusion never depends on R or G
            }
        Assert.True(observed > 0);                                            // the sweep checked at least one edit's effect
    }

    [Fact] // Ref §13: a fixed log of every item-anchored edit kind keeps each effect at every R from 5 to 100
    public async Task ItemAnchoredEdits_SurviveEveryRadius()
    {
        var b = InvariantScenario.Build().Prepare();
        ItemId N(int n) => b.Items.Single(i => i.Raw.Name.Contains($"_{n:0000}", StringComparison.Ordinal)).Raw.Unit.Id;
        var s = NewSession(b);
        PlanEdit[] log =
        [
            new SplitBefore(N(1)), new SplitBefore(N(2)), new SplitBefore(N(52)), new SplitBefore(N(124)),
            new Rename(N(2), "Anvil Mountain", [N(2), N(14)]), new Retarget(N(40), new SkipTarget(), false, [N(40)]),
            new Retarget(N(124), new NewFolderTarget(), false, [N(124)]),
            new SetIncluded([N(148)], false), new SetDayIncluded(new DateOnly(2026, 9, 27), false),
        ];
        foreach (var e in log) Assert.True(await s.ApplyAsync(e, Ct) is Applied, $"{e} was rejected");

        var planner = PlanScenario.CreatePlanner();
        foreach (var g in new[] { 0, 1, 7 })
            for (var r = 5; r <= 100; r++)
            {
                var p = planner.Derive(b, new Tuning(r, g), s.Edits, new SessionFlags(false), 1, Ct);
                Assert.Equal(log.Length, AssertEditEffects(p, s.Edits));        // every edit's effect was checked
            }
    }

    [Fact] // §8.9 worked example at session level: SplitBefore at R 50 → 25 → 50 keeps its UserSplit chip and the edit stays in the log
    public async Task SplitBefore_SurvivesTuningChanges()
    {
        var a1 = Clip.Vid("20260726235645", 1, Sites.Anvil);
        var b = new PlanScenario().Card(Clip.Vid("20260725232655", 117, Sites.Council), a1).Prepare();
        var s = NewSession(b);
        await s.ApplyAsync(new SplitBefore(a1.Id()), Ct);
        foreach (var r in new[] { 25, 50 })
        {
            await s.PreviewAsync(new Tuning(r, 1));
            await s.CommitTuningAsync();
            Assert.Equal(BoundaryCause.UserSplit, Assert.Single(s.Current.Boundaries).Cause);
            Assert.Single(s.Edits);
        }
    }

    /// <summary>Ref §13: item-anchored edits survive every R. Each edit still in force is observable in <paramref name="p"/>;
    /// returns how many edit effects were checked.</summary>
    private static int AssertEditEffects(Plan p, IReadOnlyList<PlanEdit> edits)
    {
        var byId = p.Base.Items.ToDictionary(i => i.Raw.Unit.Id);
        var videos = p.Base.Items.Where(i => i.Raw.Kind == ItemKind.Video).ToList();
        Clusterer.Cluster(videos, p.Tuning, [.. edits.Where(PlanEditRefs.IsStructural)], out var missing);
        Assert.Equal(0, missing);                                             // every edit's items are on the card
        var groupOf = p.Groups.SelectMany(g => g.Videos.Select(v => (v, g))).ToDictionary(x => x.v, x => x.g);
        bool TwoPins(VideoGroup g) =>
            p.Issues.Any(i => i is { Code: IssueCode.ConflictingPins, Severity: IssueSeverity.Blocking } && i.Anchor == g.Id.Anchor);
        var checkedEffects = 0;

        for (var k = 0; k < edits.Count; k++)
        {
            var later = edits.Skip(k + 1).ToList();
            switch (edits[k])
            {
                case SplitBefore sb when !SplitMayBeUndoneBy(later, sb.First):
                {
                    var g = groupOf[sb.First];
                    if (g.Id.Anchor == sb.First)                               // the split clip still starts its group, after a UserSplit
                    {
                        if (p.Boundaries.SingleOrDefault(x => x.Right == g.Id) is { } boundary)
                            Assert.Equal(BoundaryCause.UserSplit, boundary.Cause);
                    }
                    else                                                      // or only clips a later MoveToGroup brought in precede it
                    {
                        var movedIn = later.OfType<MoveToGroup>().SelectMany(m => m.Items).ToHashSet();
                        Assert.All(g.Videos.TakeWhile(v => v != sb.First), v => Assert.Contains(v, movedIn));
                    }
                    checkedEffects++;
                    break;
                }
                case Rename { Description: { } name } rn:
                {
                    var g = groupOf[rn.InGroup];
                    if (later.Exists(e => e is Rename o && g.Videos.Contains(o.InGroup)) || TwoPins(g)) break;   // a later pin wins
                    Assert.NotNull(g.NamePin);
                    if (g.Target is NewFolder)
                        Assert.Equal((FolderNamer.Clean(name), DescSource.User), (g.Description, g.DescSource));
                    checkedEffects++;
                    break;
                }
                case Retarget rt when rt.Choice is not AutoTarget:
                {
                    var g = groupOf[rt.InGroup];
                    if (later.Exists(e => e is Retarget o && g.Videos.Contains(o.InGroup)) || TwoPins(g)) break;
                    Assert.NotNull(g.TargetPin);
                    Assert.True(rt.Choice switch
                    {
                        NewFolderTarget => g.Target is NewFolder || g.Target is Append { Why: FolderNamer.FolderExistsWhy },
                        SkipTarget => g.Target is SkipGroup,
                        AppendTo to => g.Target is Append a && PathRules.Equal(a.Folder.FullPath, to.FolderFullPath),
                        _ => false,
                    }, $"{rt.Choice} pinned on {rt.InGroup} but the group's target is {g.Target}");
                    checkedEffects++;
                    break;
                }
            }
        }

        // inclusion: the last SetIncluded/SetDayIncluded touching an includable item decides it
        var expected = new Dictionary<ItemId, (bool Included, int Edit)>();
        static bool Includable(Item i) => i.Newness is IsNew or Conflict or ProbablyImported;
        for (var k = 0; k < edits.Count; k++)
        {
            if (edits[k] is SetIncluded si)
                foreach (var id in si.Items.Where(id => Includable(byId[id]))) expected[id] = (si.Included, k);
            else if (edits[k] is SetDayIncluded sd)
                foreach (var i in p.Base.Items.Where(i => i.Raw.Kind != ItemKind.Video && i.Time.LocalDate == sd.Day && Includable(i)))
                    expected[i.Raw.Unit.Id] = (sd.Included, k);
        }
        foreach (var (id, (included, _)) in expected)
            Assert.Equal((id, included), (id, p.Included.Contains(id)));
        return checkedEffects + expected.Values.Select(x => x.Edit).Distinct().Count();
    }

    /// <summary>A later merge (which may absorb the split, depending on R) or a later move of the split clip itself ends a split.</summary>
    private static bool SplitMayBeUndoneBy(List<PlanEdit> later, ItemId first) =>
        later.Exists(e => e switch
        {
            Merge => true,
            MoveToNewGroup mv => mv.Items.Contains(first),
            MoveToGroup mt => mt.Items.Contains(first),
            _ => false,
        });
}
