namespace UasSort.Testing;

/// <summary>Canonical paths through subst drives / junctions (aliases) and cloud sync roots, for validator tests.</summary>
public sealed class FakePathFacts : IPathFacts
{
    private readonly List<(string From, string To)> _aliases = [];
    private readonly List<string> _syncRoots = [];

    public void AddAlias(string from, string to) => _aliases.Add((PathRules.Normalize(from), PathRules.Normalize(to)));

    public void AddSyncRoot(string root) => _syncRoots.Add(PathRules.Normalize(root));

    public string Canonical(string path)
    {
        var p = PathRules.Normalize(path);
        foreach (var (from, to) in _aliases.OrderByDescending(a => a.From.Length))
            if (PathRules.IsSameOrUnder(p, from)) return PathRules.Join(to, PathRules.RelativeCardPath(p, from));
        return p;
    }

    public bool InSyncRoot(string canonicalPath) => _syncRoots.Any(r => PathRules.IsSameOrUnder(canonicalPath, r));

    public IReadOnlyList<string> SyncRoots() => [.. _syncRoots];
}
