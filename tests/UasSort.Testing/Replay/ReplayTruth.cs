// tests/UasSort.Testing/Replay/ReplayTruth.cs
using UasSort.Core.Planning;

namespace UasSort.Testing.Planning;

/// <summary>The user's folder assignment of every library MP4, as partitions comparable with a plan.</summary>
public static class ReplayTruth
{
    public static string Leaf(string folderRelPath) => folderRelPath[(folderRelPath.LastIndexOf('/') + 1)..];

    public static IReadOnlyDictionary<string, string> FolderByClip(ReplayFixture f) =>
        f.Mp4s.ToDictionary(ReplayFixture.NameOf, e => Leaf(ReplayFixture.FolderOf(e)), StringComparer.Ordinal);

    public static IEnumerable<ReplayEntry> In(ReplayFixture f, string leaf) => f.Mp4s.Where(e => Leaf(ReplayFixture.FolderOf(e)) == leaf);

    public static List<string> Partition(Plan p) =>
        p.Groups.Select(g => string.Join("|", g.Videos.Select(v => PlanKeys.FileName(v.CardRelPath)).Order(StringComparer.Ordinal)))
                .Order(StringComparer.Ordinal).ToList();

    public static List<string> Expected(ReplayFixture f, Func<string, string> mergeLeaf) =>
        FolderByClip(f).GroupBy(kv => mergeLeaf(kv.Value))
                       .Select(g => string.Join("|", g.Select(kv => kv.Key).Order(StringComparer.Ordinal)))
                       .Order(StringComparer.Ordinal).ToList();

    public static string Leaves(ReplayFixture f, VideoGroup g)
    {
        var truth = FolderByClip(f);
        return string.Join("+", g.Videos.Select(v => truth[PlanKeys.FileName(v.CardRelPath)]).Distinct().Order(StringComparer.Ordinal));
    }
}
