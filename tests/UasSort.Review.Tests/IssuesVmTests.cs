// tests/UasSort.Review.Tests/IssuesVmTests.cs
namespace UasSort.Review.Tests;

public class IssuesVmTests
{
    [Fact]
    public async Task IssuesVm_CountsOrdersAndRunsQuickFixes()
    {
        var ran = new List<string>();
        var vm = new IssuesVm((issue, fix) => { ran.Add($"{issue.Code}:{fix.Label}"); return Task.CompletedTask; });
        var anchor = new ItemId("DCIM/DJI_001/a.MP4");
        vm.Update(
        [
            new Issue(IssueSeverity.Info, IssueCode.SharedTarget, "Also targeted by Jul 25", anchor, [], false),
            new Issue(IssueSeverity.Blocking, IssueCode.EmptyFolderName, "Name this folder", anchor, [new QuickFix("Name it", [])], false),
            new Issue(IssueSeverity.Warning, IssueCode.MediumAppend, "Appending to 'Council Road': different day, 34 mi from Council Road", anchor,
                      [new QuickFix("New folder instead", [new SplitBefore(anchor)])], true),
        ]);

        Assert.Equal((1, 1, 1), (vm.BlockingCount, vm.WarningCount, vm.InfoCount));
        Assert.True(vm.HasBlocking);
        Assert.Equal<IssueCode>([IssueCode.EmptyFolderName, IssueCode.MediumAppend, IssueCode.SharedTarget], vm.Entries.Select(e => e.Code));
        Assert.Equal("⛔", vm.Entries[0].Glyph);
        Assert.Equal("Fix before offloading:\n• Name this folder", vm.BlockedTooltip);

        await vm.Entries[1].QuickFixes[0].Command.ExecuteAsync(null);
        Assert.Equal("MediumAppend:New folder instead", Assert.Single(ran));
    }

    [Fact]
    public void IssuesVm_NoBlocking_NoTooltip()
    {
        var vm = new IssuesVm((_, _) => Task.CompletedTask);
        vm.Update([]);
        Assert.False(vm.HasBlocking);
        Assert.Null(vm.BlockedTooltip);
    }

    [Fact]
    public void Footer_TotalsPerDriveWithFreeSpace()
    {
        var plan = new ScriptedDeriver().Derive(TestPlans.Base(TestPlans.Zachar()), new Tuning(), [], new SessionFlags(false), 1,
                                                TestContext.Current.CancellationToken);
        Assert.Equal("3 videos · 3.6 GB → C: (317 GB free)", Footer.Text(plan, new FakeFreeSpace()));
    }

    [Fact]
    public void Footer_SkippedGroupNotCounted()
    {
        var clips = TestPlans.Zachar();
        var ids = clips.Select(c => TestPlans.Id(c.Name)).ToImmutableArray();
        var plan = new ScriptedDeriver().Derive(TestPlans.Base(clips), new Tuning(), [new Retarget(ids[0], new SkipTarget(), false, ids)],
                                                new SessionFlags(false), 1, TestContext.Current.CancellationToken);

        Assert.IsType<SkipGroup>(Assert.Single(plan.Groups).Target);
        Assert.Equal(3, plan.Included.Count);                   // still ticked: inclusion doesn't depend on the target (Ref 8.9)
        Assert.Equal("Nothing to copy", Footer.Text(plan, new FakeFreeSpace()));
    }

    [Fact]
    public void Footer_CountsTheJpgTwinCommitCopies()
    {
        var utc = TestPlans.Utc(2026, 9, 27, 19, 0);
        var photo = PhotosOtherTabTests.Photo("DJI_20260927110000_0150_D.DNG", utc, new IsNew(NewReason.NoMatch, null));
        var unit = (PhotoUnit)photo.Raw.Unit;
        var twin = new CardEntry("DCIM/DJI_001/DJI_20260927110000_0150_D.JPG", 8_000_000, utc, utc, utc, 0x20, EntryClass.Photo, null);
        photo = photo with { Raw = photo.Raw with { Unit = unit with { JpgTwin = twin } } };
        var plan = new ScriptedDeriver().Derive(TestPlans.Base(TestPlans.Zachar(), extraItems: [photo]), new Tuning(), [],
                                                new SessionFlags(false), 1, TestContext.Current.CancellationToken);

        Assert.True(plan.Base.Scan.Settings.CopyJpgTwin);
        Assert.Equal("3 videos · 3.6 GB → C: (317 GB free) · 1 photo · 38 MB → C:", Footer.Text(plan, new FakeFreeSpace()));
    }
}
