using Spike.Core;
public class CoreTests
{
    [Fact] public void ClosedHierarchySwitch() => Assert.Equal("skipped: dup", Describe.Outcome(new Skipped("dup")));
    [Fact] public void UnionSwitch() => Assert.Equal("5+10", Describe.Probe(new ByteRange(5, 10)));
    [Fact] public void CollectionArgs() => Assert.Equal(3, Describe.Exts().Count);
    [Fact] public void TimeZone() => Assert.Equal("America/Anchorage", Describe.TimeZoneAt(57.8, -152.4));
    [Fact] public void VmPartialProperty()
    {
        var vm = new MainVm(); var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        vm.RadiusMiles = 40; vm.RegroupCommand.Execute(null);
        Assert.Contains("RadiusMiles", raised); Assert.Equal("Regrouped at 40 mi, G=1", vm.Status);
    }
}
