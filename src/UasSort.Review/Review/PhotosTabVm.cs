// src/UasSort.Review/Review/PhotosTabVm.cs
namespace UasSort.Review;

/// <summary>One day row of the Photos tab with its tri-state include box (Ref §9.8).</summary>
public sealed partial class PhotoDayVm : ObservableObject, IKeyed
{
    private readonly Func<PlanEdit, Task> _apply;

    public PhotoDayVm(DateOnly date, Func<PlanEdit, Task> apply)
    {
        Date = date;
        _apply = apply;
        ToggleCommand = new AsyncRelayCommand(() => _apply(new SetDayIncluded(Date, IsIncluded != true)));
    }

    public DateOnly Date { get; }
    public string Key => Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public IReadOnlyList<Item> Items { get; private set; } = [];

    [ObservableProperty] public partial string DayText { get; private set; } = "";
    [ObservableProperty] public partial string CountsText { get; private set; } = "";
    [ObservableProperty] public partial string StatusText { get; private set; } = "";
    [ObservableProperty] public partial string Reason { get; private set; } = "";
    [ObservableProperty] public partial bool? IsIncluded { get; private set; }
    [ObservableProperty] public partial bool IsAllImported { get; private set; }

    public IAsyncRelayCommand ToggleCommand { get; }

    internal void Update(PhotoDay day, IReadOnlyList<Item> items, IReadOnlySet<ItemId> included)
    {
        Items = items;
        var utc = items.Count > 0 ? items[0].Time.CaptureUtc : day.Date.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc);
        DayText = $"{Fmt.DayWithWeekday(day.Date)} · {ZoneNames.Abbreviation(day.TzId, utc)}";
        var parts = new List<string>();
        var photos = items.Count(i => i.Raw.Unit is PhotoUnit);
        if (photos > 0) parts.Add(Fmt.Count(photos, "photo", "photos"));
        foreach (var kind in new[] { SetKind.Panorama, SetKind.Hyperlapse })
        {
            var n = items.Count(i => i.Raw.Unit is SetUnit s && s.Kind == kind);
            if (n > 0) parts.Add(Fmt.Count(n, kind == SetKind.Panorama ? "pano set" : "hyperlapse set", kind == SetKind.Panorama ? "pano sets" : "hyperlapse sets"));
        }
        parts.Add(Fmt.Size(items.Sum(i => i.Raw.Bytes)));
        CountsText = string.Join(" · ", parts);
        var status = new List<string> { Fmt.Count(items.Count(i => i.Newness is IsNew), "new", "new") };
        var probably = items.Count(i => i.Newness is ProbablyImported);
        if (probably > 0) status.Add(Fmt.Count(probably, "probably imported", "probably imported"));
        var confirmed = items.Count(i => i.Newness is Decided { Kind: DecisionKind.AssumedImported });
        if (confirmed > 0) status.Add(Fmt.Count(confirmed, "confirmed by you", "confirmed by you"));
        StatusText = string.Join(" · ", status);
        Reason = day.Reason;
        var inc = items.Count(i => included.Contains(i.Raw.Unit.Id));
        IsIncluded = inc == 0 ? false : inc == items.Count ? true : null;
        IsAllImported = items.All(i => i.Newness is Imported or Decided);
    }

    public override string ToString() => DayText;
}

/// <summary>One tile of the photo wall; a set is one tile (Ref §9.8).</summary>
public sealed partial class PhotoTileVm : ObservableObject, IKeyed
{
    public PhotoTileVm(Item item, Func<PlanEdit, Task> apply, Func<IReadOnlyList<Item>, Task> undo)
    {
        Item = item;
        ToggleCommand = new AsyncRelayCommand(() => apply(new SetIncluded([Item.Raw.Unit.Id], !IsIncluded)));
        UndoCommand = new AsyncRelayCommand(() => undo([Item]), () => CanUndo);
    }

    public Item Item { get; private set; }
    public ItemId ThumbKey => Item.Raw.Unit.Id;
    public string Key => Item.Raw.Unit.Id.CardRelPath;

    [ObservableProperty] public partial string Text { get; private set; } = "";
    [ObservableProperty] public partial string StatusText { get; private set; } = "";
    [ObservableProperty] public partial string? PairText { get; private set; }
    [ObservableProperty] public partial bool IsIncluded { get; private set; }
    [ObservableProperty] public partial bool CanUndo { get; private set; }

