// tests/UasSort.Core.Tests/Naming/SetFolderNamerTests.cs
using UasSort.Core.Naming;
using UasSort.Testing.Planning;

namespace UasSort.Core.Tests.Naming;

public sealed class SetFolderNamerTests
{
    private static readonly DateTime M1 = new(2026, 5, 25, 13, 30, 28, DateTimeKind.Utc);
    private static readonly DateTime M2 = new(2026, 5, 25, 13, 30, 31, DateTimeKind.Utc);
    private static readonly DateTime FirstFrameUtc = new(2026, 5, 25, 13, 30, 28, DateTimeKind.Utc);

    private static RawItem Pano(string name = "001_0087", int members = 2) => Clip.Set(name, "2026-05-25 09:30:28", Sites.KodiakTown,
        [.. new (string, long, DateTime)[] { ("PANO_0001.DNG", 13_751_808, M1), ("PANO_0002.DNG", 12_882_432, M2), ("PANO_0003.DNG", 12_000_000, M2) }.Take(members)]);

    private static Item ItemOf(RawItem r) => new(r,
        new ItemTime(FirstFrameUtc, TimeSource.DroneClockZone, "America/Anchorage", TzSource.Gps, new DateOnly(2026, 5, 25),
                     new DateTime(2026, 5, 25, 5, 30, 28)),
        null, null, ItemFlags.None, new IsNew(NewReason.NoMatch, null));

    private static SetPlacement Resolve(PlanScenario s, RawItem set, ISet<string>? taken = null)
    {
        var scan = s.Card(set).Build();
        return SetFolderNamer.Resolve((SetUnit)set.Unit, ItemOf(set), scan.Library, scan.Ledger,
                                      taken ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void NoFolder_IsPlain()
    {
        var p = Resolve(new PlanScenario(), Pano());
        Assert.Equal(("001_0087", SetResolution.Plain), (p.FolderName, p.Resolution));
        Assert.Equal(["PANO_0001.DNG", "PANO_0002.DNG"], p.MembersToCopy);
    }

    [Fact]
    public void EmptyExistingFolder_IsPlain()
        => Assert.Equal(SetResolution.Plain, Resolve(new PlanScenario().LibraryDir(@"Picture Offload\001_0087"), Pano()).Resolution);

    [Fact]
    public void SameMembersWithin2s_IsImported()
    {
        var s = new PlanScenario()
            .LibraryFile(@"Picture Offload\001_0087\PANO_0001.DNG", 13_751_808, M1.AddSeconds(2))
            .LibraryFile(@"Picture Offload\001_0087\PANO_0002.DNG", 12_882_432, M2.AddSeconds(-1));
        var p = Resolve(s, Pano());
        Assert.Equal(("001_0087", SetResolution.Imported), (p.FolderName, p.Resolution));
        Assert.Empty(p.MembersToCopy);
    }

    [Fact]
    public void MtimeOff3s_Clashes_ThenDateSuffixed()
    {
        var s = new PlanScenario()
            .LibraryFile(@"Picture Offload\001_0087\PANO_0001.DNG", 13_751_808, M1.AddSeconds(3))
            .LibraryFile(@"Picture Offload\001_0087\PANO_0002.DNG", 12_882_432, M2);
        var p = Resolve(s, Pano());
        Assert.Equal(("001_0087 2026-05-25", SetResolution.DateSuffixed), (p.FolderName, p.Resolution));
    }

    [Fact]
    public void ExistingSubsetOfCard_Resumes()
    {
        var s = new PlanScenario().LibraryFile(@"Picture Offload\001_0087\PANO_0001.DNG", 13_751_808, M1);
        var p = Resolve(s, Pano());
        Assert.Equal(("001_0087", SetResolution.Resume), (p.FolderName, p.Resolution));
        Assert.Equal(["PANO_0002.DNG"], p.MembersToCopy);
    }

    [Fact] // after a partial Card cleanup the card keeps only some members
    public void CardSubsetOfExisting_IsImported()
    {
        var s = new PlanScenario()
            .LibraryFile(@"Picture Offload\001_0087\PANO_0001.DNG", 13_751_808, M1)
            .LibraryFile(@"Picture Offload\001_0087\PANO_0002.DNG", 12_882_432, M2)
            .LibraryFile(@"Picture Offload\001_0087\PANO_0003.DNG", 12_000_000, M2);
        Assert.Equal(SetResolution.Imported, Resolve(s, Pano(members: 2)).Resolution);
    }

    [Fact]
    public void PlainAndDatedClash_TakesCounter2()
    {
        var s = new PlanScenario()
            .LibraryFile(@"Picture Offload\001_0087\OTHER.DNG", 1, M1)
            .LibraryFile(@"Picture Offload\001_0087 2026-05-25\OTHER.DNG", 1, M1);
        Assert.Equal("001_0087 2026-05-25 (2)", Resolve(s, Pano()).FolderName);
    }

    [Fact]
    public void BatchTaken_SkipsToNextCandidate()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "001_0087" };
        var p = Resolve(new PlanScenario(), Pano(), taken);
        Assert.Equal(("001_0087 2026-05-25", SetResolution.DateSuffixed), (p.FolderName, p.Resolution));
        Assert.Contains("001_0087 2026-05-25", taken);
    }

    [Fact]
    public void LedgerSetWithSameMembersAndFirstFrame_IsImported()
    {
        var s = new PlanScenario().LedgerSet("001_0087", FirstFrameUtc, ("PANO_0001.DNG", 13_751_808), ("PANO_0002.DNG", 12_882_432));
        Assert.Equal(SetResolution.Imported, Resolve(s, Pano()).Resolution);
        var other = new PlanScenario().LedgerSet("001_0087", FirstFrameUtc.AddHours(1), ("PANO_0001.DNG", 13_751_808), ("PANO_0002.DNG", 12_882_432));
        Assert.Equal(SetResolution.Plain, Resolve(other, Pano()).Resolution);
    }
}
