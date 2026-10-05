// src/UasSort.App/Pages/PhotoCleanupPage.xaml.cs
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;

namespace UasSort.App.Pages;

public sealed partial class PhotoCleanupPage : Page
{
    public PhotoCleanupPage() => InitializeComponent();

    public PhotoCleanupVm Vm { get; private set; } = null!;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (Vm is not null) Vm.PropertyChanged -= OnVmChanged;
        Vm = (PhotoCleanupVm)((StageArgs)e.Parameter).Vm;          // ShellVm.Current (= ShellVm.PhotoCleanup) on the PhotoCleanup stage
        Vm.PropertyChanged += OnVmChanged;
        RowList.ItemsSource = Vm.Rows;
        Bindings.Update();
        ShowStep(Vm.Step);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) => Vm.PropertyChanged -= OnVmChanged;

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PhotoCleanupVm.Step)) ShowStep(Vm.Step);
    }

    private void OnDateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) => Vm?.PickDate(args.NewDate);

    /// <summary>Same re-sync as CleanupPage.OnDecisionClick: a ToggleButton un-toggles itself on click before its Command runs.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "XAML event handler: the generated Connect code wires it as this.OnDecisionClick, so it must be an instance method.")]
    private void OnDecisionClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton b) ResyncDecision(b);
    }

    internal static void ResyncDecision(ToggleButton b)
    {
        if (b.DataContext is PhotoCleanupRowVm row)
            b.IsChecked = string.Equals(b.Tag as string, "Keep", StringComparison.Ordinal) ? row.Decision == RowDecision.Keep : row.Decision == RowDecision.Delete;
    }

    private void ShowStep(PhotoCleanupStep step)
    {
        static Visibility On(bool b) => b ? Visibility.Visible : Visibility.Collapsed;
        ChoosePanel.Visibility = On(step == PhotoCleanupStep.Choose);
        ReviewPanel.Visibility = On(step == PhotoCleanupStep.Review);
        ConfirmPanel.Visibility = On(step == PhotoCleanupStep.Confirm);
        RunningPanel.Visibility = On(step == PhotoCleanupStep.Running);
        ResultPanel.Visibility = On(step == PhotoCleanupStep.Result);
        BackButton.Visibility = On(step is PhotoCleanupStep.Choose or PhotoCleanupStep.Review or PhotoCleanupStep.Confirm);
        NextButton.Visibility = On(step is PhotoCleanupStep.Choose or PhotoCleanupStep.Review);
        RunButton.Visibility = On(step == PhotoCleanupStep.Confirm);
        StopButton.Visibility = On(step == PhotoCleanupStep.Running);
        DoneButton.Visibility = On(step == PhotoCleanupStep.Result);
        Bindings.Update();                                           // Result is created when the run ends
    }
}
