// src/UasSort.App/Pages/CleanupPage.xaml.cs
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Globalization.NumberFormatting;

namespace UasSort.App.Pages;

public sealed partial class CleanupPage : Page
{
    public CleanupPage()
    {
        InitializeComponent();
        // Decimal GB with one decimal (Ref §10.6 mode 2).
        GigabytesBox.NumberFormatter = new DecimalFormatter
        {
            FractionDigits = 1,
            IntegerDigits = 1,
            NumberRounder = new IncrementNumberRounder { Increment = 0.1, RoundingAlgorithm = RoundingAlgorithm.RoundHalfUp },
        };
    }

    public CleanupVm Vm { get; private set; } = null!;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (Vm is not null) Vm.PropertyChanged -= OnVmChanged;
        Vm = (CleanupVm)((StageArgs)e.Parameter).Vm;             // ShellVm.Current (= ShellVm.Cleanup) on the Cleanup stage
        Vm.PropertyChanged += OnVmChanged;
        ReviewList.ItemsSource = Vm.Rows;
        Bindings.Update();
        ShowMode(Vm.Mode);
        ShowStep(Vm.Step);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) => Vm.PropertyChanged -= OnVmChanged;

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(CleanupVm.Step): ShowStep(Vm.Step); break;
            case nameof(CleanupVm.Mode): ShowMode(Vm.Mode); break;
            case nameof(CleanupVm.FirstUndecided): ScrollToFirstUndecided(); break;
        }
    }

    private void OnModeChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        int index = sender.Items.IndexOf(sender.SelectedItem);
        if (index < 0 || Vm is null) return;
        var mode = index == 1 ? CleanupMode.FreeSpace : CleanupMode.BeforeDate;
        if (Vm.Mode != mode) Vm.Mode = mode;
        ShowMode(mode);
    }

    private void ShowMode(CleanupMode mode)
    {
        int index = UiFormat.ModeIndex(mode);
        if (ModeBar.SelectedItem != ModeBar.Items[index]) ModeBar.SelectedItem = ModeBar.Items[index];
        BeforeDatePanel.Visibility = mode == CleanupMode.BeforeDate ? Visibility.Visible : Visibility.Collapsed;
        FreeSpacePanel.Visibility = mode == CleanupMode.FreeSpace ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnDateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) => Vm?.PickDate(args.NewDate);

    private void OnFreeKindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Vm is not null && FreeKindButtons.SelectedIndex >= 0)
            Vm.FreeKind = FreeKindButtons.SelectedIndex == 1 ? FreeSpaceKind.FreeUp : FreeSpaceKind.HaveFree;
    }

    /// <summary>Ref §10.6: Choose → Review (not in library) → Confirm → Deleting → Result; the footer buttons follow the step.</summary>
    private void ShowStep(CleanupStep step)
    {
        static Visibility On(bool b) => b ? Visibility.Visible : Visibility.Collapsed;
        ChoosePanel.Visibility = On(step == CleanupStep.Choose);
        ReviewPanel.Visibility = On(step == CleanupStep.Review);
        ConfirmPanel.Visibility = On(step == CleanupStep.Confirm);
        DeletingPanel.Visibility = On(step == CleanupStep.Deleting);
        ResultPanel.Visibility = On(step == CleanupStep.Result);
        BackButton.Visibility = On(step is CleanupStep.Choose or CleanupStep.Review or CleanupStep.Confirm);
        ContinueButton.Visibility = On(step is CleanupStep.Choose or CleanupStep.Review);
        DeleteButton.Visibility = On(step == CleanupStep.Confirm);
        StopButton.Visibility = On(step == CleanupStep.Deleting);
        DoneButton.Visibility = On(step == CleanupStep.Result);
        if (step == CleanupStep.Result) EjectResult.Text = "";
        Bindings.Update();                                       // Result is created when the run ends
        if (step == CleanupStep.Review) ScrollToFirstUndecided();
    }

    // Ref §10.6: rows that join later arrive undecided and the list scrolls to the first undecided one.
    private void ScrollToFirstUndecided()
    {
        if (Vm.FirstUndecided is not { } row) return;
        int i = Vm.Rows.IndexOf(row);
        if (i >= 0) ReviewList.StartBringItemIntoView(i, new BringIntoViewOptions { VerticalAlignmentRatio = 0.1 });
    }

    private void OnEject(object sender, RoutedEventArgs e)
    {
        if (Vm.Result?.Eject is not { } eject) return;
        eject.EjectCommand.Execute(null);
        EjectResult.Text = eject.ResultText ?? "";
    }
}
