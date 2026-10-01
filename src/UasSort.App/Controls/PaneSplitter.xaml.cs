// src/UasSort.App/Controls/PaneSplitter.xaml.cs
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace UasSort.App.Controls;

/// <summary>A splitter that sits in an Auto row or column of its parent Grid and resizes the nearest non-Auto track on each
/// side (Ref §9.3 panes). Native AOT safe (Task U1): it reads and writes only the typed ColumnDefinition.Width / RowDefinition.Height,
/// ActualWidth/ActualHeight and Min/Max properties — never GetValue plus an unbox of a projected GridLength, which is what made
/// the toolkit GridSplitter throw InvalidCastException on every drag. The arithmetic is UasSort.Review.SplitterMath.
/// Pointer drag (captured), and Left/Right (columns) or Up/Down (rows) nudge by <see cref="KeyStep"/> when focused.</summary>
public sealed partial class PaneSplitter : UserControl
{
    public const double KeyStep = 8;

    private bool _resizesRows;
    private Drag? _drag;
    private double _pointerStart;

    public PaneSplitter()
    {
        InitializeComponent();
        ApplyDirection();
    }

    /// <summary>False (default): resizes columns, a vertical bar dragged left/right. True: resizes rows, dragged up/down.
    /// A plain bool so the XAML literal never needs a projected enum or struct.</summary>
    public bool ResizesRows
    {
        get => _resizesRows;
        set { _resizesRows = value; ApplyDirection(); }
    }

    internal bool IsDragging => _drag is not null;

    /// <summary>Snapshots the two tracks the splitter resizes; false when there is no parent Grid or no resizable track on a side.</summary>
    internal bool BeginDrag()
    {
        _drag = Snapshot();
        return _drag is not null;
    }

    /// <summary>The one resize path (pointer moves, keys, the selftest): moves the boundary <paramref name="offset"/> pixels from
    /// where it was at BeginDrag, within both tracks' limits, and returns the pixels really moved.</summary>
    internal double DragTo(double offset)
    {
        if (_drag is not { } d) return 0;
        var r = SplitterMath.Apply(d.First, d.Second, offset);
        Set(d.FirstIndex, r.First, d.First.Unit);
        Set(d.SecondIndex, r.Second, d.Second.Unit);
        return r.Applied;
    }

    internal void EndDrag() => _drag = null;

    /// <summary>A whole drag by <paramref name="delta"/> pixels from the current layout (keyboard nudge, selftest).</summary>
    internal double ApplyDrag(double delta)
    {
        if (!BeginDrag()) return 0;
        try { return DragTo(delta); }
        finally { EndDrag(); }
    }

    /// <summary>The pixels an arrow key moves the boundary (0: not this splitter's key).</summary>
    internal static double NudgeFor(VirtualKey key, bool resizesRows) => (resizesRows, key) switch
    {
        (false, VirtualKey.Left) or (true, VirtualKey.Up) => -KeyStep,
        (false, VirtualKey.Right) or (true, VirtualKey.Down) => KeyStep,
        _ => 0,
    };

    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || !BeginDrag()) return;
        _pointerStart = Offset(e);
        CapturePointer(e.Pointer);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerRoutedEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag is null) return;
        DragTo(Offset(e) - _pointerStart);                       // from the press point: the boundary stays under the pointer
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag is null) return;
        EndDrag();
        ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerRoutedEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        EndDrag();
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        double step = NudgeFor(e.Key, _resizesRows);
        if (step == 0)
        {
            base.OnKeyDown(e);
            return;
        }
        ApplyDrag(step);
        e.Handled = true;
    }

    private double Offset(PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(_drag?.Grid).Position;          // the parent Grid's coordinates: they don't move with the splitter
        return _resizesRows ? p.Y : p.X;
    }

    private void ApplyDirection()
    {
        ProtectedCursor = InputSystemCursor.Create(_resizesRows ? InputSystemCursorShape.SizeNorthSouth : InputSystemCursorShape.SizeWestEast);
        Grip.Width = _resizesRows ? 24 : 4;
        Grip.Height = _resizesRows ? 4 : 24;
    }

    private Drag? Snapshot()
    {
        if (VisualTreeHelper.GetParent(this) is not Grid grid) return null;
        int index = _resizesRows ? Grid.GetRow(this) : Grid.GetColumn(this);
        int count = _resizesRows ? grid.RowDefinitions.Count : grid.ColumnDefinitions.Count;
        int first = -1, second = -1;
        for (int i = Math.Min(index, count) - 1; i >= 0 && first < 0; i--)
            if (!IsAuto(grid, i)) first = i;
        for (int i = index + 1; i < count && second < 0; i++)
            if (!IsAuto(grid, i)) second = i;
        if (first < 0 || second < 0) return null;
        return new Drag(grid, first, Track(grid, first), second, Track(grid, second));
    }

    private bool IsAuto(Grid grid, int i) =>
        (_resizesRows ? grid.RowDefinitions[i].Height : grid.ColumnDefinitions[i].Width).GridUnitType == GridUnitType.Auto;

    private PaneTrack Track(Grid grid, int i)
    {
        if (_resizesRows)
        {
            var row = grid.RowDefinitions[i];
            var height = row.Height;
            return new PaneTrack(height.Value, UnitOf(height), row.ActualHeight, row.MinHeight, row.MaxHeight);
        }
        var column = grid.ColumnDefinitions[i];
        var width = column.Width;
        return new PaneTrack(width.Value, UnitOf(width), column.ActualWidth, column.MinWidth, column.MaxWidth);
    }

    private static PaneUnit UnitOf(GridLength length) => length.GridUnitType == GridUnitType.Star ? PaneUnit.Star : PaneUnit.Pixel;

    private void Set(int i, double value, PaneUnit unit)
    {
        if (_drag is not { } d) return;
        var length = new GridLength(value, unit == PaneUnit.Star ? GridUnitType.Star : GridUnitType.Pixel);
        if (_resizesRows) d.Grid.RowDefinitions[i].Height = length;
        else d.Grid.ColumnDefinitions[i].Width = length;
    }

    private sealed record Drag(Grid Grid, int FirstIndex, PaneTrack First, int SecondIndex, PaneTrack Second);
}
