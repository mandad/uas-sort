// src/UasSort.Review/Review/VideosTabVm.cs
namespace UasSort.Review;

/// <summary>The Videos tab: timeline of group cards and folded runs, and the clip list of the selection (Ref §9.3, §9.5, §9.6 Sync).</summary>
public sealed partial class VideosTabVm : ObservableObject
{
    private readonly IReviewActions _actions;
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
    private PlanIndex? _index;
    private bool _updating;

    public VideosTabVm(IReviewActions actions) => _actions = actions;

    public ObservableCollection<TimelineEntryVm> Timeline { get; } = [];
    public ObservableCollection<ClipRowVm> Clips { get; } = [];
    public IReadOnlyList<ItemId> SelectedClipIds { get; private set; } = [];

    [ObservableProperty] public partial TimelineEntryVm? SelectedEntry { get; set; }
    [ObservableProperty] public partial string Header { get; private set; } = "Videos";

    public GroupCardVm? SelectedCard => SelectedEntry as GroupCardVm;

    public event Action<GroupId, IReadOnlyList<ItemId>, bool>? MapSelectRequested;
    public event Action<IReadOnlyList<ItemId>>? ClipSelectionChanged;

    private abstract record Entry(string Key);
    private sealed record CardEntry(string Key, VideoGroup Group) : Entry(Key);
    private sealed record FoldEntry(string Key, IReadOnlyList<VideoGroup> Run) : Entry(Key);

    internal void Update(PlanIndex ix)
    {
        _index = ix;
        var plan = ix.Plan;
        var previous = SelectedEntry;
        var previousAnchors = previous switch
        {
            GroupCardVm c => c.Group?.Videos.ToHashSet() ?? [],
            FoldedRunVm f => f.Groups.SelectMany(g => g.Videos).ToHashSet(),
            _ => [],
        };

        var entries = new List<Entry>();
        var run = new List<VideoGroup>();
        void FlushRun()
        {
            if (run.Count == 0) return;
            var key = "f:" + run[0].Id.Anchor.CardRelPath;
            if (_expanded.Contains(key)) entries.AddRange(run.Select(g => new CardEntry("g:" + g.Id.Anchor.CardRelPath, g)));
            else entries.Add(new FoldEntry(key, [.. run]));
            run.Clear();
        }
        foreach (var g in plan.Groups)
        {
            if (g.Foldable) { run.Add(g); continue; }
            FlushRun();
            entries.Add(new CardEntry("g:" + g.Id.Anchor.CardRelPath, g));
        }
        FlushRun();

        _updating = true;
        try
        {
            CollectionSync.Sync<TimelineEntryVm, Entry>(Timeline, entries, e => e.Key,
                e => e switch
                {
                    CardEntry c => new GroupCardVm(c.Group.Id, _actions),
                    FoldEntry f => new FoldedRunVm(f.Key),
                    _ => throw new InvalidOperationException("unknown timeline entry"),
                },
                (vm, e) =>
                {
                    if (vm is GroupCardVm card && e is CardEntry ce) card.Update(ce.Group, ix);
                    else if (vm is FoldedRunVm fold && e is FoldEntry fe) fold.Update(fe.Run);
                });

            if (previous is not null && !Timeline.Contains(previous))
                SelectedEntry = Timeline.FirstOrDefault(e => Contains(e, previousAnchors));
        }
        finally
        {
            _updating = false;
        }

        Header = "Videos · " + string.Create(CultureInfo.InvariantCulture,
            $"{plan.Groups.Count(g => g.Videos.Any(plan.Included.Contains))} to offload");
        RebuildClips();
    }

    public void ToggleFold(FoldedRunVm run)
    {
        if (!_expanded.Add(run.Key)) _expanded.Remove(run.Key);
        run.IsExpanded = _expanded.Contains(run.Key);
        if (_index is { } ix) Update(ix);
    }

    public GroupCardVm? NextCard(GroupCardVm card)
    {
        var i = Timeline.IndexOf(card);
        return i < 0 ? null : Timeline.Skip(i + 1).OfType<GroupCardVm>().FirstOrDefault();
    }

    public GroupCardVm? CardFor(ItemId anchor) => Timeline.OfType<GroupCardVm>().FirstOrDefault(c => c.Anchor == anchor);

    /// <summary>ReviewVm.GoTo: selects the card (or folded run) that holds this clip and selects the clip in the list and on the map.</summary>
    internal void Reveal(ItemId clip)
    {
        var entry = Timeline.FirstOrDefault(e => Contains(e, [clip]));
        if (entry is null) return;
        if (!ReferenceEquals(entry, SelectedEntry)) SelectedEntry = entry;
        SelectedClipIds = [clip];
        ClipSelectionChanged?.Invoke(SelectedClipIds);
        if (SelectedGroupId() is { } g) MapSelectRequested?.Invoke(g, SelectedClipIds, true);
    }

