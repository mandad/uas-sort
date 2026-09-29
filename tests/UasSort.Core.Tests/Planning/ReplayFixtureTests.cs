// tests/UasSort.Core.Tests/Planning/ReplayFixtureTests.cs
using UasSort.Testing.Planning;

namespace UasSort.Core.Tests.Planning;

public sealed class ReplayFixtureTests
{
    [Fact]
    public void Fixture_HasTheUsersEightFoldersAndClipGps()
    {
        var f = ReplayFixture.Load();
        Assert.Equal("America/Anchorage", f.PcZone);
        var folders = f.Mp4s.GroupBy(ReplayFixture.FolderOf).ToDictionary(g => g.Key[(g.Key.LastIndexOf('/') + 1)..], g => g.Count());
        Assert.Equal(8, folders.Count);
        Assert.Equal(4, folders["2026-07-25 Council Road"]);
        Assert.Equal(21, folders["2026-07-26 Anvil Mountain"]);
        Assert.Equal(13, folders["2026-09-27 Zachar Bay"]);
        Assert.All(f.Mp4s, e => Assert.True(f.Clips.ContainsKey(ReplayFixture.NameOf(e))));
        Assert.False(f.Clips["DJI_20260727002013_0014_D.MP4"].HasMoov);
        Assert.False(f.Clips["DJI_20260727005240_0024_D.MP4"].HasMoov);
        Assert.All(f.Clips.Where(c => c.Key.StartsWith("MAX_", StringComparison.Ordinal)), c => Assert.Null(c.Value.MvhdUtc));
        Assert.DoesNotContain(f.Entries, e => e.RelPath.StartsWith(".uas-sort/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CardItem_BuildsDjiAndAutelRawItems()
    {
        var f = ReplayFixture.Load();
        var zachar = f.Mp4s.First(e => ReplayFixture.NameOf(e) == "DJI_20260927142416_0148_D.MP4");
        var r = f.CardItem(zachar);
        Assert.Equal(new DateTime(2026, 9, 27, 14, 24, 16), r.DroneStamp);
        Assert.True(r.Mp4!.First is GpsFix);
        var trunc = f.CardItem(f.Mp4s.First(e => ReplayFixture.NameOf(e) == "DJI_20260727002013_0014_D.MP4"));
        Assert.False(trunc.Mp4!.HasMoov);
        var autel = f.CardItem(f.Mp4s.First(e => ReplayFixture.NameOf(e).StartsWith("MAX_", StringComparison.Ordinal)));
        Assert.Null(autel.Mp4);
    }
}
