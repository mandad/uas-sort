// src/UasSort.Review/PhotoCleanup/PhotoCleanupRowVm.cs
namespace UasSort.Review;

/// <summary>One review row (spec 2026-10-04 §2 step 2): a photo unit or a set folder, with its Keep/Delete toggle.</summary>
public sealed partial class PhotoCleanupRowVm : ObservableObject, IKeyed
{
    public PhotoCleanupRowVm(PhotoRow row, Action<PhotoCleanupRowVm, RowDecision> set)
    {
        ArgumentNullException.ThrowIfNull(set);
        Row = row;
        KeepCommand = new RelayCommand(() => set(this, RowDecision.Keep));
        DeleteCommand = new RelayCommand(() => set(this, RowDecision.Delete));
    }

    public PhotoRow Row { get; private set; }
    public string Key => Row.Key;
    public ItemId ThumbKey => PhotoRootThumbnails.KeyOf(Row.Item);

    [ObservableProperty] public partial string? DayHeader { get; private set; }
    [ObservableProperty] public partial string Title { get; private set; } = "";
    [ObservableProperty] public partial string DetailText { get; private set; } = "";
    [ObservableProperty] public partial string StatusText { get; private set; } = "";
    [ObservableProperty] public partial bool IsVerified { get; private set; }
    [ObservableProperty] public partial RowDecision Decision { get; private set; }

    public IRelayCommand KeepCommand { get; }
    public IRelayCommand DeleteCommand { get; }

    internal void Update(PhotoRow row, RowDecision decision, string? dayHeader)
    {
        Row = row;
        var item = row.Item;
        Title = item.Kind == PhotoItemKind.Photo
            ? item.Members.Length > 1 ? $"{item.RelPath} + JPG" : item.RelPath
            : $"{KindText(item.SetKind)} · {item.RelPath}";
        var dates = item.FirstDate is { } a && item.LastDate is { } b ? Fmt.DateRange(a, b) : "date unknown";
        DetailText = item.Kind == PhotoItemKind.Photo
            ? $"{dates} · {Fmt.Size(item.Bytes)}"
            : $"{dates} · {Fmt.Count(item.Members.Length, "frame", "frames")} · {Fmt.Size(item.Bytes)}";
        StatusText = row.Verification.Text;
        IsVerified = row.Verification.Verified;
        Decision = decision;
        DayHeader = dayHeader;
    }

    private static string KindText(PhotoSetKind k) => k switch
    {
        PhotoSetKind.Panorama => "Panorama",
        PhotoSetKind.Hyperlapse => "Hyperlapse",
        _ => "Set",
    };

    public override string ToString() => $"{Title} · {DetailText} · {StatusText}";
}
