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

    /// <summary>Ref §9.12. Page keys use modifiers; list keys act only on item containers, never in a TextBox.</summary>
    public bool HandleKey(ReviewKey key, KeyMods mods, KeyFocus focus)
    {
        var ctrl = mods.HasFlag(KeyMods.Ctrl);
        var shift = mods.HasFlag(KeyMods.Shift);
        if (focus == KeyFocus.TextBox && key is ReviewKey.Z or ReviewKey.Y or ReviewKey.Space or ReviewKey.S or ReviewKey.M or ReviewKey.N)
            return false;

        switch (key)
        {
            case ReviewKey.Z when ctrl && !shift:
                _ = UndoCommand.ExecuteAsync(null);
                return true;
            case ReviewKey.Z when ctrl && shift:
            case ReviewKey.Y when ctrl:
                _ = RedoCommand.ExecuteAsync(null);
                return true;
            case ReviewKey.M when ctrl && !shift:
                _ = MergeSelectedWithNextAsync();
                return true;
            case ReviewKey.N when ctrl && shift:
                _ = MoveSelectedToNewGroupAsync();
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
                _ = ToggleSelectedClipsAsync();
                return true;
            case ReviewKey.Space when mods == KeyMods.None && focus == KeyFocus.PhotoItem && FocusedTile is { } tile:
                _ = tile.ToggleCommand.ExecuteAsync(null);
                return true;
            case ReviewKey.S when ctrl && shift && focus == KeyFocus.ClipItem && FocusedClip is { } clip:
                _ = SplitBeforeAsync(clip.Id);
                return true;
            case ReviewKey.F2 when focus == KeyFocus.TimelineItem && Videos.SelectedCard is { } card:
                FocusRenameRequested?.Invoke(card.Anchor);
                return true;
            default:
                return false;
        }
    }
}
