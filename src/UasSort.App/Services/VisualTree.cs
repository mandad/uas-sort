// src/UasSort.App/Services/VisualTree.cs
using Microsoft.UI.Xaml;
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

    public static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
    {
        for (var d = start; d is not null; d = VisualTreeHelper.GetParent(d))
            if (d is T t) return t;
        return null;
    }
}
