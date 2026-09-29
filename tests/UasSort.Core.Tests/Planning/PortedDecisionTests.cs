// tests/UasSort.Core.Tests/Planning/PortedDecisionTests.cs
using UasSort.Core.Naming;
using UasSort.Core.Planning;
using UasSort.Testing.Planning;
using static UasSort.Testing.Planning.DecisionScenarios;
using static UasSort.Testing.Planning.PlanAsserts;

namespace UasSort.Core.Tests.Planning;

/// <summary>Ref §13 table rows #22–#35 (spike: test_grouping.py Decisions) + new planning cases.</summary>
public sealed class PortedDecisionTests
{
    private static Plan P(PlanScenario s, params PlanEdit[] edits) => PlanScenario.Derive(s.Prepare(), edits: edits);
    private static string Full(string rel) => $@"{PlanScenario.VideoRoot}\{rel}";

    [Fact] // #22
    public void EmptyLibrary_NewFolder()
    {
        var p = P(new PlanScenario().Card(Z));
        Assert.Equal(new NewFolder(@"2026\2026-09\2026-09-27"), Assert.Single(p.Groups).Target);
        Assert.Contains(p.Issues, i => i.Code == IssueCode.EmptyFolderName && i.Severity == IssueSeverity.Blocking);
        Assert.Empty(p.Base.PhotoDays);
    }

    [Fact] // #23
    public void AppendToday_ViaCardLeftovers()
    {
        var p = P(ZLibrary().Card([.. Z, Clip.Vid("20260927160000", 160, Sites.Zachar), Clip.Vid("20260927161000", 161, Sites.Zachar)]));
        var g = Assert.Single(p.Groups);
        Assert.Equal(Full(Zrel), g.Wall!.FullPath);
        var a = Assert.IsType<Append>(g.Target);
        Assert.Equal((Full(Zrel), Confidence.High, "same day as clips already in this folder"), (a.Folder.FullPath, a.Confidence, a.Why));
    }

    [Fact] // #24
    public void AppendToday_ViaLedgerCentroid()
    {
        var a = Assert.IsType<Append>(Assert.Single(P(ZWithLedger().Card(Clip.Vid("20260927160000", 160, Sites.Zachar))).Groups).Target);
        Assert.Equal((Full(Zrel), Confidence.High, "same dates, <0.1 mi"), (a.Folder.FullPath, a.Confidence, a.Why));
    }

    [Fact] // #25
    public void AppendToday_LocationUnknown_Medium()
    {
        var a = Assert.IsType<Append>(Assert.Single(P(ZLibrary().Card(Clip.Vid("20260927160000", 160, Sites.Zachar))).Groups).Target);
        Assert.Equal((Confidence.Medium, "same dates, location unknown"), (a.Confidence, a.Why));
    }

    [Fact] // #26
    public void NextDay_Far_NewFolder()
    {
        var p = P(ZWithLedger().Card(Clip.Vid("20260928180000", 170, Sites.KodiakTown)));
        Assert.Equal(new NewFolder(@"2026\2026-09\2026-09-28"), Assert.Single(p.Groups).Target);
        Assert.Contains(p.Issues, i => i.Code == IssueCode.EmptyFolderName);
    }

    [Fact] // #27
    public void NextDay_SamePlace_AppendHigh()
    {
        var a = Assert.IsType<Append>(Assert.Single(P(ZWithLedger().Card(Clip.Vid("20260928180000", 170, Sites.Zachar))).Groups).Target);
        Assert.Equal((Full(Zrel), Confidence.High, "next day, <0.1 mi"), (a.Folder.FullPath, a.Confidence, a.Why));
    }

    [Fact] // #28
    public void NextDay_LocationUnknown_NewFolder()
    {
        var p = P(ZLibrary().Card(Clip.Vid("20260928180000", 170, Sites.Zachar)));
        var g = Assert.Single(p.Groups);
        Assert.Equal(new NewFolder(@"2026\2026-09\2026-09-28"), g.Target);
        Assert.Contains(Planner.AppendCandidates(p, g.Id), f => f.Ref.FullPath == Full(Zrel));
    }

    [Fact] // #29
    public void NeverAppendBeforeFolderNameDate()
        => Assert.Equal(new NewFolder(@"2026\2026-09\2026-09-26"),
                        Assert.Single(P(ZLibrary().Card(Clip.Vid("20260926180000", 170, Sites.Zachar))).Groups).Target);

