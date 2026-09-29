// src/UasSort.Review/Settings/SetupVm.cs
namespace UasSort.Review;

/// <summary>First run and settings recovery: confirm the video and photo roots; the ledger is a status only (Ref §9.1 Setup).</summary>
public sealed partial class SetupVm : ObservableObject
{
    private readonly SettingsLoad _load;
    private readonly ISettingsStore _store;
    private readonly Func<string, ILedgerStore> _ledgerFor;
    private readonly IFreeSpace _space;

    public SetupVm(SettingsLoad load, ISettingsStore store, Func<string, ILedgerStore> ledgerFor, IFreeSpace space)
    {
        _load = load;
        _store = store;
        _ledgerFor = ledgerFor;
        _space = space;
        ConfirmCommand = new RelayCommand(Confirm, () => CanConfirm);
        KeepOnDeviceCommand = new RelayCommand(KeepOnDevice, () => CanKeepOnDevice);
        if (load.Recovered && load.RootsFromLastRun is { } last)
        {
            RecoveryText = "Settings couldn't be read, so the folders of the last offload on this PC are shown. Confirm them to continue.";
            VideoRoot = last.Video;
            PhotoRoot = last.Photo;
        }
        else
        {
            RecoveryText = load.Recovered ? "Settings couldn't be read. Confirm the folders to continue." : null;
            VideoRoot = load.Settings.VideoRoot;
            PhotoRoot = load.Settings.PhotoRoot;
        }
        Refresh();
    }

    [ObservableProperty] public partial string VideoRoot { get; private set; } = "";
    [ObservableProperty] public partial string PhotoRoot { get; private set; } = "";
    [ObservableProperty] public partial string VideoFreeText { get; private set; } = "";
    [ObservableProperty] public partial string PhotoFreeText { get; private set; } = "";
    [ObservableProperty] public partial string LedgerStatus { get; private set; } = "";
    [ObservableProperty] public partial InfoSeverity LedgerSeverity { get; private set; }
    [ObservableProperty] public partial bool CanKeepOnDevice { get; private set; }
    [ObservableProperty] public partial bool CanConfirm { get; private set; }
    [ObservableProperty] public partial string? RecoveryText { get; private set; }
    [ObservableProperty] public partial bool PhotoInsideVideoNote { get; private set; }

    public IRelayCommand ConfirmCommand { get; }
    public IRelayCommand KeepOnDeviceCommand { get; }
    public event Action<Settings>? Confirmed;

    public void SetVideoRoot(string path)
    {
        var followed = string.Equals(PhotoRoot, VideoRoot.TrimEnd('\\') + @"\Picture Offload", StringComparison.OrdinalIgnoreCase);
        VideoRoot = path.TrimEnd('\\');
        if (followed) PhotoRoot = VideoRoot + @"\Picture Offload";
        Refresh();
    }

    public void SetPhotoRoot(string path)
    {
        PhotoRoot = path.TrimEnd('\\');
        Refresh();
    }

    private void Refresh()
    {
        VideoFreeText = Free(VideoRoot);
        PhotoFreeText = Free(PhotoRoot);
        PhotoInsideVideoNote = PhotoRoot.StartsWith(VideoRoot.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
        var (text, severity, keep) = LedgerStatusText.For(_ledgerFor(VideoRoot).Check());
        LedgerStatus = text;
        LedgerSeverity = severity;
        CanKeepOnDevice = keep;
        CanConfirm = VideoRoot.Length > 0 && PhotoRoot.Length > 0 && severity != InfoSeverity.Error;
        ConfirmCommand.NotifyCanExecuteChanged();
        KeepOnDeviceCommand.NotifyCanExecuteChanged();
    }

    private string Free(string root) => _space.FreeBytes(root) is { } f ? $"{Fmt.Size(f)} free" : "not available";

    private void KeepOnDevice()
    {
        _ledgerFor(VideoRoot).KeepOnDevice();
        Refresh();
    }

    private void Confirm()
    {
        var s = _load.Settings with { VideoRoot = VideoRoot, PhotoRoot = PhotoRoot, RootsConfirmed = true };
        _store.Save(s);
        Confirmed?.Invoke(s);
    }
}
