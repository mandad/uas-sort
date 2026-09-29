// src/UasSort.App/Controls/TuningStrip.xaml.cs — Ref §9.7 / §2.7 #10: Slider has no drag-completed event
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

namespace UasSort.App.Controls;

public sealed partial class TuningStrip : UserControl
{
    private ReviewVm? _review;
    private bool _dragging;

    public TuningStrip()
    {
        InitializeComponent();
        foreach (var slider in new[] { RadiusSlider, GapSlider })
        {
            // The Slider's thumb marks the pointer events handled, so handledEventsToo is required (Ref §9.7).
            slider.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => OnDragStarted()), handledEventsToo: true);
            slider.AddHandler(PointerReleasedEvent, new PointerEventHandler(async (_, _) => await OnDragEndedAsync()), handledEventsToo: true);
            slider.PointerCaptureLost += async (_, _) => await OnDragEndedAsync();
        }
        // Keyboard and wheel changes have no release: TuningVm commits them after 400 ms (IdleCommit) without a change.
    }

    public ReviewVm? Review { get => _review; set { _review = value; Bindings.Update(); } }

    public void OnDragStarted()
    {
        if (_dragging || _review is null) return;
        _dragging = true;
        _review.Tuning.BeginDrag();
    }

    public async Task OnDragEndedAsync()
    {
        if (!_dragging || _review is null) return;
        _dragging = false;
        await _review.Tuning.EndDragAsync();                  // commits ONE undo entry
    }

    private void OnGapChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_review is not null) _review.Tuning.GapDays = (int)Math.Round(e.NewValue);
    }

    private void OnBaseChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_review is not null && BaseToggle.SelectedIndex >= 0) _review.MapBase = UiFormat.BaseAt(BaseToggle.SelectedIndex);
    }
}
