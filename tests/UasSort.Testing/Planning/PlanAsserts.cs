// tests/UasSort.Testing/Planning/PlanAsserts.cs
using UasSort.Core.Planning;

namespace UasSort.Testing.Planning;

public static class PlanAsserts
{
    public static string[][] Ids(Plan p) =>
        p.Groups.Select(g => g.Videos.Select(id => PlanKeys.FileName(id.CardRelPath)[4..18]).ToArray()).ToArray();

    public static Item ItemOf(PlanBase b, RawItem r) => b.Items.Single(i => i.Raw.Unit.Id == r.Id());

    public static VideoGroup GroupOf(Plan p, RawItem r) => p.Groups.Single(g => g.Videos.Contains(r.Id()));

    public static IEnumerable<IssueCode> Codes(Plan p) => p.Issues.Select(i => i.Code);
}