    public IAsyncRelayCommand ToggleCommand { get; }
    public IAsyncRelayCommand UndoCommand { get; }

    internal void Update(Item item, bool included, SetPlacement? placement)
    {
        Item = item;
        Text = item.Raw.Unit is SetUnit s && placement is { } p ? SetText(s, p) : item.Raw.Name;
        StatusText = ClipRowVm.Status(item).Text;
        PairText = item.Raw.Unit is PhotoUnit { JpgTwin: not null } ? "DNG+JPG" : null;
        IsIncluded = included;
        CanUndo = item.Newness is Decided { Kind: DecisionKind.AssumedImported };
        UndoCommand.NotifyCanExecuteChanged();
    }

    internal static string SetText(SetUnit s, SetPlacement p)
    {
        var kind = s.Kind == SetKind.Panorama ? "Panorama" : "Hyperlapse";
        var text = $"{kind} · {Fmt.Count(s.Members.Length, "frame", "frames")} → {p.FolderName}";
        if (p.Resolution == SetResolution.Resume)
            text += $" (resuming: {Fmt.Count(p.MembersToCopy.Length, "frame", "frames")} missing)";
        else if (!string.Equals(p.FolderName, s.SetName, StringComparison.Ordinal) && p.Resolution != SetResolution.Imported)
            text += $" ({s.SetName} holds a different set)";
        return text;
    }

    public override string ToString() => Text;
}

/// <summary>The Photos tab (Ref §9.8).</summary>
public sealed partial class PhotosTabVm : ObservableObject
{
    private readonly Func<PlanEdit, Task> _apply;
    private readonly Func<IReadOnlyList<Item>, Task> _undoConfirmed;
    private PlanIndex? _index;

    public PhotosTabVm(Func<PlanEdit, Task> apply, Func<IReadOnlyList<Item>, Task> undoConfirmed)
    {
        _apply = apply;
        _undoConfirmed = undoConfirmed;
    }

    public ObservableCollection<PhotoDayVm> Days { get; } = [];
    public ObservableCollection<PhotoTileVm> Tiles { get; } = [];

    [ObservableProperty] public partial PhotoDayVm? SelectedDay { get; set; }
    [ObservableProperty] public partial bool ShowImportedDays { get; set; }
    [ObservableProperty] public partial string Header { get; private set; } = "Photos";
    [ObservableProperty] public partial string ShowImportedText { get; private set; } = "";

    internal void Update(PlanIndex ix)
    {
        _index = ix;
        var plan = ix.Plan;
        var all = plan.Base.PhotoDays.OrderBy(d => d.Date)
                      .Select(d => (Day: d, Items: d.Items.Where(ix.Items.ContainsKey).Select(i => ix.Items[i]).ToList()))
                      .ToList();
        var imported = all.Where(x => x.Items.Count > 0 && x.Items.All(i => i.Newness is Imported or Decided)).ToList();
        var visible = ShowImportedDays ? all : all.Except(imported).ToList();
        CollectionSync.Sync(Days, visible, x => x.Day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                            x => new PhotoDayVm(x.Day.Date, _apply),
                            (vm, x) => vm.Update(x.Day, x.Items, plan.Included));
        Header = "Photos · " + Fmt.Count(all.Count, "day", "days");
        ShowImportedText = imported.Count == 0 ? "" : $"Show imported days ({imported.Count.ToString(CultureInfo.InvariantCulture)})";
        if (SelectedDay is not null && !Days.Contains(SelectedDay)) SelectedDay = null;
        RebuildTiles();
    }

    partial void OnSelectedDayChanged(PhotoDayVm? value) => RebuildTiles();

    partial void OnShowImportedDaysChanged(bool value)
    {
        if (_index is { } ix) Update(ix);
    }

    private void RebuildTiles()
    {
        if (_index is not { } ix || SelectedDay is null)
        {
            Tiles.Clear();
            return;
        }
        CollectionSync.Sync(Tiles, SelectedDay.Items, i => i.Raw.Unit.Id.CardRelPath,
                            i => new PhotoTileVm(i, _apply, _undoConfirmed),
                            (vm, i) => vm.Update(i, ix.Plan.Included.Contains(i.Raw.Unit.Id), ix.Plan.Base.Sets.GetValueOrDefault(i.Raw.Unit.Id)));
    }
}
