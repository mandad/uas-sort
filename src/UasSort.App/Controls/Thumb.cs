using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace UasSort.App.Controls;

/// <summary>Ref §9.5: <c>&lt;Image ctl:Thumb.Key="{x:Bind ThumbKey}"/&gt;</c>. The DP stores the key's string (a WinRT-friendly
/// value, safe under AOT); the load assigns Source only if the element still carries the same key when it finishes, so a
/// recycled container never shows another item's thumbnail.</summary>
public static class Thumb
{
    public static ThumbnailCache? Cache { get; set; }

    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key", typeof(string), typeof(Thumb), new PropertyMetadata(null, OnKeyChanged));

    public static ItemId GetKey(Image element) => new((string?)element.GetValue(KeyProperty) ?? "");

    public static void SetKey(Image element, ItemId value) => element.SetValue(KeyProperty, value.CardRelPath);

    private static async void OnKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Image image) return;
        image.Source = null;                                   // placeholder (the template's Border background) until loaded
        if (e.NewValue is not string rel || rel.Length == 0 || Cache is null) return;
        var requested = new ItemId(rel);
        var source = await Cache.GetAsync(requested);
        if ((string?)image.GetValue(KeyProperty) == rel)       // key re-check: the container may have been recycled meanwhile
            image.Source = source;
    }
}
