// src/UasSort.Review/Settings/SettingsPageVm.cs
namespace UasSort.Review;

public sealed class PreviousRootVm(string path, IRelayCommand forgetCommand)
{
    public string Path { get; } = path;
    public IRelayCommand ForgetCommand { get; } = forgetCommand;
    public override string ToString() => Path;
}

/// <summary>The Settings page (Ref §9.14). The ledger folder is derived from the video root and is never a setting.</summary>
public sealed partial class SettingsPageVm : ObservableObject, IDisposable
{
    public static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

    private readonly ISettingsStore _store;
    private readonly Func<string, ILedgerStore> _ledgerFor;
    private readonly IDirectoryLister _lister;
    private readonly IShellLauncher _shell;
    private readonly IDialogService _dialogs;
    private readonly IFreeSpace _space;
    private readonly TimeProvider _time;
    private readonly IUiDispatcher _ui;
    private readonly Func<LedgerSnapshot> _currentLedger;
    private readonly Func<string, string> _backupDirFor;
    private ITimer? _saveTimer;
    private bool _loading;

    public SettingsPageVm(Settings settings, ISettingsStore store, Func<string, ILedgerStore> ledgerFor, IDirectoryLister lister,
                          IShellLauncher shell, IDialogService dialogs, IFreeSpace space, TimeProvider time, IUiDispatcher ui,
                          Func<LedgerSnapshot> currentLedger, Func<string, string> backupDirFor)
    {
        _ui = ui;
        Current = settings;
        _store = store;
        _ledgerFor = ledgerFor;
        _lister = lister;
        _shell = shell;
        _dialogs = dialogs;
        _space = space;
        _time = time;
        _currentLedger = currentLedger;
        _backupDirFor = backupDirFor;
        KeepOnDeviceCommand = new RelayCommand(() => { _ledgerFor(Current.VideoRoot).KeepOnDevice(); RefreshLedger(); });
        OpenLedgerCommand = new RelayCommand(() => _shell.OpenFolder(LedgerFolder));
        OpenBackupCommand = new RelayCommand(() => _shell.OpenFolder(BackupFolder));
        CopyLedgerCommand = new AsyncRelayCommand(CopyLedgerAsync);
        StartEmptyCommand = new AsyncRelayCommand(StartEmptyAsync);

        _loading = true;
        VideoRoot = settings.VideoRoot;
        PhotoRoot = settings.PhotoRoot;
        RadiusMiles = settings.RadiusMiles;
        GapDays = settings.GapDays;
        IsSiteLocal = settings.DroneClockMode == StoredClockMode.SiteLocal;
        ClockZone = settings.DroneClockZone;
        CopyJpgTwin = settings.CopyJpgTwin;
        MapBase = settings.Map.Base;
        StreetsUrl = settings.Map.StreetsStyleUrl;
        StreetsDarkUrl = settings.Map.StreetsDarkStyleUrl;
        SatelliteUrl = settings.Map.SatelliteUrl;
        _loading = false;
        RebuildPrevious();
        RefreshLedger();
    }

    public Settings Current { get; private set; }
    public ObservableCollection<PreviousRootVm> PreviousPhotoRoots { get; } = [];

