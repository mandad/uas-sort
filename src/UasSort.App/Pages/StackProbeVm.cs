using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace UasSort.App.Pages;

/// <summary>Probe-page view model: partial properties (MVVM Toolkit 8.4.2) and an ObservableCollection of VM classes.</summary>
public sealed partial class StackProbeVm : ObservableObject
{
    [ObservableProperty]
    public partial string Status { get; set; } = "Starting";

    [ObservableProperty]
    public partial string FolderText { get; set; } = "No folder chosen";

    public ObservableCollection<ProbeItemVm> Items { get; } =
        [new ProbeVideoVm("Anvil Mountain"), new ProbePhotoVm("Zachar Bay")];
}

/// <summary>Every VM shown as text overrides ToString with its display text (Ref §2.7 #4).</summary>
public abstract partial class ProbeItemVm
{
    public abstract string Text { get; }

    public override string ToString() => Text;
}

public sealed partial class ProbeVideoVm(string label) : ProbeItemVm
{
    public string Label { get; } = label;

    public override string Text => Label;
}

public sealed partial class ProbePhotoVm(string caption) : ProbeItemVm
{
    public string Caption { get; } = caption;

    public override string Text => Caption;
}
