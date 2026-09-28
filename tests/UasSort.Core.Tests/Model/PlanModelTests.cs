using System.Reflection;

namespace UasSort.Core.Tests.Model;

public class PlanModelTests
{
    [Fact]
    public void Tuning_DefaultsToFiftyMilesAndOneDay()
    {
        var t = new Tuning();
        Assert.Equal(50, t.RadiusMiles);
        Assert.Equal(1, t.GapDays);
    }

    [Fact]
    public void Settings_HasNoLedgerFolderProperty()
    {
        var names = typeof(Settings).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToList();
        Assert.DoesNotContain(names, n => n.Contains("Ledger", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("VideoRoot", names);
    }

    [Fact]
    public void GroupTarget_PlanEdit_TargetChoice_And_EditResult_AreExhaustive()
    {
        var a = new ItemId("DCIM/DJI_001/DJI_20260725232655_0117_D.MP4");
        var folder = new LibraryFolderRef(@"C:\V\2026\2026-07\2026-07-25 Council Road", new DateOnly(2026, 7, 25), "Council Road");
        GroupTarget[] targets =
        [
            new AlreadyImported(folder), new NothingToCopy("nothing to copy: 2 conflicts, 1 dismissed"),
            new NewFolder(@"2026\2026-07\2026-07-26"), new Append(folder, Confidence.Medium, "different day, 34 mi from Council Road", null),
            new SkipGroup(),
        ];
        Assert.Equal("AINAS", string.Concat(targets.Select(Letter)));

        PlanEdit[] edits =
        [
            new Merge(a, a), new SplitBefore(a), new MoveToNewGroup([a]), new MoveToGroup([a], a),
            new Rename(a, "Anvil", []), new Retarget(a, new AutoTarget(), false, []), new SetIncluded([a], true),
            new SetDayIncluded(new DateOnly(2026, 7, 26), true),
        ];
        Assert.Equal(8, edits.Select(EditName).Distinct().Count());

        TargetChoice[] choices = [new AutoTarget(), new NewFolderTarget(), new AppendTo(folder.FullPath), new SkipTarget()];
        Assert.Equal(4, choices.Select(ChoiceName).Distinct().Count());

        EditResult rejected = new Rejected(RejectReason.MergeAcrossLibraryFolders, "Can't merge across library folders");
        Assert.Equal("rejected", rejected switch { Applied => "applied", Rejected => "rejected" });

        static char Letter(GroupTarget t) => t switch
        {
            AlreadyImported => 'A', NothingToCopy => 'I', NewFolder => 'N', Append => 'A', SkipGroup => 'S',
        };
        static string EditName(PlanEdit e) => e switch
        {
            Merge => "m", SplitBefore => "s", MoveToNewGroup => "mn", MoveToGroup => "mg", Rename => "r",
            Retarget => "rt", SetIncluded => "si", SetDayIncluded => "sd",
        };
        static string ChoiceName(TargetChoice c) => c switch
        {
            AutoTarget => "auto", NewFolderTarget => "new", AppendTo => "append", SkipTarget => "skip",
        };
    }

    [Fact]
    public void LedgerRecord_IsAClosedHierarchyOfEightKinds()
    {
        var card = new RunCard("1A2B3C4D", null, "exFAT", "FC9113", "9f3c0a6d12e4b7a1");
        var at = new DateTime(2026, 10, 6, 18, 2, 11, DateTimeKind.Utc);
        LedgerRecord[] records =
        [
            new FileRecord(1, "f", "DESKTOP-A", "r", at, "video", "a.MP4", 1, "DCIM/DJI_001/a.MP4", "video", @"C:\V\a.MP4", null, "nameSize", at, null, null, null, null, null, null, null, null, null),
            new FolderRecord(1, "d", "DESKTOP-A", "r", @"C:\V\x", "x", "created", null, null, new DateOnly(2026, 9, 27), new DateOnly(2026, 9, 27), "America/Anchorage"),
            new SeenRecord(1, "s", "DESKTOP-A", "r", at, "b.DNG", 2, "DCIM/DJI_001/b.DNG", null, "New", "unticked", null),
            new DecisionRecord(1, "c", "DESKTOP-A", "r", at, "dismissed", "b.DNG", 2, "DCIM/DJI_001/b.DNG", null, "not needed", null),
            new RevokeRecord(1, "v", "LAPTOP-B", at, "c"),
            new RunRecord(1, "u", "DESKTOP-A", "r", at, at, "0.1.0", card, new RunRoots(@"C:\V", @"C:\V\P"), "Safe", ImmutableDictionary<string, int>.Empty),
            new TornRecord(1, "t", "DESKTOP-A", at, 412),
            new CardDeleteRecord(1, "x", "DESKTOP-A", "r", at, "a.MP4", 1, "DCIM/DJI_001/a.MP4", "DCIM/DJI_001/a.MP4", null, "InLedger", "in the history, verified", "beforeDate", card, null),
        ];
        Assert.Equal(8, records.Select(Kind).Distinct().Count());

        static string Kind(LedgerRecord r) => r switch
        {
            FileRecord => "file", FolderRecord => "folder", SeenRecord => "seen", DecisionRecord => "decision",
            RevokeRecord => "revoke", RunRecord => "run", TornRecord => "torn", CardDeleteRecord => "cardDelete",
        };
    }
}
