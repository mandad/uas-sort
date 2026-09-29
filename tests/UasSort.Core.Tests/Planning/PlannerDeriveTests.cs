// tests/UasSort.Core.Tests/Planning/PlannerDeriveTests.cs
using UasSort.Core.Naming;
using UasSort.Testing.Planning;

namespace UasSort.Core.Tests.Planning;

public sealed class PlannerDeriveTests
{
    private const string Zrel = @"2026\2026-09\2026-09-27 Zachar Bay";
    private static readonly RawItem[] Z =
    [
        Clip.Vid("20260927140127", 123, Sites.Zachar), Clip.Vid("20260927140144", 124, Sites.Zachar), Clip.Vid("20260927142416", 148, Sites.Zachar),
    ];
    private static readonly RawItem C117 = Clip.Vid("20260725232655", 117, Sites.Council);
    private static readonly RawItem C118 = Clip.Vid("20260726022937", 118, Sites.Council);
    private static readonly RawItem A1 = Clip.Vid("20260726235645", 1, Sites.Anvil);
    private static readonly RawItem A2 = Clip.Vid("20260727000012", 2, Sites.Anvil);
    private static readonly string[] KeepOrReset = ["Keep", "Reset to Auto"];
    private static readonly string[] UseFirstOrSecond = ["Use first", "Use second"];

    private static IEnumerable<Issue> Of(Plan p, IssueCode c) => p.Issues.Where(i => i.Code == c);

    [Fact]
    public void NewFolder_PrefilledFromSuggestion_ElseBlankAndBlocking()
    {
        var b = new PlanScenario().Card(Z).Prepare();
        var blank = PlanScenario.Derive(b);
        var g = Assert.Single(blank.Groups);
        Assert.Equal(new NewFolder(@"2026\2026-09\2026-09-27"), g.Target);
        Assert.True(g.DescriptionEditable);
        var e = Assert.Single(Of(blank, IssueCode.EmptyFolderName));
        Assert.Equal((IssueSeverity.Blocking, g.Id.Anchor), (e.Severity, e.Anchor));

        var places = new FakePlaceIndex(new PlaceHit("Zachar Bay", new GeoPoint(57.5400, -153.7500), PlaceClass.Feature, "BAY", 0, "America/Anchorage", default));
        var named = PlanScenario.Derive(b, places: places);
        Assert.Equal(new NewFolder(@"2026\2026-09\2026-09-27 Zachar Bay"), named.Groups[0].Target);
        Assert.Equal(DescSource.Feature, named.Groups[0].DescSource);
        Assert.Empty(Of(named, IssueCode.EmptyFolderName));
    }

    [Fact]
    public void Rename_NamesTheFolder_UnicodeKept()
    {
        var b = new PlanScenario().Card(Z).Prepare();
        var ids = Z.Select(z => z.Id()).ToArray();
        var p = PlanScenario.Derive(b, edits: [new Rename(ids[0], "  Sunset 🌅 Kodiak. ", [.. ids])]);
        var g = p.Groups[0];
        Assert.Equal(new NewFolder(@"2026\2026-09\2026-09-27 Sunset 🌅 Kodiak"), g.Target);
        Assert.Equal((DescSource.User, false), (g.DescSource, g.NamePin!.MembershipChanged));
    }

    [Theory] // [Review Focus] #2: a case-only (or trailing-dot) clash with an existing same-date folder is an explicit Append
    [InlineData("zachar bay")]
    [InlineData("ZACHAR BAY. ")]
    public void CaseOnlyClash_BecomesAppend_NeverASecondFolder(string typed)
    {
        var kodiak = Clip.Vid("20260927190000", 150, Sites.KodiakTown);        // same day, 53 mi away: auto = NewFolder
        var b = Z.Aggregate(new PlanScenario().Library(Zrel, Z), (s, z) => s.LedgerFile(z, Zrel, Sites.Zachar, "America/Anchorage"))
                 .Card(kodiak).Prepare();
        Assert.IsType<NewFolder>(PlanScenario.Derive(b).Groups[0].Target);
        var p = PlanScenario.Derive(b, edits: [new Rename(kodiak.Id(), typed, [kodiak.Id()])]);
        var a = Assert.IsType<Append>(p.Groups[0].Target);
        Assert.Equal($@"{PlanScenario.VideoRoot}\{Zrel}", a.Folder.FullPath);
        Assert.Equal(FolderNamer.FolderExistsWhy, a.Why);
        Assert.Equal(IssueSeverity.Info, Assert.Single(Of(p, IssueCode.FolderExistsAppending)).Severity);
        Assert.True(p.Groups[0].DescriptionEditable);
    }