    [Fact] // #30 (Changed: rewritten for the approved model)
    public void LegacyDepth_DjiAppendsBesideAutelMembers()
    {
        const string makaha = @"2022\2022-03-27 Makaha Valley";
        var mtimes = new[] { new DateTime(2022, 3, 27, 15, 1, 0, DateTimeKind.Utc), new DateTime(2022, 3, 27, 15, 2, 0, DateTimeKind.Utc),
                             new DateTime(2022, 3, 27, 15, 4, 0, DateTimeKind.Utc) };
        var sizes = new long[] { 61_000_000, 62_000_000, 64_000_000 };
        var names = new[] { "MAX_0061.MP4", "MAX_0062.MP4", "MAX_0064.MP4" };
        var s = new PlanScenario();
        var autel = new List<RawItem>();
        for (var i = 0; i < 3; i++)
        {
            s.LibraryFile($@"{makaha}\{names[i]}", sizes[i], mtimes[i]);
            autel.Add(Clip.Autel(names[i], sizes[i], mtimes[i]));
        }
        var b = s.Card([.. autel, Clip.Vid("20220327110500", 1, Sites.Makaha)]).Prepare();
        Assert.All(autel, a => Assert.Equal(TimeSource.Mtime, ItemOf(b, a).Time.Source));
        var g = Assert.Single(PlanScenario.Derive(b).Groups);
        Assert.Equal("Makaha Valley", g.Wall!.Description);
        var app = Assert.IsType<Append>(g.Target);
        Assert.Equal((Full(makaha), Confidence.High, "same day as clips already in this folder"), (app.Folder.FullPath, app.Confidence, app.Why));
    }

    [Fact] // #31 (Changed: AppendSplit → two groups split by a LibraryFolder wall)
    public void TwoWalls_TwoAppends()
    {
        var d1 = new[] { Clip.Vid("20260801200000", 1, Sites.Anvil), Clip.Vid("20260801201000", 2, Sites.Anvil) };
        var d2 = new[] { Clip.Vid("20260802200000", 3, Sites.Anvil), Clip.Vid("20260802203000", 5, Sites.Anvil) };
        var n9 = Clip.Vid("20260801202000", 9, Sites.Anvil);
        var n4 = Clip.Vid("20260802202000", 4, Sites.Anvil);
        var p = P(new PlanScenario().Library(@"2026\2026-08\2026-08-01 Anvil AM", d1).Library(@"2026\2026-08\2026-08-02 Anvil PM", d2)
                                    .Card([.. d1, .. d2, n9, n4]));
        Assert.Equal(2, p.Groups.Length);
        Assert.Equal(new[] { d1[0].Id(), d1[1].Id(), n9.Id() }, p.Groups[0].Videos);
        Assert.Equal(new[] { d2[0].Id(), n4.Id(), d2[1].Id() }, p.Groups[1].Videos);
        Assert.Equal(("Anvil AM", Confidence.High), (Assert.IsType<Append>(p.Groups[0].Target).Folder.Description, ((Append)p.Groups[0].Target).Confidence));
        Assert.Equal(("Anvil PM", Confidence.High), (Assert.IsType<Append>(p.Groups[1].Target).Folder.Description, ((Append)p.Groups[1].Target).Confidence));
        Assert.All(p.Groups, g => Assert.Equal("same day as clips already in this folder", ((Append)g.Target).Why));
        Assert.Equal(BoundaryCause.LibraryFolder, Assert.Single(p.Boundaries).Cause);
    }

