// src/UasSort.Review/Sync/CollectionSync.cs
namespace UasSort.Review;

/// <summary>A VM with a stable identity across re-derivations.</summary>
public interface IKeyed
{
    string Key { get; }
}

/// <summary>Keyed diff of an ObservableCollection against a new model list (Ref §4.2 CollectionSync).</summary>
public static class CollectionSync
{
    public static void Sync<TVm, TModel>(ObservableCollection<TVm> target, IReadOnlyList<TModel> source, Func<TModel, string> key,
                                         Func<TModel, TVm> create, Action<TVm, TModel> update)
        where TVm : class, IKeyed
    {
        var wanted = new HashSet<string>(source.Select(key), StringComparer.Ordinal);
        for (var i = target.Count - 1; i >= 0; i--)
            if (!wanted.Contains(target[i].Key)) target.RemoveAt(i);

        for (var i = 0; i < source.Count; i++)
        {
            var k = key(source[i]);
            if (i < target.Count && string.Equals(target[i].Key, k, StringComparison.Ordinal))
            {
                update(target[i], source[i]);
                continue;
            }
            var j = -1;
            for (var s = i + 1; s < target.Count; s++)
                if (string.Equals(target[s].Key, k, StringComparison.Ordinal)) { j = s; break; }
            if (j >= 0)
            {
                target.Move(j, i);
                update(target[i], source[i]);
            }
            else
            {
                var vm = create(source[i]);
                update(vm, source[i]);
                target.Insert(i, vm);
            }
        }
        while (target.Count > source.Count) target.RemoveAt(target.Count - 1);
    }
}
