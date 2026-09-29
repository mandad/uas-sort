// src/UasSort.Review/Cleanup/CleanupRowVm.cs
namespace UasSort.Review;

public enum RowDecision { Undecided, Keep, Delete }

/// <summary>One not-in-library review row (Ref §10.6 "The not-in-library switch and the review list").</summary>
public sealed partial class CleanupRowVm : ObservableObject, IKeyed
{
    public CleanupRowVm(CleanupCandidate c, Action<CleanupRowVm, RowDecision> set)
    {
        ArgumentNullException.ThrowIfNull(set);
        Candidate = c;
        KeepCommand = new RelayCommand(() => set(this, RowDecision.Keep));
        DeleteCommand = new RelayCommand(() => set(this, RowDecision.Delete));
    }

    public CleanupCandidate Candidate { get; private set; }
    public ItemId Unit => Candidate.Unit;
    public ItemId ThumbKey => Candidate.Unit;
    public string Key => Candidate.Unit.CardRelPath;

    [ObservableProperty] public partial string DateText { get; private set; } = "";
    [ObservableProperty] public partial string LengthText { get; private set; } = "";
    [ObservableProperty] public partial string LocationText { get; private set; } = "";
    [ObservableProperty] public partial string SizeText { get; private set; } = "";
    [ObservableProperty] public partial string ReasonText { get; private set; } = "";
    [ObservableProperty] public partial string? Badge { get; private set; }
    [ObservableProperty] public partial RowDecision Decision { get; private set; }
    [ObservableProperty] public partial string Text { get; private set; } = "";

    public IRelayCommand KeepCommand { get; }
    public IRelayCommand DeleteCommand { get; }

    internal void Update(CleanupCandidate c, RowDecision decision, bool undecidedNewRow)
    {
        Candidate = c;
        var bytes = c.Files.Sum(f => f.Size);
        DateText = $"{Fmt.Day(c.LocalDate)} {Fmt.Clock(c.LocalTime)} {ZoneNames.Abbreviation(c.TzId, c.CaptureUtc)}";
        LengthText = c.Kind switch
        {
            ItemKind.Video when c.NotInLibrary == NotInLibraryReason.Unfinished || c.Duration is null => $"unfinished · ~{Fmt.Size(bytes)}",
            ItemKind.Video => Fmt.ClipLength(c.Duration!.Value),
            ItemKind.Photo => "photo",
            ItemKind.Set => ((c.SetFolder ?? c.Unit.CardRelPath).Contains("HYPERLAPSE", StringComparison.OrdinalIgnoreCase) ? "hyperlapse" : "panorama")
                            + " · " + Fmt.Count(c.Files.Length, "frame", "frames"),
            _ => "",
        };
        LocationText = c.PlaceLabel
                       ?? (c.Location is { } p ? string.Create(CultureInfo.InvariantCulture, $"{p.Lat:0.0000}, {p.Lon:0.0000}") : "no GPS");
        SizeText = Fmt.Size(bytes);
        ReasonText = c.Reason;
        Badge = c.TickedForOffload ? "ticked for offload" : undecidedNewRow ? "new in range" : null;
        Decision = decision;
        Text = string.Join(" · ", new[] { DateText, LengthText, LocationText, SizeText, ReasonText, Badge }.Where(s => !string.IsNullOrEmpty(s)));
    }

    public override string ToString() => Text;
}