    [Fact] // #32 (Changed)
    public void LeftoversCard_WallsAndPhotoDays()
    {
        var council = new[] { Clip.Vid("20260725232655", 117, Sites.Council), Clip.Vid("20260726022937", 118, Sites.Council) };
        var anvil = new[] { Clip.Vid("20260726235645", 1, Sites.Anvil), Clip.Vid("20260727000012", 2, Sites.Anvil) };
        var p116 = Clip.Dng("20260725233000", 116, Sites.Council);
        var p122 = Clip.Dng("20260927140000", 122, Sites.Zachar);
        var p119 = Clip.Dng("20260815200000", 119, Sites.Anvil);
        var b = new PlanScenario().Library(@"2026\2026-07\2026-07-25 Council Road", council).Library(@"2026\2026-07\2026-07-26 Anvil Mountain", anvil)
            .Card([.. council, .. anvil, .. Z, p116, p122, p119]).Prepare();
        var p = PlanScenario.Derive(b);
        Assert.Equal(3, p.Groups.Length);
        Assert.Equal("Council Road", Assert.IsType<AlreadyImported>(p.Groups[0].Target).Folder.Description);
        Assert.Equal("Anvil Mountain", Assert.IsType<AlreadyImported>(p.Groups[1].Target).Folder.Description);
        Assert.Equal(new NewFolder(@"2026\2026-09\2026-09-27"), p.Groups[2].Target);
        Assert.Equal(BoundaryCause.LibraryFolder, p.Boundaries[0].Cause);
        Assert.Contains(p.Issues, i => i.Code == IssueCode.EmptyFolderName && i.Anchor == p.Groups[2].Id.Anchor);
        Assert.Equal(new DateTime(2026, 7, 27, 4, 0, 12, DateTimeKind.Utc), b.WatermarkUtc);
        Assert.Equal("videos from this day are already in the library", Assert.IsType<ProbablyImported>(ItemOf(b, p116).Newness).Why);
        Assert.Equal(new IsNew(NewReason.AfterWatermark, null), ItemOf(b, p122).Newness);
        Assert.Equal(new IsNew(NewReason.AfterWatermark, null), ItemOf(b, p119).Newness);
        Assert.Equal(new[] { new DateOnly(2026, 7, 25), new DateOnly(2026, 8, 15), new DateOnly(2026, 9, 27) }, b.PhotoDays.Select(d => d.Date));
    }

    [Fact] // #33
    public void PhotoOnlyDay_BeforeWatermark_ProbablyImported()
    {
        var d = Clip.Dng("20260815200000", 119, Sites.Anvil);
        var b = ZLibrary().Card(d).Prepare();
        Assert.Empty(PlanScenario.Derive(b).Groups);
        Assert.Equal("photo-only day before the last imported video (Sep 27)", Assert.IsType<ProbablyImported>(ItemOf(b, d).Newness).Why);
        Assert.Equal(ClockMode.Setting, b.Scan.Clock.Mode);
    }

    [Fact] // #34 (Changed: PhotosOnly → PhotoDays)
    public void PhotoOnlyCard_AfterWatermark_PhotoDays()
    {
        var ph = new[] { Clip.Dng("20260930200000", 200, Sites.Zachar), Clip.Dng("20260930200500", 201, Sites.Zachar) };
        var b = ZLibrary().Card(ph).Prepare();
        Assert.All(ph, x => Assert.Equal(new IsNew(NewReason.AfterWatermark, null), ItemOf(b, x).Newness));
        var day = Assert.Single(b.PhotoDays);
        Assert.Equal((new DateOnly(2026, 9, 30), "America/Anchorage", 2), (day.Date, day.TzId, day.Items.Length));
        Assert.Empty(PlanScenario.Derive(b).Groups);
    }

    [Fact] // #35 (Changed: mtime ±2 s; no-watermark rule)
    public void PanoSets_ImportedByFolderAndDistinct()
    {
        var m1 = new DateTime(2026, 5, 25, 13, 30, 28, DateTimeKind.Utc);
        var m2 = new DateTime(2026, 5, 25, 13, 30, 31, DateTimeKind.Utc);
        var p87 = Clip.Set("001_0087", "2026-05-25 09:30:28", Sites.KodiakTown, ("PANO_0001.DNG", 13_751_808, m1), ("PANO_0002.DNG", 12_882_432, m2));
        var p112 = Clip.Set("001_0112", "2026-05-25 10:00:00", Sites.KodiakTown, ("PANO_0001.DNG", 13_751_808, m1.AddMinutes(30)), ("PANO_0002.DNG", 12_882_432, m2.AddMinutes(30)));
        var b = new PlanScenario()
            .LibraryFile(@"Picture Offload\001_0087\PANO_0001.DNG", 13_751_808, m1)
            .LibraryFile(@"Picture Offload\001_0087\PANO_0002.DNG", 12_882_432, m2)
            .Card(p87, p112).Prepare();
        Assert.IsType<Imported>(ItemOf(b, p87).Newness);
        Assert.Equal(SetResolution.Imported, b.Sets[p87.Id()].Resolution);
        Assert.Equal(new IsNew(NewReason.AfterWatermark, null), ItemOf(b, p112).Newness);
        Assert.Equal(("001_0112", SetResolution.Plain), (b.Sets[p112.Id()].FolderName, b.Sets[p112.Id()].Resolution));
    }

