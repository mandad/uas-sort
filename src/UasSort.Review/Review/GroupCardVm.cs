// src/UasSort.Review/Review/GroupCardVm.cs
namespace UasSort.Review;

/// <summary>One group on the timeline, with its preceding boundary chip as the header (Ref §9.3, §9.4).</summary>
public sealed partial class GroupCardVm : TimelineEntryVm
{
    private readonly IReviewActions _actions;
    private PlanIndex? _index;

    public GroupCardVm(GroupId id, IReviewActions actions)
    {
        Id = id;
        _actions = actions;
        CommitDescriptionCommand = new AsyncRelayCommand(() => _actions.RenameAsync(this, Description), () => !IsDescriptionReadOnly);
        NewFolderInsteadCommand = new AsyncRelayCommand(NewFolderInsteadAsync);
    }

    public GroupId Id { get; }
    public ItemId Anchor => Id.Anchor;
    public override string Key => "g:" + Id.Anchor.CardRelPath;
    public VideoGroup? Group { get; private set; }

    [ObservableProperty] public partial BoundaryChipVm? Chip { get; private set; }
    [ObservableProperty] public partial string Swatch { get; private set; } = MapProjection.ImportedGrey;
    [ObservableProperty] public partial string Description { get; set; } = "";
    [ObservableProperty] public partial bool DescriptionIsSuggestion { get; private set; }
    [ObservableProperty] public partial bool IsDescriptionReadOnly { get; private set; }
    [ObservableProperty] public partial string? ReadOnlyHint { get; private set; }
    [ObservableProperty] public partial string Badge { get; private set; } = "";
    [ObservableProperty] public partial string? ConfidenceText { get; private set; }
    [ObservableProperty] public partial string TargetPath { get; private set; } = "";
    [ObservableProperty] public partial string DateRangeText { get; private set; } = "";
    [ObservableProperty] public partial IReadOnlyList<string> ZoneBadges { get; private set; } = [];
    [ObservableProperty] public partial string LocationText { get; private set; } = "";
    [ObservableProperty] public partial string VideoCountsText { get; private set; } = "";
    [ObservableProperty] public partial string PhotoCountsText { get; private set; } = "";
    [ObservableProperty] public partial IReadOnlyList<ThumbVm> Thumbs { get; private set; } = [];
    [ObservableProperty] public partial string? MoreThumbsText { get; private set; }
    [ObservableProperty] public partial bool HasClockMismatch { get; private set; }

    public ObservableCollection<SuggestionVm> Suggestions { get; } = [];
    public ObservableCollection<ChipVm> Chips { get; } = [];
    public IAsyncRelayCommand CommitDescriptionCommand { get; }
    public IAsyncRelayCommand NewFolderInsteadCommand { get; }

    /// <summary>Built when the target DropDownButton's flyout opens (Ref §9.4 item 4).</summary>
    public IReadOnlyList<RetargetOptionVm> RetargetOptions()
    {
        var list = new List<RetargetOptionVm>
        {
            new(RetargetKind.Auto, "Auto (proposed)", null, null, null),
            new(RetargetKind.NewFolder, "New folder", null, null, null),
        };
        if (Group is { } g && _index is { } ix) list.AddRange(ix.Candidates(g));
        list.Add(new(RetargetKind.Browse, "Browse existing…", null, null, null));
        list.Add(new(RetargetKind.Skip, "Skip this group", null, null, null));
        return list;
    }

