// src/UasSort.App/Controls/TimelineTemplateSelector.cs — two selectable item kinds (Ref §9.3)
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace UasSort.App.Controls;

public sealed partial class TimelineTemplateSelector : DataTemplateSelector
{
    public DataTemplate? GroupCard { get; set; }
    public DataTemplate? FoldedRun { get; set; }

    protected override DataTemplate SelectTemplateCore(object item) =>
        (item is FoldedRunVm ? FoldedRun : GroupCard) ?? throw new InvalidOperationException("timeline templates not set");

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}