    [Fact] // Scenario D in miniature: after [New folder instead] the UserSplit rule makes the new day a NewFolder
    public void CrossDayFix_GivesAlreadyImportedPlusNewFolder()
    {
        var council = new[] { Clip.Vid("20260725232655", 117, Sites.Council), Clip.Vid("20260726022937", 118, Sites.Council) };
        var a1 = Clip.Vid("20260726235645", 1, Sites.Anvil);
        var b = new PlanScenario().Library(@"2026\2026-07\2026-07-25 Council Road", council)
            .Card([.. council, a1, Clip.Vid("20260727000012", 2, Sites.Anvil)]).Prepare();
        var before = PlanScenario.Derive(b);
        var fix = Assert.IsType<Append>(Assert.Single(before.Groups).Target).Hint!.Fix;
        var after = PlanScenario.Derive(b, edits: [.. fix]);
        Assert.IsType<AlreadyImported>(after.Groups[0].Target);
        Assert.Equal(new NewFolder(@"2026\2026-07\2026-07-26"), after.Groups[1].Target);
        Assert.Equal(BoundaryCause.UserSplit, after.Boundaries[0].Cause);
    }

    [Fact] // two-pass UserSplit: a Retarget pin on A never feeds B's rule
    public void PinOnEarlierGroup_DoesNotFeedUserSplitRule()
    {
        var a = Clip.Vid("20260927160000", 160, Sites.Zachar);
        var bClip = Clip.Vid("20260927161000", 161, Sites.Zachar);
        var b = ZWithLedger().Card(a, bClip).Prepare();
        var p = PlanScenario.Derive(b, edits: [new SplitBefore(bClip.Id()), new Retarget(a.Id(), new SkipTarget(), false, [a.Id()])]);
        Assert.IsType<SkipGroup>(p.Groups[0].Target);
        Assert.IsType<NewFolder>(p.Groups[1].Target);                 // still excluded: F was A's pass-1 target
    }

    [Fact] // controller ruling 2026-09-28 (Ref §8.7 Prefill): the auto prefill skips a folder the UserSplit excluded; a typed name still appends
    public void UserSplit_PrefillSkipsExcludedFolder_TypedNameStillAppends()
    {
        var a = Clip.Vid("20260927160000", 160, Sites.Zachar);
        var bClip = Clip.Vid("20260927161000", 161, Sites.Zachar);
        var b = ZWithLedger().Card(a, bClip).Prepare();
        var split = new SplitBefore(bClip.Id());

        var blank = PlanScenario.Derive(b, edits: [split]);
        Assert.Equal(Full(Zrel), Assert.IsType<Append>(blank.Groups[0].Target).Folder.FullPath);
        var later = blank.Groups[1];
        Assert.Equal(("Zachar Bay", DescSource.Ledger), (later.Suggestions[0].Text, later.Suggestions[0].Source));   // still offered
        Assert.Equal(new NewFolder(@"2026\2026-09\2026-09-27"), later.Target);
        Assert.Equal(("", DescSource.None), (later.Description, later.DescSource));
        Assert.Contains(blank.Issues, i => i.Code == IssueCode.EmptyFolderName && i.Anchor == later.Id.Anchor);
        Assert.DoesNotContain(blank.Issues, i => i.Code == IssueCode.FolderExistsAppending);

        var places = new FakePlaceIndex(new PlaceHit("Uyak Bay", new GeoPoint(57.5450, -153.7600), PlaceClass.Feature, "BAY", 0, "America/Anchorage", default));
        var next = PlanScenario.Derive(b, edits: [split], places: places).Groups[1];
        Assert.Equal(new NewFolder(@"2026\2026-09\2026-09-27 Uyak Bay"), next.Target);
        Assert.Equal(("Uyak Bay", DescSource.Feature), (next.Description, next.DescSource));

        var typed = PlanScenario.Derive(b, edits: [split, new Rename(bClip.Id(), "Zachar Bay", [bClip.Id()])]);
        var app = Assert.IsType<Append>(typed.Groups[1].Target);
        Assert.Equal((Full(Zrel), Confidence.High, FolderNamer.FolderExistsWhy), (app.Folder.FullPath, app.Confidence, app.Why));
        Assert.Contains(typed.Issues, i => i.Code == IssueCode.FolderExistsAppending && i.Anchor == typed.Groups[1].Id.Anchor);
    }
}
