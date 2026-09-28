using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace UasSort.App.Pages;

/// <summary>ItemsView template selector (Ref §2.1 "ItemsView … takes a template selector").</summary>
public sealed partial class ProbeTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Video { get; set; }

    public DataTemplate? Photo { get; set; }

    protected override DataTemplate SelectTemplateCore(object item) => item switch
    {
        ProbeVideoVm => Video ?? throw new InvalidOperationException("Video template not set"),
        ProbePhotoVm => Photo ?? throw new InvalidOperationException("Photo template not set"),
        _ => throw new ArgumentException("Unexpected probe item " + item?.GetType().Name, nameof(item)),
    };

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}
