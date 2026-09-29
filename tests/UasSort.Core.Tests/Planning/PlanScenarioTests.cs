// tests/UasSort.Core.Tests/Planning/PlanScenarioTests.cs
using UasSort.Core.Planning;
using UasSort.Testing.Planning;

namespace UasSort.Core.Tests.Planning;

public sealed class PlanScenarioTests
{
    [Fact]
    public void Vid_FollowsTheSpikeHelper()
    {
        var v = Clip.Vid("20260927140127", 123, Sites.Zachar);
        Assert.Equal("DJI_20260927140127_0123_D.MP4", v.Name);
        Assert.Equal(100_000_123, v.Bytes);
        Assert.Equal(new DateTime(2026, 9, 27, 18, 1, 27, DateTimeKind.Utc), v.Mp4!.MvhdUtc);
        Assert.Equal(new DateTime(2026, 9, 27, 18, 2, 57, DateTimeKind.Utc), v.CardMtimeUtc);
        Assert.Equal(new ItemId("DCIM/DJI_001/DJI_20260927140127_0123_D.MP4"), v.Id());
        Assert.True(v.Mp4!.First is GpsFix);
        Assert.Null(Clip.Vid("20260727002013", 14, Sites.Anvil, moov: false).Mp4!.MvhdUtc);
    }

    [Fact]
    public void Build_ProducesScanResultWithLibraryMatches()
    {
        var z = Clip.Vid("20260927140127", 123, Sites.Zachar);
        var scan = new PlanScenario()
            .Card(z)
            .Library(@"2026\2026-09\2026-09-27 Zachar Bay", z)
            .Build();
        Assert.Single(scan.Inventory.Units);
        Assert.Single(scan.Raw);
        Assert.Single(scan.Library.Match(PlanKeys.Key(z.Name, z.Bytes)));
        Assert.Contains(scan.Library.Folders, f => f.Ref.Description == "Zachar Bay");
        Assert.Equal(ClockMode.Zone, scan.Clock.Mode);
        Assert.Equal(@"C:\lib\UAS Videos", scan.Settings.VideoRoot);
    }

    [Fact] // Sites are Part 04's FixturePoints, not a second copy
    public void Sites_AreTheFixturePoints()
    {
        Assert.Equal(FixturePoints.Zachar, Sites.Zachar);
        Assert.Equal(FixturePoints.KodiakTown, Sites.KodiakTown);
        Assert.Equal(FixturePoints.Makaha, Sites.Makaha);
    }
}
