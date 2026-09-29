// src/UasSort.Review/Review/TuningVm.cs
namespace UasSort.Review;

/// <summary>Where TuningVm sends previews and commits (ReviewVm, over PlanSession).</summary>
public interface ITuningHost
{
    void Preview(Tuning t);
    Task CommitTuningAsync(Tuning t);
}

/// <summary>The R and G sliders under the map (Ref §9.7).</summary>
public sealed partial class TuningVm : ObservableObject, IDisposable
{
    public static readonly TimeSpan IdleCommit = TimeSpan.FromMilliseconds(400);
    public const double DefaultRadiusMiles = 50;
    public const int DefaultGapDays = 1;

    private readonly ITuningHost _host;
    private readonly TimeProvider _time;
    private readonly IUiDispatcher _ui;
    private ITimer? _idle;
    private bool _syncing;

    public TuningVm(ITuningHost host, TimeProvider time, IUiDispatcher ui)
    {
        _host = host;
        _time = time;
        _ui = ui;
        _syncing = true;
        RadiusMiles = DefaultRadiusMiles;
        GapDays = DefaultGapDays;
        _syncing = false;
        Committed = Current;
        ResetCommand = new AsyncRelayCommand(ResetAsync);
    }

    [ObservableProperty] public partial double RadiusMiles { get; set; }
    [ObservableProperty] public partial int GapDays { get; set; }
    [ObservableProperty] public partial string GroupCountText { get; private set; } = "";

    public string RadiusText => Fmt.Miles(Distance.FromMiles(RadiusMiles));
    public string GapText => Fmt.Count(GapDays, "day", "days");
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "Bound by x:Bind through the TuningVm instance (Review.Tuning.ResetText)")]
    public string ResetText => "Reset to 50 mi · 1 day";
    public bool IsDragging { get; private set; }
    public Tuning Current => new(RadiusMiles, GapDays);
    public Tuning Committed { get; private set; }
    public IAsyncRelayCommand ResetCommand { get; }

    partial void OnRadiusMilesChanged(double value)
    {
        var stepped = Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 5, 100);
        if (stepped != value) { RadiusMiles = stepped; return; }
        OnPropertyChanged(nameof(RadiusText));
        ValueChanged();
    }

    partial void OnGapDaysChanged(int value)
    {
        var clamped = Math.Clamp(value, 0, 7);
        if (clamped != value) { GapDays = clamped; return; }
        OnPropertyChanged(nameof(GapText));
        ValueChanged();
    }

    public void BeginDrag()
    {
        IsDragging = true;
        StopIdle();
    }

    public Task EndDragAsync()
    {
        IsDragging = false;
        StopIdle();
        return CommitAsync();
    }

    /// <summary>Shows a plan's tuning without previewing; skipped while the user is moving a slider.</summary>
    internal void Sync(Tuning shown, int groupCount)
    {
        GroupCountText = Fmt.Count(groupCount, "group", "groups");
        if (IsDragging || _idle is not null) return;
        _syncing = true;
        try
        {
            RadiusMiles = shown.RadiusMiles;
            GapDays = shown.GapDays;
        }
        finally
        {
            _syncing = false;
        }
    }

    internal void SetCommitted(Tuning t) => Committed = t;

    public void Dispose() => StopIdle();

    private void ValueChanged()
    {
        if (_syncing) return;
        _host.Preview(Current);
        if (IsDragging) return;
        StopIdle();
        _idle = _time.CreateTimer(_ => _ui.Post(() => _ = OnIdleAsync()), null, IdleCommit, Timeout.InfiniteTimeSpan);
    }

    private Task OnIdleAsync()
    {
        StopIdle();
        return CommitAsync();
    }

    private async Task CommitAsync()
    {
        var t = Current;
        if (t == Committed) return;
        Committed = t;
        await _host.CommitTuningAsync(t).ConfigureAwait(true);
    }

    private async Task ResetAsync()
    {
        StopIdle();
        _syncing = true;
        try
        {
            RadiusMiles = DefaultRadiusMiles;
            GapDays = DefaultGapDays;
        }
        finally
        {
            _syncing = false;
        }
        _host.Preview(Current);
        await CommitAsync().ConfigureAwait(true);
    }

    private void StopIdle()
    {
        _idle?.Dispose();
        _idle = null;
    }
}
