// src/UasSort.App/Controls/FlowPanel.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace UasSort.App.Controls;

/// <summary>Lays its children out left to right at their desired sizes and starts a new line when the next one would not fit
/// the available width (Task U2: the Verdict page's per-day buttons, which overflowed a single horizontal row). Native AOT safe:
/// typed Measure/Arrange/DesiredSize only, no dependency properties and nothing set from XAML. Collapsed children take no room.</summary>
public sealed partial class FlowPanel : Panel
{
    public const double Spacing = 6;

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        return Flow(availableSize.Width, arrange: false);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Flow(finalSize.Width, arrange: true);
        return finalSize;
    }

    /// <summary>The one line-breaking pass for measure and arrange: returns the size the lines take.</summary>
    private Size Flow(double width, bool arrange)
    {
        double x = 0, y = 0, line = 0, used = 0;
        foreach (var child in Children)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            var d = child.DesiredSize;
            if (x > 0 && x + d.Width > width)
            {
                y += line + Spacing;
                x = 0;
                line = 0;
            }
            if (arrange) child.Arrange(new Rect(x, y, d.Width, d.Height));
            used = Math.Max(used, x + d.Width);
            line = Math.Max(line, d.Height);
            x += d.Width + Spacing;
        }
        return new Size(used, y + line);
    }
}