    [ObservableProperty] public partial string VideoRoot { get; private set; } = "";
    [ObservableProperty] public partial string PhotoRoot { get; private set; } = "";
    [ObservableProperty] public partial string VideoFreeText { get; private set; } = "";
    [ObservableProperty] public partial string PhotoFreeText { get; private set; } = "";
    [ObservableProperty] public partial string LedgerFolder { get; private set; } = "";
    [ObservableProperty] public partial string BackupFolder { get; private set; } = "";
    [ObservableProperty] public partial string LedgerStatus { get; private set; } = "";
    [ObservableProperty] public partial InfoSeverity LedgerSeverity { get; private set; }
    [ObservableProperty] public partial bool CanKeepOnDevice { get; private set; }
    [ObservableProperty] public partial string? NoHistoryPrompt { get; private set; }
    [ObservableProperty] public partial double RadiusMiles { get; set; }
    [ObservableProperty] public partial int GapDays { get; set; }
    [ObservableProperty] public partial bool IsSiteLocal { get; set; }
    [ObservableProperty] public partial string ClockZone { get; set; } = "";
    [ObservableProperty] public partial bool CopyJpgTwin { get; set; }
    [ObservableProperty] public partial string MapBase { get; set; } = "";
    [ObservableProperty] public partial string StreetsUrl { get; set; } = "";
    [ObservableProperty] public partial string StreetsDarkUrl { get; set; } = "";
    [ObservableProperty] public partial string SatelliteUrl { get; set; } = "";

    public IRelayCommand KeepOnDeviceCommand { get; }
    public IRelayCommand OpenLedgerCommand { get; }
    public IRelayCommand OpenBackupCommand { get; }
    public IAsyncRelayCommand CopyLedgerCommand { get; }
    public IAsyncRelayCommand StartEmptyCommand { get; }

    public IReadOnlyDictionary<string, string> SatellitePresets => Current.Map.SatellitePresets;

    public string ClockLearnedText => Current.DroneClockMode == StoredClockMode.SiteLocal
        ? "Last learned: follows local time at each site"
        : $"Last learned: {Current.DroneClockZone}";

    public string AboutText { get; } =
        "uas-sort · Maps: OpenFreeMap, © OpenStreetMap contributors · Imagery: Esri World Imagery, USGS · Places: GeoNames CC-BY 4.0 · MapLibre GL JS (BSD-3-Clause)";

    /// <summary>Ref §9.14 order: the new root is saved first, so the ledger's derived own-file path is the new one before [Copy] writes.</summary>
    public Task ChangeVideoRootAsync(string newRoot)
    {
        StopSave();
        Current = Current with { VideoRoot = newRoot.TrimEnd('\\'), RootsConfirmed = true };
        _store.Save(Current);
        _loading = true;
        VideoRoot = Current.VideoRoot;
        _loading = false;
        RefreshLedger();
        var status = _ledgerFor(Current.VideoRoot).Check();
        NoHistoryPrompt = VideoRootChange.NeedsHistoryPrompt(status, _currentLedger())
            ? $"No history found in {LedgerPaths.For(Current.VideoRoot)}; copy current ledger there?"
            : null;
        return Task.CompletedTask;
    }

    /// <summary>Ref §7.1: the old photo root joins PreviousPhotoRoots (Part 05 SettingsEdits.ChangePhotoRoot); saved 500 ms later.</summary>
    public void ChangePhotoRoot(string newRoot)
    {
        var next = SettingsEdits.ChangePhotoRoot(Current, newRoot);
        if (ReferenceEquals(next, Current)) return;
        Current = next;
        _loading = true;
        PhotoRoot = next.PhotoRoot;
        _loading = false;
        RebuildPrevious();
        RefreshLedger();
        ScheduleSave();
    }

    public void Dispose() => StopSave();

    partial void OnRadiusMilesChanged(double value) => Edit(s => s with { RadiusMiles = Math.Clamp(Math.Round(value), 5, 100) });
    partial void OnGapDaysChanged(int value) => Edit(s => s with { GapDays = Math.Clamp(value, 0, 7) });
    partial void OnIsSiteLocalChanged(bool value) => Edit(s => s with { DroneClockMode = value ? StoredClockMode.SiteLocal : StoredClockMode.Zone });
    partial void OnClockZoneChanged(string value) => Edit(s => s with { DroneClockZone = value });
    partial void OnCopyJpgTwinChanged(bool value) => Edit(s => s with { CopyJpgTwin = value });
    partial void OnMapBaseChanged(string value) => Edit(s => s with { Map = s.Map with { Base = value } });
    partial void OnStreetsUrlChanged(string value) => Edit(s => s with { Map = s.Map with { StreetsStyleUrl = value } });
    partial void OnStreetsDarkUrlChanged(string value) => Edit(s => s with { Map = s.Map with { StreetsDarkStyleUrl = value } });
    partial void OnSatelliteUrlChanged(string value) => Edit(s => s with { Map = s.Map with { SatelliteUrl = value } });

