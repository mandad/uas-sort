using System.Collections.ObjectModel;
using System.IO.Hashing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Spike.Core;

// C# 15: closed hierarchy (exhaustive switch without default arm)
public closed record class CopyOutcome;
public sealed record class Copied(string Path, UInt128 Hash) : CopyOutcome;
public sealed record class Skipped(string Reason) : CopyOutcome;
public sealed record class Conflict(string Existing, long ExistingSize) : CopyOutcome;

// C# 15: union type
public record ByteRange(long Start, long Length);
public record NoFix(string Why);
public union GpsProbe(ByteRange, NoFix);

public static class Describe
{
    public static string Outcome(CopyOutcome o) => o switch
    {
        Copied(var p, _) => $"copied {p}",
        Skipped(var r) => $"skipped: {r}",
        Conflict(var e, var s) => $"conflict with {e} ({s} bytes)",
    };

    public static string Probe(GpsProbe p) => p switch
    {
        ByteRange r => $"{r.Start}+{r.Length}",
        NoFix n => n.Why,
    };

    public static UInt128 Hash(ReadOnlySpan<byte> data) => XxHash128.HashToUInt128(data);

    public static string TimeZoneAt(double lat, double lon) => GeoTimeZone.TimeZoneLookup.GetTimeZone(lat, lon).Result;

    // C# 15: collection expression arguments
    public static HashSet<string> Exts() => [with(StringComparer.OrdinalIgnoreCase), ".mp4", ".MP4", ".dng", ".jpg"];
}

public sealed partial class RowVm : ObservableObject
{
    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial double SizeMb { get; set; }
    [ObservableProperty] public partial bool Include { get; set; } = true;
    [ObservableProperty] public partial string Reason { get; set; } = "";
}

public sealed partial class GroupVm : ObservableObject
{
    [ObservableProperty] public partial string Title { get; set; } = "";
    public ObservableCollection<RowVm> Rows { get; } = [];
}

public sealed partial class MainVm : ObservableObject
{
    [ObservableProperty] public partial double RadiusMiles { get; set; } = 50;
    [ObservableProperty] public partial int GapDays { get; set; } = 1;
    [ObservableProperty] public partial string Status { get; set; } = "Ready";
    public ObservableCollection<GroupVm> Groups { get; } = [];
    public ObservableCollection<RowVm> Rows { get; } = [];

    public MainVm()
    {
        for (int g = 0; g < 3; g++)
        {
            var gv = new GroupVm { Title = $"2026-07-2{5 + g} Group {g}" };
            for (int i = 0; i < 20; i++) { var r = new RowVm { Name = $"DJI_2026072{5 + g}_{i:0000}_D.MP4", SizeMb = 100 + i }; gv.Rows.Add(r); Rows.Add(r); }
            Groups.Add(gv);
        }
    }

    [RelayCommand]
    private void Regroup() => Status = $"Regrouped at {RadiusMiles:0} mi, G={GapDays}";
}