    internal void Update(VideoGroup g, PlanIndex ix)
    {
        Group = g;
        _index = ix;
        var members = g.Videos.Select(v => ix.Items[v]).OrderBy(i => i.Time.CaptureUtc).ToList();

        Chip = ix.Before(g.Id) is { } b ? (Chip?.Model == b ? Chip : new BoundaryChipVm(b, _actions)) : null;
        Swatch = MapProjection.Color(g);
        (Badge, ConfidenceText, TargetPath) = g.Target switch
        {
            NewFolder n => ("NEW FOLDER", (string?)null, n.RelPath),
            Append a => ("APPEND", a.Confidence == Confidence.Medium ? $"Medium · {a.Why}" : "High", Relative(a.Folder.FullPath, ix.VideoRoot)),
            AlreadyImported ai => ("ALREADY IN LIBRARY", null, Relative(ai.Folder.FullPath, ix.VideoRoot)),
            NothingToCopy x => ("NOTHING TO COPY", null, x.Summary),
            SkipGroup => ("SKIP", null, "Not copied"),
        };
        IsDescriptionReadOnly = !g.DescriptionEditable || g.Target is Append or AlreadyImported;
        ReadOnlyHint = g.Target is Append ? "Appending to an existing folder · [New folder instead]" : null;
        if (!string.Equals(Description, g.Description, StringComparison.Ordinal)) Description = g.Description;
        DescriptionIsSuggestion = g.DescSource is DescSource.Ledger or DescSource.Feature or DescSource.Place or DescSource.Town;
        CommitDescriptionCommand.NotifyCanExecuteChanged();

        Suggestions.Clear();
        foreach (var s in g.Suggestions) Suggestions.Add(new SuggestionVm(s));

        var abbrs = members.Select(i => ZoneNames.Abbreviation(i.Time.TzId, i.Time.CaptureUtc)).Distinct(StringComparer.Ordinal).ToList();
        ZoneBadges = abbrs;
        var zone = abbrs.Count == 1 ? " " + abbrs[0] : "";
        DateRangeText = g.Start == g.End && members.Count > 0
            ? $"{Fmt.Day(g.Start)} {Fmt.Clock(members[0].Time.LocalTime)}–{Fmt.Clock(members[^1].Time.LocalTime)}{zone}"
            : Fmt.DateRange(g.Start, g.End) + (zone.Length > 0 ? " ·" + zone : "");

        var place = g.Suggestions.FirstOrDefault(s => s.Source is DescSource.Feature or DescSource.Place or DescSource.Town);
        var placeText = place is null ? "" : (place.Text.StartsWith("near ", StringComparison.Ordinal) ? place.Text : "near " + place.Text) + " · ";
        LocationText = g.Centroid is null ? "no GPS" : $"{placeText}spread {Fmt.Miles(g.Spread)}";

        var included = members.Where(i => ix.Plan.Included.Contains(i.Raw.Unit.Id)).ToList();
        VideoCountsText = string.Create(CultureInfo.InvariantCulture, $"Videos {included.Count} new / {members.Count}")
                          + (included.Count > 0 ? " · " + Fmt.Size(included.Sum(i => i.Raw.Bytes)) : "");
        PhotoCountsText = ix.PhotoCounts(g);
        Thumbs = [.. g.Videos.Take(8).Select(v => new ThumbVm(v))];
        MoreThumbsText = g.Videos.Length > 8 ? string.Create(CultureInfo.InvariantCulture, $"+{g.Videos.Length - 8}") : null;

        HasClockMismatch = members.Exists(i => i.Flags.HasFlag(ItemFlags.ClockMismatch));
        Chips.Clear();
        foreach (var chip in BuildChips(g, members, ix)) Chips.Add(chip);
    }

    private List<ChipVm> BuildChips(VideoGroup g, List<Item> members, PlanIndex ix)
    {
        var chips = new List<ChipVm>();
        var unfinished = members.Count(i => i.Flags.HasFlag(ItemFlags.Truncated));
        if (unfinished > 0) chips.Add(new(ChipKind.Unfinished, Fmt.Count(unfinished, "unfinished recording", "unfinished recordings"), null, []));
        var conflicts = members.Count(i => i.Newness is Conflict);
        if (conflicts > 0) chips.Add(new(ChipKind.Conflicts, Fmt.Count(conflicts, "conflict", "conflicts"), null, []));
        if (members.Exists(i => i.Flags.HasFlag(ItemFlags.CheckDate))) chips.Add(new(ChipKind.CheckDate, "Check date", null, []));
        if (members.Exists(i => i.Flags.HasFlag(ItemFlags.GpsGuessed))) chips.Add(new(ChipKind.GpsGuessed, "GPS guessed", null, []));
        if (members.Find(i => i.Flags.HasFlag(ItemFlags.ClockMismatch)) is { } mm)
            chips.Add(new(ChipKind.ClockMismatch, "clock ≠ local", ClockText.ChipTooltip(mm), []));
        if (g.Target is NewFolder && g.Description.Length == 0) chips.Add(new(ChipKind.EmptyName, "Name this folder", null, []));
        if (g.Target is Append { Confidence: Confidence.Medium, Hint: { } hint })
            chips.Add(new(ChipKind.CrossDayAppend, hint.Text, null, [new QuickFixVm(new QuickFix("New folder instead", hint.Fix), _actions)]));
        if (g.DaySplits.FirstOrDefault(s => s.Emphasised) is { } split)
        {
            var days = g.End.DayNumber - g.Start.DayNumber + 1;
            var apart = split.Apart is { } d ? Fmt.Miles(d) + " apart" : "far apart";
            chips.Add(new(ChipKind.EmphasisedSplit, $"{Fmt.Count(days, "day", "days")} · {apart}", null,
                          [new QuickFixVm(new QuickFix("Split", [new SplitBefore(split.FirstOfDay)]), _actions)]));
        }
        foreach (var issue in ix.Plan.Issues.Where(i => i.Anchor == g.Id.Anchor))
        {
            var kind = issue.Code switch
            {
                IssueCode.PinMembershipChanged => ChipKind.PinChanged,
                IssueCode.ConflictingPins => ChipKind.ConflictingPins,
                _ => (ChipKind?)null,
            };
            if (kind is { } k) chips.Add(new(k, issue.Message, null, [.. issue.QuickFixes.Select(f => new QuickFixVm(f, _actions))]));
        }
        return chips;
    }

    private Task NewFolderInsteadAsync()
        => Group?.Target is Append { Hint: { } hint }
            ? _actions.ApplyQuickFixAsync(new QuickFix("New folder instead", hint.Fix))
            : _actions.RetargetAsync(this, new RetargetOptionVm(RetargetKind.NewFolder, "New folder", null, null, null));

    private static string Relative(string full, string root)
        => full.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) ? full[(root.TrimEnd('\\').Length + 1)..] : full;

    public override string ToString() => Description.Length > 0 ? Description : TargetPath;
}
