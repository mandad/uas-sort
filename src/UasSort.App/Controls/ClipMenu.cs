// src/UasSort.App/Controls/ClipMenu.cs — Ref §9.5 context menu (also used for the map's contextMenu message, Ref §9.6)
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace UasSort.App.Controls;

public static class ClipMenu
{
    public static MenuFlyout Build(ReviewVm review, ClipRowVm? row)
    {
        var menu = new MenuFlyout();
        var toNew = new MenuFlyoutItem { Text = "Move selected to new group" };
        toNew.Click += async (_, _) => await review.MoveSelectedToNewGroupAsync();
        menu.Items.Add(toNew);
        var moveTo = new MenuFlyoutSubItem { Text = "Move to group ▸" };
        foreach (var g in review.MoveTargets())                 // every other card that is not AlreadyImported
        {
            var item = new MenuFlyoutItem { Text = string.IsNullOrEmpty(g.Description) ? g.DateRangeText : g.DateRangeText + " · " + g.Description };
            item.Click += async (_, _) => await review.MoveSelectedToGroupAsync(g);
            moveTo.Items.Add(item);
        }
        moveTo.IsEnabled = moveTo.Items.Count > 0;
        menu.Items.Add(moveTo);
        menu.Items.Add(new MenuFlyoutSeparator());
        var include = new MenuFlyoutItem { Text = "Include" };
        include.Click += async (_, _) => await review.SetIncludedAsync(review.Videos.SelectedClipIds, true);
        var exclude = new MenuFlyoutItem { Text = "Exclude" };
        exclude.Click += async (_, _) => await review.SetIncludedAsync(review.Videos.SelectedClipIds, false);
        menu.Items.Add(include);
        menu.Items.Add(exclude);
        if (row is not null)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            var play = new MenuFlyoutItem { Text = "Open in default player" };
            play.Click += (_, _) => review.OpenClip(row.Id);
            var copy = new MenuFlyoutItem { Text = "Copy card path" };
            copy.Click += (_, _) =>
            {
                var package = new DataPackage();
                package.SetText(CardPath(review, row));
                Clipboard.SetContent(package);
            };
            menu.Items.Add(play);
            menu.Items.Add(copy);
        }
        return menu;
    }

    /// <summary>The clip's full path on the card (registry Part 11 item 16).</summary>
    public static string CardPath(ReviewVm review, ClipRowVm row) =>
        PathRules.Join(review.Plan.Base.Scan.Inventory.Source.Root, row.Id.CardRelPath.Replace('/', '\\'));
}
