// src/UasSort.Review/Stages/CardStageVm.cs
namespace UasSort.Review;

public sealed class CardRowVm
{
    public CardRowVm(CardCandidate c, Action<CardRowVm> use)
    {
        Candidate = c;
        var v = c.Volume;
        Text = $"{v.Root} · {v.Identity.Label ?? "no label"} · {v.Identity.FileSystem}";
        KindText = (c.IsDjiCard ? $"DJI card · {Fmt.Count(c.MediaCount, "media file", "media files")}" : c.NotCardReason ?? "not a DJI card")
                   + (v.IsReadOnlyVolume ? " · write-protected" : "");
        UseCommand = new RelayCommand(() => use(this), () => c.IsDjiCard);
    }

    public CardCandidate Candidate { get; }
    public VolumeInfo Volume => Candidate.Volume;
    public string Text { get; }
    public string KindText { get; }
    public bool IsDjiCard => Candidate.IsDjiCard;
    public bool IsWriteProtected => Candidate.Volume.IsReadOnlyVolume;
    public IRelayCommand UseCommand { get; }
    public override string ToString() => Text;
}

/// <summary>Shown only when zero or several DJI cards are found; never auto-picks among several (Ref §9.1 Card; Review Focus #5).</summary>
public sealed partial class CardStageVm : ObservableObject
{
    private readonly IVolumeProvider _volumes;
    private readonly Func<IReadOnlyList<VolumeInfo>, IReadOnlyList<CardCandidate>> _detect;
    private readonly Func<string, VolumeInfo?, CardSourceCheck> _validate;

    public CardStageVm(IVolumeProvider volumes, Func<IReadOnlyList<VolumeInfo>, IReadOnlyList<CardCandidate>> detect,
                       Func<string, VolumeInfo?, CardSourceCheck> validate)
    {
        _volumes = volumes;
        _detect = detect;
        _validate = validate;
        RescanCommand = new RelayCommand(() => Refresh());
    }

    public ObservableCollection<CardRowVm> Rows { get; } = [];

    [ObservableProperty] public partial string StatusText { get; private set; } = "";
    [ObservableProperty] public partial string? Message { get; private set; }

    public IRelayCommand RescanCommand { get; }
    public event Action<CardSource>? CardChosen;

    /// <summary>Re-detects; returns (and raises CardChosen for) the source only when exactly one DJI card is present and valid.</summary>
    public CardSource? Refresh()
    {
        Message = null;
        var candidates = _detect(_volumes.GetVolumes());
        Rows.Clear();
        foreach (var c in candidates) Rows.Add(new CardRowVm(c, Use));
        var dji = candidates.Where(c => c.IsDjiCard).ToList();
        StatusText = dji.Count switch
        {
            0 => "No DJI card found. Insert the card, then Rescan (F5), or browse to a folder.",
            1 => "",
            _ => string.Create(CultureInfo.InvariantCulture, $"{dji.Count} DJI cards found. Pick one; each is offloaded on its own."),
        };
        return dji.Count == 1 ? Choose(dji[0].Volume.Root, dji[0].Volume) : null;
    }

    public void Browse(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path)) Choose(path, null);
    }

    private void Use(CardRowVm row) => Choose(row.Volume.Root, row.Volume);

    private CardSource? Choose(string path, VolumeInfo? detected)
    {
        switch (_validate(path, detected))
        {
            case SourceOk ok:
                Message = null;
                CardChosen?.Invoke(ok.Source);
                return ok.Source;
            case SourceRefused refused:
                Message = refused.Reason;
                return null;
        }
        return null;
    }
}