    public void SetSelectedClips(IReadOnlyList<ItemId> ids)
    {
        SelectedClipIds = ids;
        if (SelectedGroupId() is { } g) MapSelectRequested?.Invoke(g, ids, false);
    }

    public void OnMapClick(MapClick c)
    {
        var entry = Timeline.FirstOrDefault(e => e switch
        {
            GroupCardVm card => string.Equals(card.Anchor.CardRelPath, c.GroupId, StringComparison.Ordinal),
            FoldedRunVm fold => fold.Groups.Any(g => string.Equals(g.Id.Anchor.CardRelPath, c.GroupId, StringComparison.Ordinal)),
            _ => false,
        });
        if (entry is not null && !ReferenceEquals(entry, SelectedEntry)) SelectedEntry = entry;
        var clicked = c.ItemIds.Select(s => new ItemId(s)).ToList();
        SelectedClipIds = c.Ctrl ? [.. SelectedClipIds.Concat(clicked).Distinct()] : clicked;
        ClipSelectionChanged?.Invoke(SelectedClipIds);
        if (SelectedGroupId() is { } g) MapSelectRequested?.Invoke(g, SelectedClipIds, false);
    }

    /// <summary>The map's contextMenu (Ref §9.6): selects the card that holds the right-clicked dots (a dot of any group can be
    /// clicked) and makes the clicked clips the selection, so the menu acts on them. Returns that selection; [] when no card
    /// holds the dots (the selection is left alone and no menu opens).</summary>
    public IReadOnlyList<ItemId> OnMapContextMenu(MapContextMenu m)
    {
        ArgumentNullException.ThrowIfNull(m);
        var clicked = m.ItemIds.Select(s => new ItemId(s)).ToList();
        var entry = clicked.Count > 0 ? Timeline.FirstOrDefault(e => Contains(e, [clicked[0]])) : null;
        if (entry is null) return [];
        if (!ReferenceEquals(entry, SelectedEntry)) SelectedEntry = entry;
        var present = Clips.Select(c => c.Id).ToHashSet();
        List<ItemId> hit = [.. clicked.Where(present.Contains)];
        if (hit.Count == 0) return [];
        SelectedClipIds = hit;
        ClipSelectionChanged?.Invoke(SelectedClipIds);
        if (SelectedGroupId() is { } g) MapSelectRequested?.Invoke(g, SelectedClipIds, false);
        return SelectedClipIds;
    }

    public void OnMapClickEmpty()
    {
        SelectedClipIds = [];
        ClipSelectionChanged?.Invoke(SelectedClipIds);
    }

    partial void OnSelectedEntryChanged(TimelineEntryVm? value)
    {
        RebuildClips();
        if (_updating) return;
        SelectedClipIds = [];
        if (SelectedGroupId() is { } g) MapSelectRequested?.Invoke(g, [], true);
    }

    private GroupId? SelectedGroupId() => SelectedEntry switch
    {
        GroupCardVm c => c.Id,
        FoldedRunVm f when f.Groups.Count > 0 => f.Groups[0].Id,
        _ => null,
    };

    private void RebuildClips()
    {
        if (_index is not { } ix)
        {
            Clips.Clear();
            return;
        }
        IReadOnlyList<VideoGroup> groups = SelectedEntry switch
        {
            GroupCardVm { Group: { } g } => [g],
            FoldedRunVm f => f.Groups,
            _ => [],
        };
        var rows = groups.SelectMany(g => g.Videos.Select(v => (Group: g, Item: ix.Items[v])))
                         .OrderBy(x => x.Item.Time.CaptureUtc).ToList();
        CollectionSync.Sync(Clips, rows, r => r.Item.Raw.Unit.Id.CardRelPath,
            r => new ClipRowVm(r.Item.Raw.Unit.Id, _actions),
            (vm, r) => vm.Update(r.Item, ix.Plan.Included.Contains(r.Item.Raw.Unit.Id), r.Group,
                                 readOnly: SelectedEntry is FoldedRunVm || r.Group.Target is AlreadyImported));
        var present = Clips.Select(c => c.Id).ToHashSet();
        SelectedClipIds = [.. SelectedClipIds.Where(present.Contains)];
    }

    private static bool Contains(TimelineEntryVm e, HashSet<ItemId> anchors) => e switch
    {
        GroupCardVm c => c.Group is { } g && g.Videos.Any(anchors.Contains),
        FoldedRunVm f => f.Groups.Any(g => g.Videos.Any(anchors.Contains)),
        _ => false,
    };
}
