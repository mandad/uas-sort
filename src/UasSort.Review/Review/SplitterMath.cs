// src/UasSort.Review/Review/SplitterMath.cs — the Review page's pane splitters (Ref §9.3 panes; Task U1), WinUI-free so it is unit tested
namespace UasSort.Review;

/// <summary>How a Grid row or column is sized: fixed pixels or a share of the star space (Auto tracks are never resized).</summary>
public enum PaneUnit { Pixel, Star }

/// <summary>One side of a splitter: the definition's Width/Height (Value in Unit), its laid-out size, and its MinWidth/MaxWidth.</summary>
public readonly record struct PaneTrack(double Value, PaneUnit Unit, double Actual, double Min, double Max);

/// <summary>The new Width/Height values (each in its own track's unit) and the pixels the splitter really moved.</summary>
public readonly record struct PaneResize(double First, double Second, double Applied);

public static class SplitterMath
{
    /// <summary>Moves the boundary between two adjacent tracks by <paramref name="delta"/> pixels (positive grows the first),
    /// from their laid-out sizes, so that both stay within their Min/Max. The space the two share is kept: a pixel track gets its
    /// new size; when both are star tracks their star sum is kept and split by the new sizes; a star track next to a pixel
    /// track keeps its value and takes the rest. Nothing changes for a zero or non-finite delta, before layout, or when the
    /// limits cannot all be met.</summary>
    public static PaneResize Apply(PaneTrack first, PaneTrack second, double delta)
    {
        var unchanged = new PaneResize(first.Value, second.Value, 0);
        double total = first.Actual + second.Actual;
        if (delta == 0 || !double.IsFinite(delta) || !(total > 0)) return unchanged;
        double lo = Math.Max(first.Min, total - second.Max);
        double hi = Math.Min(first.Max, total - second.Min);
        if (lo > hi) return unchanged;
        double newFirst = Math.Clamp(first.Actual + delta, lo, hi);
        double applied = newFirst - first.Actual;
        if (applied == 0) return unchanged;
        double newSecond = total - newFirst;
        if (first.Unit == PaneUnit.Star && second.Unit == PaneUnit.Star)
        {
            double stars = first.Value + second.Value;
            return new PaneResize(stars * newFirst / total, stars * newSecond / total, applied);
        }
        return new PaneResize(first.Unit == PaneUnit.Pixel ? newFirst : first.Value,
                              second.Unit == PaneUnit.Pixel ? newSecond : second.Value, applied);
    }
}
