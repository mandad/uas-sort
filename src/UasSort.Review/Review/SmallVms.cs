// src/UasSort.Review/Review/SmallVms.cs
namespace UasSort.Review;

public enum ChipKind { Unfinished, Conflicts, CheckDate, GpsGuessed, ClockMismatch, EmptyName, CrossDayAppend, EmphasisedSplit, PinChanged, ConflictingPins }

public enum RetargetKind { Auto, NewFolder, Append, Browse, Skip }

public sealed class QuickFixVm
{
    public QuickFixVm(string label, Func<Task> run)
    {
        Label = label;
        Command = new AsyncRelayCommand(run);
    }

    public QuickFixVm(QuickFix fix, IReviewActions actions) : this(fix.Label, () => actions.ApplyQuickFixAsync(fix)) { }

    public string Label { get; }
    public IAsyncRelayCommand Command { get; }
    public override string ToString() => Label;
}

public sealed class ChipVm(ChipKind kind, string text, string? tooltip, IReadOnlyList<QuickFixVm> actions)
{
    public ChipKind Kind { get; } = kind;
    public string Text { get; } = text;
    public string? Tooltip { get; } = tooltip;
    public IReadOnlyList<QuickFixVm> Actions { get; } = actions;
    public override string ToString() => Text;
}

public sealed class ThumbVm(ItemId key)
{
    public ItemId Key { get; } = key;
}

public sealed class SuggestionVm(Suggestion s)
{
    public Suggestion Model { get; } = s;
    public string Text => Model.Text;
    public string Detail => Model.Source switch
    {
        DescSource.ExistingFolder => "existing folder",
        DescSource.Ledger => "used before" + Away(),
        DescSource.Feature => "feature" + Away(),
        DescSource.Place => "place" + Away(),
        DescSource.Town => "town" + Away(),
        DescSource.User => "your name",
        DescSource.None => "",
        _ => "",
    };
    public override string ToString() => Text;
    private string Away() => Model.Away is { } d ? " · " + Fmt.Miles(d) : "";
}

public sealed class RetargetOptionVm(RetargetKind kind, string label, string? detail, string? folderPath, DateOnly? folderDate)
{
    public RetargetKind Kind { get; } = kind;
    public string Label { get; } = label;
    public string? Detail { get; } = detail;
    public string? FolderPath { get; } = folderPath;
    public DateOnly? FolderDate { get; } = folderDate;
    public override string ToString() => Label;
}

/// <summary>The boundary chip drawn as the header of the next group card (Ref §8.3, §9.3).</summary>
public sealed class BoundaryChipVm
{
    public BoundaryChipVm(Boundary b, IReviewActions actions)
    {
        Model = b;
        var core = b.Cause switch
        {
            BoundaryCause.Distance => $"{(b.Jump is { } j ? Fmt.Miles(j) : "far")} jump · {Fmt.Gap(b.Gap)}",
            BoundaryCause.DayGap => Fmt.Count(b.DayGap, "day", "days"),
            BoundaryCause.LibraryFolder => "different library folder",
            BoundaryCause.UserSplit => "split by you",
            _ => "",
        };
        Text = $"── {core} ──";
        CanMerge = b.Cause != BoundaryCause.LibraryFolder;
        ButtonText = b.Cause == BoundaryCause.UserSplit ? "Undo split" : "Merge";
        MergeTooltip = CanMerge ? null : "These clips are already in two different folders";
        MergeCommand = new AsyncRelayCommand(() => actions.MergeAsync(b.Left.Anchor, b.Right.Anchor), () => CanMerge);
    }

    public Boundary Model { get; }
    public string Text { get; }
    public string ButtonText { get; }
    public bool CanMerge { get; }
    public string? MergeTooltip { get; }
    public IAsyncRelayCommand MergeCommand { get; }
    public override string ToString() => Text;
}

/// <summary>The day-change banner drawn inside the first clip row of the new day (Ref §8.3, §9.5).</summary>
public sealed class DaySplitBannerVm
{
    public DaySplitBannerVm(DaySplit s, IReviewActions actions)
    {
        Model = s;
        var apart = s.Apart is { } d ? $"{Fmt.Miles(d)} apart" : "location unknown";
        Text = $"── {Fmt.Day(s.From)} → {Fmt.Day(s.To)} · {apart} · {Fmt.Gap(s.Gap)} ──";
        SplitCommand = new AsyncRelayCommand(() => actions.SplitBeforeAsync(s.FirstOfDay));
    }

    public DaySplit Model { get; }
    public string Text { get; }
    public bool IsEmphasised => Model.Emphasised;
    public string? EmphasisText => Model.Emphasised ? "Likely separate outing" : null;
    public IAsyncRelayCommand SplitCommand { get; }
    public override string ToString() => Text;
}

public abstract partial class TimelineEntryVm : ObservableObject, IKeyed
{
    public abstract string Key { get; }
}

/// <summary>A run of AlreadyImported groups folded into one grey row (Ref §9.3).</summary>
public sealed partial class FoldedRunVm(string key) : TimelineEntryVm
{
    public override string Key { get; } = key;
    public IReadOnlyList<VideoGroup> Groups { get; private set; } = [];
    [ObservableProperty] public partial string Text { get; private set; } = "";
    [ObservableProperty] public partial int ClipCount { get; private set; }
    [ObservableProperty] public partial bool IsExpanded { get; set; }

    internal void Update(IReadOnlyList<VideoGroup> run)
    {
        Groups = run;
        Text = Fmt.Count(run.Count, "group", "groups") + " already in library";
        ClipCount = run.Sum(g => g.Videos.Length);
    }

    public override string ToString() => Text;
}
