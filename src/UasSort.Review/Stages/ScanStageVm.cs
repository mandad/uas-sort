// src/UasSort.Review/Stages/ScanStageVm.cs
using System.Text.RegularExpressions;

namespace UasSort.Review;

/// <summary>The Scan stage: phases, progress, Cancel (Ref §9.1 Scan, §12 card removed during scan).</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "The CancellationTokenSource has no timer or linked tokens and its WaitHandle is never read, so it holds nothing to release; each RunAsync disposes the previous one, and the registry contract has no Dispose.")]
public sealed partial class ScanStageVm : ObservableObject
{
    private readonly Func<CardSource, IProgress<ScanProgress>, CancellationToken, Task<ScanResult>> _scan;
    private readonly Func<ScanResult, PlanBase> _prepare;
    private readonly Settings _settings;
    private readonly IUiDispatcher _ui;
    private CancellationTokenSource? _cts;

    public ScanStageVm(Func<CardSource, IProgress<ScanProgress>, CancellationToken, Task<ScanResult>> scan, Func<ScanResult, PlanBase> prepare,
                       Settings settings, IUiDispatcher ui)
    {
        _scan = scan;
        _prepare = prepare;
        _settings = settings;
        _ui = ui;
        CancelCommand = new RelayCommand(() => _cts?.Cancel());
    }

    [ObservableProperty] public partial string PhaseText { get; private set; } = "Listing card";
    [ObservableProperty] public partial double Progress { get; private set; }
    [ObservableProperty] public partial bool IsIndeterminate { get; private set; } = true;
    [ObservableProperty] public partial string? ErrorText { get; private set; }
    [ObservableProperty] public partial bool IsRunning { get; private set; }

    public IRelayCommand CancelCommand { get; }

    [GeneratedRegex(@"^DJI_\d{14}_(\d{4})", RegexOptions.CultureInvariant)]
    private static partial Regex DjiName();

    public static string PhaseTextFor(ScanProgress p, Settings s) => p.Phase switch
    {
        ScanPhase.ListingCard => "Listing card",
        ScanPhase.ListingLibrary => $"Listing library ({string.Join(", ", new[] { s.VideoRoot, s.PhotoRoot }.Concat(s.PreviousPhotoRoots).Select(Fmt.Drive).Distinct(StringComparer.OrdinalIgnoreCase))})",
        ScanPhase.ReadingLedger => p.Total > 0 ? $"Reading ledger ({Fmt.Count(p.Total, "PC", "PCs")})" : "Reading ledger",
        ScanPhase.ReadingMetadata => string.Create(CultureInfo.InvariantCulture, $"Reading metadata {p.Done}/{p.Total}") + (p.Current is { } c ? " · " + Short(c) : ""),
        ScanPhase.BuildingPlan => "Building plan",
        _ => p.Phase.ToString(),
    };

    public async Task<PlanBase?> RunAsync(CardSource source)
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        IsRunning = true;
        ErrorText = null;
        var progress = new UiProgress<ScanProgress>(_ui, p =>
        {
            PhaseText = PhaseTextFor(p, _settings);
            IsIndeterminate = p.Total == 0;
            Progress = p.Total == 0 ? 0 : (double)p.Done / p.Total;
        });
        try
        {
            var result = await _scan(source, progress, _cts.Token).ConfigureAwait(true);
            PhaseText = "Building plan";
            return await Task.Run(() => _prepare(result), _cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (IOException ex)
        {
            ErrorText = $"The card was removed or can't be read. Reinsert it, then Rescan. ({ex.Message})";
            return null;
        }
        catch (UnsafeIoException ex)
        {
            ErrorText = "Internal safety stop: " + ex.Message;
            return null;
        }
        finally
        {
            IsRunning = false;
        }
    }

    private static string Short(string name)
    {
        var m = DjiName().Match(name);
        return m.Success ? "DJI_…" + m.Groups[1].Value : name;
    }
}