    private void Edit(Func<Settings, Settings> change)
    {
        if (_loading) return;
        Current = change(Current);
        ScheduleSave();
    }

    /// <summary>The debounce timer only posts to the UI thread (like ReviewVm's draft timer), so the save, <c>_saveTimer</c> and
    /// <c>Current</c> are only touched there and a save failure surfaces on the UI thread instead of ending the process.</summary>
    private void ScheduleSave()
    {
        StopSave();
        _saveTimer = _time.CreateTimer(_ => _ui.Post(SaveNow), null, SaveDelay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>A save posted before StopSave (the video-root save of Ref §9.14 already wrote Current, or the page closed) is dropped.</summary>
    private void SaveNow()
    {
        if (_saveTimer is null) return;
        StopSave();
        _store.Save(Current);
    }

    private void StopSave()
    {
        _saveTimer?.Dispose();
        _saveTimer = null;
    }

    private void RebuildPrevious()
    {
        PreviousPhotoRoots.Clear();
        foreach (var p in Current.PreviousPhotoRoots)
            PreviousPhotoRoots.Add(new PreviousRootVm(p, new RelayCommand(() => Forget(p))));
    }

    private void Forget(string path)
    {
        Current = Current with { PreviousPhotoRoots = Current.PreviousPhotoRoots.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) };
        RebuildPrevious();
        ScheduleSave();
    }

    private void RefreshLedger()
    {
        LedgerFolder = LedgerPaths.For(Current.VideoRoot);
        BackupFolder = _backupDirFor(Current.VideoRoot);
        var (text, severity, keep) = LedgerStatusText.For(_ledgerFor(Current.VideoRoot).Check());
        LedgerStatus = text;
        LedgerSeverity = severity;
        CanKeepOnDevice = keep;
        VideoFreeText = _space.FreeBytes(Current.VideoRoot) is { } v ? $"{Fmt.Size(v)} free" : "not available";
        PhotoFreeText = _space.FreeBytes(Current.PhotoRoot) is { } p ? $"{Fmt.Size(p)} free" : "not available";
        OnPropertyChanged(nameof(ClockLearnedText));
    }

    private Task CopyLedgerAsync()
    {
        _ledgerFor(Current.VideoRoot).CopyInto(Current.VideoRoot, _currentLedger());
        NoHistoryPrompt = null;
        RefreshLedger();
        return Task.CompletedTask;
    }

    private async Task StartEmptyAsync()
    {
        var listing = _lister.Enumerate(Current.VideoRoot, true, new HashSet<string>([LedgerPaths.FolderName], StringComparer.OrdinalIgnoreCase));
        var copied = VideoRootChange.AppCopiedVideos(listing, Current.VideoRoot, _currentLedger());
        if (copied > 0)
        {
            var lead = copied == 1 ? "1 video here was copied by uas-sort" : string.Create(CultureInfo.InvariantCulture, $"{copied} videos here were copied by uas-sort");
            var answer = await _dialogs.ShowAsync(new DialogRequest("Start without history?",
                lead + "; starting empty treats them as manual imports and may mark un-copied photos as probably imported. [Copy] is recommended.",
                "Start empty", "Copy", "Cancel")).ConfigureAwait(true);
            if (answer == DialogResult.Secondary) { await CopyLedgerAsync().ConfigureAwait(true); return; }
            if (answer != DialogResult.Primary) return;
        }
        NoHistoryPrompt = null;
    }
}
