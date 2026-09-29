// src/UasSort.App/Services/VisualTree.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace UasSort.App.Services;

public static class VisualTree
{
    public static T? FindDescendant<T>(DependencyObject root, Func<T, bool>? match = null) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t && (match is null || match(t))) return t;
            if (FindDescendant(child, match) is { } found) return found;
        }
        return null;
    }

    public static List<T> FindAll<T>(DependencyObject root, Func<T, bool>? match = null) where T : DependencyObject
    {
        var list = new List<T>();
        void Walk(DependencyObject d)
        {
            int n = VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < n; i++)
            {
                var c = VisualTreeHelper.GetChild(d, i);
                if (c is T t && (match is null || match(t))) list.Add(t);
                Walk(c);
            }
        }
        Walk(root);
        return list;
    }

    /// <summary>The realised ItemContainer that shows this item, or null. A recycled container stays a child of the ItemsView's
    /// ItemsRepeater (arranged off-screen) and keeps its last DataContext, so only elements with an index count.</summary>
    public static ItemContainer? RealizedContainer(ItemsView view, object item)
    {
        if (FindDescendant<ItemsRepeater>(view) is not { } repeater) return null;
        int n = VisualTreeHelper.GetChildrenCount(repeater);
        for (int i = 0; i < n; i++)
            if (VisualTreeHelper.GetChild(repeater, i) is ItemContainer c && ReferenceEquals(c.DataContext, item) && repeater.GetElementIndex(c) >= 0)
                return c;
        return null;
    }

    public static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
    {
        for (var d = start; d is not null; d = VisualTreeHelper.GetParent(d))
            if (d is T t) return t;
        return null;
    }
}
