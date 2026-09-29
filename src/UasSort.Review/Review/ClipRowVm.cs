// src/UasSort.Review/Review/ClipRowVm.cs
namespace UasSort.Review;

/// <summary>One clip in the clip list (Ref §9.5).</summary>
public sealed partial class ClipRowVm : ObservableObject, IKeyed
{
    private const string UnfinishedTooltip =
        "Unfinished recording. Powering the drone on with this card inserted may repair it (UNVERIFIED on the Air 3S). Rescan afterwards, or tick to copy as-is.";

    private readonly IReviewActions _actions;

    public ClipRowVm(ItemId id, IReviewActions actions)
    {
        Id = id;
        _actions = actions;
        ToggleIncludedCommand = new AsyncRelayCommand(() => _actions.SetIncludedAsync([Id], !IsIncluded), () => !IsReadOnly);
        SplitBeforeCommand = new AsyncRelayCommand(() => _actions.SplitBeforeAsync(Id), () => !IsReadOnly);
    }

    public ItemId Id { get; }
    public ItemId ThumbKey => Id;
    public string Key => Id.CardRelPath;

    [ObservableProperty] public partial string Name { get; private set; } = "";
    [ObservableProperty] public partial string TimeText { get; private set; } = "";
    [ObservableProperty] public partial string TimeGlyph { get; private set; } = "";
    [ObservableProperty] public partial string TimeTooltip { get; private set; } = "";
    [ObservableProperty] public partial string SizeText { get; private set; } = "";
    [ObservableProperty] public partial string StatusText { get; private set; } = "";
    [ObservableProperty] public partial string StatusTooltip { get; private set; } = "";
    [ObservableProperty] public partial string DistanceText { get; private set; } = "";
    [ObservableProperty] public partial bool IsIncluded { get; private set; }
    [ObservableProperty] public partial bool IsReadOnly { get; private set; }
    [ObservableProperty] public partial DaySplitBannerVm? Banner { get; private set; }

    public IAsyncRelayCommand ToggleIncludedCommand { get; }
    public IAsyncRelayCommand SplitBeforeCommand { get; }
    public Item? Item { get; private set; }

    internal void Update(Item item, bool included, VideoGroup group, bool readOnly)
    {
        Item = item;
        Name = item.Raw.Name;
        TimeText = ClockText.TimeCell(item);
        TimeGlyph = ClockText.SourceGlyph(item.Time.Source);
        TimeTooltip = ClockText.TimeTooltip(item);
        SizeText = Fmt.Size(item.Raw.Bytes);
        (StatusText, StatusTooltip) = Status(item);
        DistanceText = item.Gps is { } fix && group.Centroid is { } c ? Fmt.Miles(GeoMath.Haversine(fix.Point, c)) : "";
        IsIncluded = included;
        IsReadOnly = readOnly;
        var split = group.DaySplits.FirstOrDefault(s => s.FirstOfDay == Id);
        if (split is null) Banner = null;
        else if (Banner is null || Banner.Model != split) Banner = new DaySplitBannerVm(split, _actions);
        ToggleIncludedCommand.NotifyCanExecuteChanged();
        SplitBeforeCommand.NotifyCanExecuteChanged();
    }

    internal static (string Text, string Tooltip) Status(Item item)
    {
        if (item.Flags.HasFlag(ItemFlags.Truncated)) return ("Unfinished", UnfinishedTooltip);
        return item.Newness switch
        {
            IsNew n => ("New", n.Why switch
            {
                NewReason.NoMatch => "Not in the library",
                NewReason.SeenNotCopied => "Seen before, not copied",
                NewReason.AfterWatermark => "After the last imported video",
                NewReason.NearWatermark => "Near the last imported video; time estimated",
                NewReason.DayHasNewVideos => "Day has new videos",
                _ => "New",
            }),
            Imported im => ("Imported", im.Why),
            Decided d => d.Kind == DecisionKind.Dismissed
                ? ("Dismissed", $"You marked it not needed on {Fmt.Day(DateOnly.FromDateTime(d.AtUtc))}")
                : ("Confirmed by you", $"You recorded it as imported on {Fmt.Day(DateOnly.FromDateTime(d.AtUtc))}"),
            ProbablyImported p => ("Probably imported", p.Why),
            Conflict c => ("Conflict", $"A different file named {item.Raw.Name} exists: {c.ExistingPath}, {Fmt.Size(c.ExistingSize)}"),
        };
    }

    public override string ToString() => Name;
}
