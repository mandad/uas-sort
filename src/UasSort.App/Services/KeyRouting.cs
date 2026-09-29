// src/UasSort.App/Services/KeyRouting.cs — Ref §9.12: page keys use modifiers; list keys act only on items, never in a TextBox
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.System;
using Windows.UI.Core;

namespace UasSort.App.Services;

public static class KeyRouting
{
    public static bool IsTextInput(object? focused) =>
        focused is TextBox or PasswordBox or RichEditBox or AutoSuggestBox or NumberBox
        || (focused is DependencyObject d && (VisualTree.FindAncestor<AutoSuggestBox>(d) is not null
                                              || VisualTree.FindAncestor<TextBox>(d) is not null
                                              || VisualTree.FindAncestor<NumberBox>(d) is not null));

    /// <summary>A focused CheckBox or any other button (Split before, Split here, a tile's Undo) keeps Space for itself: the
    /// list keys act only when the item container itself has focus (Ref §9.12). CheckBox is a ToggleButton, hence a ButtonBase.</summary>
    public static bool IsCheckBox(object? focused) => focused is ButtonBase;

    public static bool IsInItemContainer(object? focused) =>
        focused is DependencyObject d && !IsTextInput(focused) && VisualTree.FindAncestor<ItemContainer>(d) is not null;

    public static KeyMods Modifiers()
    {
        static bool Down(VirtualKey k) => InputKeyboardSource.GetKeyStateForCurrentThread(k).HasFlag(CoreVirtualKeyStates.Down);
        return (Down(VirtualKey.Control) ? KeyMods.Ctrl : KeyMods.None)
             | (Down(VirtualKey.Shift) ? KeyMods.Shift : KeyMods.None)
             | (Down(VirtualKey.Menu) ? KeyMods.Alt : KeyMods.None);
    }

    public static KeyMods ToMods(VirtualKeyModifiers m) =>
        (m.HasFlag(VirtualKeyModifiers.Control) ? KeyMods.Ctrl : KeyMods.None)
        | (m.HasFlag(VirtualKeyModifiers.Shift) ? KeyMods.Shift : KeyMods.None)
        | (m.HasFlag(VirtualKeyModifiers.Menu) ? KeyMods.Alt : KeyMods.None);

    /// <summary>The keys ReviewVm.HandleKey knows (Part 10's ReviewKey); any other key is not the Review's.</summary>
    public static ReviewKey? ToReviewKey(VirtualKey key) => key switch
    {
        VirtualKey.Z => ReviewKey.Z,
        VirtualKey.Y => ReviewKey.Y,
        VirtualKey.M => ReviewKey.M,
        VirtualKey.N => ReviewKey.N,
        VirtualKey.S => ReviewKey.S,
        VirtualKey.Number1 => ReviewKey.D1,
        VirtualKey.Number2 => ReviewKey.D2,
        VirtualKey.Number3 => ReviewKey.D3,
        VirtualKey.F2 => ReviewKey.F2,
        VirtualKey.F5 => ReviewKey.F5,
        VirtualKey.Enter => ReviewKey.Enter,
        VirtualKey.Space => ReviewKey.Space,
        _ => null,
    };

    /// <summary>Where the key was pressed: any text input is TextBox (keys stay the box's own), else the kind of focused item.</summary>
    public static KeyFocus FocusOf(object? focused)
    {
        if (IsTextInput(focused)) return KeyFocus.TextBox;
        return VisualTree.FindAncestor<ItemContainer>(focused as DependencyObject)?.DataContext switch
        {
            TimelineEntryVm => KeyFocus.TimelineItem,
            ClipRowVm => KeyFocus.ClipItem,
            PhotoTileVm => KeyFocus.PhotoItem,
            _ => KeyFocus.Other,
        };
    }
}
