// tests/UasSort.Review.Tests/CollectionSyncTests.cs
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace UasSort.Review.Tests;

public class CollectionSyncTests
{
    private sealed class Row(string key) : IKeyed
    {
        public string Key { get; } = key;
        public int Value { get; set; }
    }

    private static void Sync(ObservableCollection<Row> target, params (string Key, int Value)[] source)
        => CollectionSync.Sync(target, source, s => s.Key, s => new Row(s.Key) { Value = s.Value }, (r, s) => r.Value = s.Value);

    [Fact]
    public void CollectionSync_KeepsInstancesAndSelection()
    {
        var target = new ObservableCollection<Row>();
        Sync(target, ("a", 1), ("b", 2), ("c", 3));
        var selected = target[1];
        var events = new List<NotifyCollectionChangedAction>();
        target.CollectionChanged += (_, e) => events.Add(e.Action);

        Sync(target, ("c", 30), ("b", 20), ("d", 4));

        Assert.Equal<string>(["c", "b", "d"], target.Select(r => r.Key));
        Assert.Same(selected, target[1]);
        Assert.Equal(20, selected.Value);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, events);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Replace, events);
    }

    [Fact]
    public void CollectionSync_EmptySourceClears()
    {
        var target = new ObservableCollection<Row>();
        Sync(target, ("a", 1));
        Sync(target);
        Assert.Empty(target);
    }
}
