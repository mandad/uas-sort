// src/UasSort.Review/Review/ReviewVm.Keys.cs
namespace UasSort.Review;

public enum ReviewKey { Z, Y, M, N, S, D1, D2, D3, F2, F5, Enter, Space }

[Flags]
public enum KeyMods { None = 0, Ctrl = 1, Shift = 2, Alt = 4 }

public enum KeyFocus { Other, TextBox, TimelineItem, ClipItem, PhotoItem }

public sealed partial class ReviewVm
{
    public ClipRowVm? FocusedClip { get; set; }
    public PhotoTileVm? FocusedTile { get; set; }

    /// <summary>Ref §9.12. Page keys use modifiers; list keys act only on item containers, never in a TextBox. Every async action is
    /// observed: a fault shows the session-fault InfoBar instead of vanishing.</summary>
    public bool HandleKey(ReviewKey key, KeyMods mods, KeyFocus focus)
    {
        var ctrl = mods.HasFlag(KeyMods.Ctrl);
        var shift = mods.HasFlag(KeyMods.Shift);
        if (focus == KeyFocus.TextBox && key is ReviewKey.Z or ReviewKey.Y or ReviewKey.Space or ReviewKey.S or ReviewKey.M or ReviewKey.N)
            return false;

        switch (key)
        {
            case ReviewKey.Z when ctrl && !shift:
                Observe(UndoCommand.ExecuteAsync(null));
                return true;
            case ReviewKey.Z when ctrl && shift:
            case ReviewKey.Y when ctrl:
                Observe(RedoCommand.ExecuteAsync(null));
                return true;
            case ReviewKey.M when ctrl && !shift:
                Observe(MergeSelectedWithNextAsync());
                return true;
            case ReviewKey.N when ctrl && shift:
                Observe(MoveSelectedToNewGroupAsync());
                return true;
            case ReviewKey.D1 when ctrl:
                SelectedTab = 0;
                return true;
            case ReviewKey.D2 when ctrl:
                SelectedTab = 1;
                return true;
            case ReviewKey.D3 when ctrl:
                SelectedTab = 2;
                return true;
            case ReviewKey.F5 when !IsReadOnly:
                RescanRequested?.Invoke();
                return true;
            case ReviewKey.Enter when ctrl:
                if (OffloadCommand.CanExecute(null)) OffloadCommand.Execute(null);
                return true;
            case ReviewKey.Space when mods == KeyMods.None && focus == KeyFocus.ClipItem:
                Observe(ToggleSelectedClipsAsync());
                return true;
            case ReviewKey.Space when mods == KeyMods.None && focus == KeyFocus.PhotoItem && FocusedTile is { } tile:
                Observe(tile.ToggleCommand.ExecuteAsync(null));
                return true;
            case ReviewKey.S when ctrl && shift && focus == KeyFocus.ClipItem && FocusedClip is { } clip:
                Observe(SplitBeforeAsync(clip.Id));
                return true;
            case ReviewKey.F2 when focus == KeyFocus.TimelineItem && Videos.SelectedCard is { } card:
                FocusRenameRequested?.Invoke(card.Anchor);
                return true;
            default:
                return false;
        }
    }
}
