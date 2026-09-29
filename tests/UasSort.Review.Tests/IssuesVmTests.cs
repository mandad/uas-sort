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
}