    [Theory] // the 400-character limit is on the TEMP path
    [InlineData(331, true)]
    [InlineData(330, false)]
    public void TempPathLimit_Is400(int pad, bool blocking)
    {
        var s = new PlanScenario { Root = @"C:\" + new string('r', pad) }.Card(Z[0]);
        var p = PlanScenario.Derive(s.Prepare());
        Assert.Equal(blocking, Of(p, IssueCode.TempPathTooLong).Any());
    }

    [Fact] // pins: membership change → Warning with [Keep]/[Reset to Auto]
    public void RetargetPin_MembershipChanged_WarnsWithQuickFixes()
    {
        var b = new PlanScenario().Card(C117, C118, A1, A2).Prepare();
        var members = new[] { C117, C118, A1, A2 }.Select(x => x.Id()).ToArray();
        var pin = new Retarget(C117.Id(), new SkipTarget(), false, [.. members]);
        var at50 = PlanScenario.Derive(b, new Tuning(50, 1), [pin]);
        Assert.IsType<SkipGroup>(at50.Groups[0].Target);
        Assert.Empty(Of(at50, IssueCode.PinMembershipChanged));

        var at25 = PlanScenario.Derive(b, new Tuning(25, 1), [pin]);
        Assert.Equal(new PinState(true, 4, 2), at25.Groups[0].TargetPin);
        var w = Assert.Single(Of(at25, IssueCode.PinMembershipChanged));
        Assert.True(w.RequiresAckAtPreflight);
        Assert.Equal(KeepOrReset, w.QuickFixes.Select(q => q.Label));
        Assert.IsType<AutoTarget>(Assert.IsType<Retarget>(Assert.Single(w.QuickFixes[1].Edits)).Choice);
    }

    [Fact] // [Reset to Auto] clears a pin whose group gained an earlier clip (new anchor outside the old pin set)
    public void RetargetPin_ResetToAuto_AfterGroupGainsEarlierClips_ReturnsToAuto()
    {
        var b = new PlanScenario().Card(C117, C118, A1, A2).Prepare();
        var pin = new Retarget(A1.Id(), new SkipTarget(), false, [A1.Id(), A2.Id()]);
        var auto = PlanScenario.Derive(b, new Tuning(50, 1));
        var grown = PlanScenario.Derive(b, new Tuning(50, 1), [pin]);
        var g = Assert.Single(grown.Groups);
        Assert.Equal(C117.Id(), g.Id.Anchor);
        Assert.IsType<SkipGroup>(g.Target);
        var w = Assert.Single(Of(grown, IssueCode.PinMembershipChanged));

        var reset = PlanScenario.Derive(b, new Tuning(50, 1), [pin, .. w.QuickFixes[1].Edits]);
        Assert.Equal(auto.Groups[0].Target, reset.Groups[0].Target);
        Assert.Null(reset.Groups[0].TargetPin);
        Assert.Empty(Of(reset, IssueCode.PinMembershipChanged));
    }

    [Fact] // [Reset to Auto] clears a name pin whose group gained an earlier clip
    public void RenamePin_ResetToAuto_AfterGroupGainsEarlierClips_ReturnsToAuto()
    {
        var b = new PlanScenario().Card(C117, C118, A1, A2).Prepare();
        var pin = new Rename(A1.Id(), "Anvil Mountain", [A1.Id(), A2.Id()]);
        var auto = PlanScenario.Derive(b, new Tuning(50, 1));
        var grown = PlanScenario.Derive(b, new Tuning(50, 1), [pin]);
        var g = Assert.Single(grown.Groups);
        Assert.Equal(C117.Id(), g.Id.Anchor);
        Assert.Equal("Anvil Mountain", g.Description);
        var w = Assert.Single(Of(grown, IssueCode.PinMembershipChanged));

        var reset = PlanScenario.Derive(b, new Tuning(50, 1), [pin, .. w.QuickFixes[1].Edits]);
        Assert.Equal((auto.Groups[0].Target, auto.Groups[0].Description, auto.Groups[0].DescSource),
                     (reset.Groups[0].Target, reset.Groups[0].Description, reset.Groups[0].DescSource));
        Assert.Null(reset.Groups[0].NamePin);
        Assert.Empty(Of(reset, IssueCode.PinMembershipChanged));
    }

    [Fact] // two pins after a merge → Blocking [Use first]/[Use second]
    public void TwoRenamesAfterMerge_AreConflictingPins()
    {
        var b = new PlanScenario().Card(C117, C118, A1, A2).Prepare();
        var r1 = new Rename(C117.Id(), "Council Road", [C117.Id(), C118.Id()]);
        var r2 = new Rename(A1.Id(), "Anvil Mountain", [A1.Id(), A2.Id()]);
        var split = PlanScenario.Derive(b, new Tuning(25, 1), [r1, r2]);
        Assert.Empty(Of(split, IssueCode.ConflictingPins));
        var merged = PlanScenario.Derive(b, new Tuning(25, 1), [r1, r2, new Merge(C117.Id(), A1.Id())]);
        var c = Assert.Single(Of(merged, IssueCode.ConflictingPins));
        Assert.Equal(IssueSeverity.Blocking, c.Severity);
        Assert.Equal(UseFirstOrSecond, c.QuickFixes.Select(q => q.Label));
        var fixedPlan = PlanScenario.Derive(b, new Tuning(25, 1), [r1, r2, new Merge(C117.Id(), A1.Id()), .. c.QuickFixes[1].Edits]);
        Assert.Empty(Of(fixedPlan, IssueCode.ConflictingPins));
        Assert.Equal("Anvil Mountain", fixedPlan.Groups[0].Description);
    }

    [Fact]
    public void SharedTarget_InfoWithMerge()
    {
        var late = Clip.Vid("20261001120000", 300, Sites.Zachar);
        var b = new PlanScenario().Library(Zrel, Z).Card([.. Z, Clip.Vid("20260927160000", 160, Sites.Zachar), late]).Prepare();
        var p = PlanScenario.Derive(b, edits: [new Retarget(late.Id(), new AppendTo($@"{PlanScenario.VideoRoot}\{Zrel}"), false, [late.Id()])]);
        Assert.Equal(2, p.Groups.Count(g => g.Target is Append));
        var shared = Of(p, IssueCode.SharedTarget).ToList();
        Assert.Equal(2, shared.Count);
        Assert.All(shared, s => Assert.IsType<Merge>(Assert.Single(Assert.Single(s.QuickFixes).Edits)));
    }

    [Fact] // fold rule: Conflict, Truncated or ProbeFailed members never fold
    public void FoldRule()
    {
        var folded = PlanScenario.Derive(new PlanScenario().Library(Zrel, Z).Card(Z).Prepare());
        Assert.True(folded.Groups[0].Foldable);
        Assert.Equal(-1, folded.Groups[0].ColorIndex);
        var trunc = Clip.Vid("20260927143000", 149, Sites.Zachar, moov: false);
        var withTrunc = PlanScenario.Derive(new PlanScenario().Library(Zrel, [.. Z, trunc]).Card([.. Z, trunc]).Prepare());
        Assert.IsType<AlreadyImported>(withTrunc.Groups[0].Target);
        Assert.False(withTrunc.Groups[0].Foldable);
    }

    [Fact] // [Review Focus] #1: nothing new on the card → Blocking NothingNew; every AlreadyImported group folded; NothingToCopy groups never fold
    public void NothingNew_BlocksOffload_AlreadyImportedFold_NothingToCopyNever()
    {
        var dng = Clip.Dng("20260927141000", 125, Sites.Zachar);
        var council = new[] { C117, C118 };
        var culled = Clip.Vid("20260801200000", 7, Sites.Anvil);                 // in the ledger only (culled from the library)
        var b = new PlanScenario()
            .Library(Zrel, Z).Library(@"2026\2026-07\2026-07-25 Council Road", council)
            .LedgerFile(dng, null)
            .LedgerFile(culled, @"2026\2026-08\2026-08-01 Anvil")
            .Card([.. council, culled, .. Z, dng]).Prepare();
        var p = PlanScenario.Derive(b);
        var n = Assert.Single(Of(p, IssueCode.NothingNew));
        Assert.Equal(IssueSeverity.Blocking, n.Severity);
        Assert.Equal(3, p.Groups.Length);
        Assert.Equal(2, p.Groups.Count(g => g.Target is AlreadyImported));
        Assert.All(p.Groups.Where(g => g.Target is AlreadyImported), g => Assert.True(g.Foldable));
        var nothing = Assert.Single(p.Groups, g => g.Target is NothingToCopy);
        Assert.Equal(new NothingToCopy("nothing to copy: 1 already imported"), nothing.Target);
        Assert.False(nothing.Foldable);
        Assert.Empty(p.Included);

        var oneNew = PlanScenario.Derive(new PlanScenario().Library(Zrel, Z).Card([.. Z, Clip.Vid("20260927160000", 160, Sites.Zachar)]).Prepare());
        Assert.Empty(Of(oneNew, IssueCode.NothingNew));
    }

    [Fact]
    public void Inclusion_DefaultsThenEditsInLogOrder()
    {
        var trunc = Clip.Vid("20260927143000", 149, Sites.Zachar, moov: false);
        var dng = Clip.Dng("20260927141000", 125, Sites.Zachar);
        var b = new PlanScenario().Card([.. Z, trunc, dng]).Prepare();
        var p = PlanScenario.Derive(b);
        Assert.DoesNotContain(trunc.Id(), p.Included);                          // truncated: unticked
        Assert.Contains(dng.Id(), p.Included);
        var p2 = PlanScenario.Derive(b, edits: [new SetIncluded([trunc.Id()], true), new SetDayIncluded(new DateOnly(2026, 9, 27), false)]);
        Assert.Contains(trunc.Id(), p2.Included);
        Assert.DoesNotContain(dng.Id(), p2.Included);
    }

    [Fact]
    public void MediumAppend_AndEmphasisedSplit_RequireAck_WithQuickFixes()
    {
        var b = new PlanScenario().Library(@"2026\2026-07\2026-07-25 Council Road", C117, C118).Card(C117, C118, A1, A2).Prepare();
        var p = PlanScenario.Derive(b);
        var m = Assert.Single(Of(p, IssueCode.MediumAppend));
        Assert.True(m.RequiresAckAtPreflight);
        Assert.Equal(new SplitBefore(A1.Id()), Assert.Single(Assert.Single(m.QuickFixes).Edits));
        var d = Assert.Single(Of(p, IssueCode.EmphasisedDaySplit));
        Assert.Equal((IssueSeverity.Warning, true, (ItemId?)A1.Id()), (d.Severity, d.RequiresAckAtPreflight, d.Anchor));
        Assert.Contains(p.Groups[0].Hints, h => h.StartsWith("2 days", StringComparison.Ordinal));
    }

    [Fact] // F3 (Ref §8.7 rule 1): the wall's description is a suggestion only on Append, never a NewFolder prefill
    public void NewFolderRetarget_OnAWalledGroup_StaysANewFolder_NeverTheWall()
    {
        const string council = @"2026\2026-07\2026-07-25 Council Road";
        var b = new PlanScenario().Library(council, C117, C118).Card(C117, C118, A1, A2).Prepare();
        var auto = Assert.Single(PlanScenario.Derive(b).Groups);
        Assert.Equal(Confidence.Medium, Assert.IsType<Append>(auto.Target).Confidence);
        var members = auto.Videos;

        var p = PlanScenario.Derive(b, edits: [new Retarget(auto.Id.Anchor, new NewFolderTarget(), false, members)]);

        var g = Assert.Single(p.Groups);
        var nf = Assert.IsType<NewFolder>(g.Target);
        Assert.NotEqual(council, nf.RelPath, StringComparer.OrdinalIgnoreCase);
        Assert.NotEqual("Council Road", g.Description);
        Assert.Empty(Of(p, IssueCode.FolderExistsAppending));
        Assert.DoesNotContain(g.Suggestions, s => s.Source == DescSource.ExistingFolder);

        // NewBeforeWall (same fixture as NewBeforeWallFolder_InfoWithSplitHere): the later folder's name is never the prefill
        var f = new[] { Clip.Vid("20260726000000", 5, Sites.Anvil), Clip.Vid("20260726010000", 6, Sites.Anvil) };
        var early = Clip.Vid("20260725000000", 1, Sites.Anvil);
        var nbw = PlanScenario.Derive(new PlanScenario().Library(@"2026\2026-07\2026-07-25 Anvil", f).Card([early, .. f]).Prepare());
        Assert.Single(Of(nbw, IssueCode.NewBeforeWallFolder));
        var first = nbw.Groups.First(x => x.Videos.Contains(early.Id()));
        Assert.IsType<NewFolder>(first.Target);
        Assert.NotEqual("Anvil", first.Description);
    }

    [Fact]
    public void NewBeforeWallFolder_InfoWithSplitHere()
    {
        var f = new[] { Clip.Vid("20260726000000", 5, Sites.Anvil), Clip.Vid("20260726010000", 6, Sites.Anvil) };
        var early = Clip.Vid("20260725000000", 1, Sites.Anvil);
        var p = PlanScenario.Derive(new PlanScenario().Library(@"2026\2026-07\2026-07-25 Anvil", f).Card([early, .. f]).Prepare());
        var i = Assert.Single(Of(p, IssueCode.NewBeforeWallFolder));
        Assert.Equal(new SplitBefore(f[0].Id()), Assert.Single(Assert.Single(i.QuickFixes).Edits));
    }

    [Fact]
    public void LedgerAndRootIssues()
    {
        var bad = new PlanScenario { LedgerState = LedgerFolderState.Ok }.LedgerParseIssue("ledger-A.jsonl", 7, "bad json").Card(Z[0]).Prepare();
        var blocked = Assert.Single(Of(PlanScenario.Derive(bad), IssueCode.LedgerParseIssue));
        Assert.Equal((IssueSeverity.Blocking, false), (blocked.Severity, blocked.RequiresAckAtPreflight));
        var accepted = Assert.Single(Of(PlanScenario.Derive(bad, ledgerAccepted: true), IssueCode.LedgerParseIssue));
        Assert.Equal((IssueSeverity.Warning, true), (accepted.Severity, accepted.RequiresAckAtPreflight));

        Assert.Equal(IssueSeverity.Blocking, Assert.Single(Of(PlanScenario.Derive(
            new PlanScenario { LedgerState = LedgerFolderState.CloudOnly }.Card(Z[0]).Prepare()), IssueCode.LedgerCloudOnly)).Severity);
        Assert.Equal(IssueSeverity.Blocking, Assert.Single(Of(PlanScenario.Derive(
            new PlanScenario { LedgerState = LedgerFolderState.Unwritable }.Card(Z[0]).Prepare()), IssueCode.LedgerUnwritable)).Severity);
        Assert.Equal(IssueSeverity.Warning, Assert.Single(Of(PlanScenario.Derive(
            new PlanScenario { LedgerState = LedgerFolderState.NotPinned }.Card(Z[0]).Prepare()), IssueCode.LedgerNotPinned)).Severity);
        Assert.Equal(IssueSeverity.Info, Assert.Single(Of(PlanScenario.Derive(
            new PlanScenario { LedgerState = LedgerFolderState.Missing }.Card(Z[0]).Prepare()), IssueCode.LedgerNoHistory)).Severity);
        var unlistable = PlanScenario.Derive(new PlanScenario { LedgerState = LedgerFolderState.Unlistable }.Card(Z[0]).Prepare());
        var cantList = Assert.Single(Of(unlistable, IssueCode.LedgerUnlistable));
        Assert.Equal((IssueSeverity.Blocking, $@"Can't list {PlanScenario.VideoRoot}\.uas-sort"), (cantList.Severity, cantList.Message));
        Assert.Empty(Of(unlistable, IssueCode.LedgerNoHistory));
        Assert.Equal(IssueSeverity.Blocking, Assert.Single(Of(PlanScenario.Derive(
            new PlanScenario { RootsConfirmed = false }.Card(Z[0]).Prepare()), IssueCode.RootsUnconfirmed)).Severity);

        var dng = Clip.Dng("20260927141000", 125, Sites.Zachar);
        var noPhotoRoot = new PlanScenario { PhotoRootAvailable = false }.Card(Z[0], dng).Prepare();
        var p = PlanScenario.Derive(noPhotoRoot);
        Assert.DoesNotContain(dng.Id(), p.Included);                            // its items are unticked
        Assert.Equal(IssueSeverity.Warning, Assert.Single(Of(p, IssueCode.RootMissing)).Severity);
        var ticked = PlanScenario.Derive(noPhotoRoot, edits: [new SetIncluded([dng.Id()], true)]);
        Assert.Equal(IssueSeverity.Blocking, Assert.Single(Of(ticked, IssueCode.RootMissing)).Severity);
    }

    [Fact] // F4 (Ref §12 "A library root is missing"): an unavailable previous photo root leaves photos New and unticked
    public void UnavailablePreviousPhotoRoot_UnticksPhotos_AsItsWarningSays()
    {
        const string old = @"D:\Old Photos";
        var dng = Clip.Dng("20260927141000", 125, Sites.Zachar);
        var b = new PlanScenario().UnavailablePreviousPhotoRoot(old).Card(Z[0], dng).Prepare();
        Assert.IsType<IsNew>(b.Items.Single(i => i.Raw.Unit.Id == dng.Id()).Newness);

        var p = PlanScenario.Derive(b);

        Assert.DoesNotContain(dng.Id(), p.Included);
        Assert.Contains(Z[0].Id(), p.Included);                                 // videos don't depend on a photo root
        var w = Assert.Single(Of(p, IssueCode.RootMissing));
        Assert.Equal((IssueSeverity.Warning, $"{old} is not available; its items are unticked"), (w.Severity, w.Message));
        Assert.Contains(dng.Id(), PlanScenario.Derive(b, edits: [new SetIncluded([dng.Id()], true)]).Included);   // the user can still tick it
    }

    [Fact]
    public void ClockAndDateIssues()
    {
        var b = new PlanScenario().Card([.. Z, Clip.Vid("20260726035000", 1, Sites.Anvil)]).Prepare();
        var p = PlanScenario.Derive(b);
        var cm = Assert.Single(Of(p, IssueCode.ClockMismatch));
        Assert.Equal((IssueSeverity.Info, (ItemId?)null), (cm.Severity, cm.Anchor));
        Assert.StartsWith("Drone clock is set to UTC\u22124 (America/New_York), but footage on this card was shot in Alaska (UTC\u22128).", cm.Message);
        Assert.Equal(b.Items.Count(i => i.Flags.HasFlag(ItemFlags.CheckDate)), Of(p, IssueCode.CheckDate).Count());
        Assert.All(Of(p, IssueCode.CheckDate), i => Assert.Equal(IssueSeverity.Warning, i.Severity));
    }
}
