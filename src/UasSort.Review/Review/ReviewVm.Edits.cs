// src/UasSort.Review/Review/ReviewVm.Edits.cs
using System.Text.RegularExpressions;

namespace UasSort.Review;

public sealed partial class ReviewVm
{
    [GeneratedRegex(@"^(\d{4})-(\d{2})-(\d{2})(?:\s|$)", RegexOptions.CultureInvariant)]
    private static partial Regex DatedFolder();

    public Task SetIncludedAsync(IReadOnlyList<ItemId> items, bool included) => ApplyEditAsync(new SetIncluded([.. items], included));
    public Task SplitBeforeAsync(ItemId first) => ApplyEditAsync(new SplitBefore(first));
    public Task MergeAsync(ItemId inA, ItemId inB) => ApplyEditAsync(new Merge(inA, inB));

    public Task ApplyQuickFixAsync(QuickFix fix) => fix.Edits.Length > 0 ? ApplyEditsAsync(fix.Edits) : RunUiFix(fix.Label, null);

    public Task MergeSelectedWithNextAsync()
        => Videos.SelectedCard is { } card && Videos.NextCard(card) is { } next ? ApplyEditAsync(new Merge(card.Anchor, next.Anchor)) : Task.CompletedTask;

    public Task MoveSelectedToNewGroupAsync()
        => Videos.SelectedClipIds.Count == 0 ? Task.CompletedTask : ApplyEditAsync(new MoveToNewGroup([.. Videos.SelectedClipIds]));

    /// <summary>The clip menu's "Move to group ▸ target" (Ref §9.5): one MoveToGroup edit into the target card's group.</summary>
    public Task MoveSelectedToGroupAsync(GroupCardVm target)
        => Videos.SelectedClipIds.Count == 0 ? Task.CompletedTask : ApplyEditAsync(new MoveToGroup([.. Videos.SelectedClipIds], target.Anchor));

    public Task ToggleSelectedClipsAsync()
    {
        var ids = Videos.SelectedClipIds;
        if (ids.Count == 0) return Task.CompletedTask;
        return SetIncludedAsync(ids, !ids.All(Plan.Included.Contains));
    }

    public async Task RenameAsync(GroupCardVm card, string text)
    {
        if (card.Group is not { } g) return;
        var trimmed = text.Trim();
        if (string.Equals(trimmed, g.Description, StringComparison.Ordinal)) return;
        if (trimmed.Length > 0 && CleanForCheck(trimmed).Length == 0)
        {
            LastError = "Use letters or numbers in the folder name";
            return;
        }
        await ApplyEditAsync(new Rename(g.Id.Anchor, trimmed.Length == 0 ? null : trimmed, g.Videos)).ConfigureAwait(true);
    }

    public async Task RetargetAsync(GroupCardVm card, RetargetOptionVm option)
    {
        if (card.Group is not { } g) return;
        switch (option.Kind)
        {
            case RetargetKind.Auto:
                await ApplyEditAsync(new Retarget(g.Id.Anchor, new AutoTarget(), false, g.Videos)).ConfigureAwait(true);
                break;
            case RetargetKind.NewFolder:
                await ApplyEditAsync(new Retarget(g.Id.Anchor, new NewFolderTarget(), false, g.Videos)).ConfigureAwait(true);
                break;
            case RetargetKind.Skip:
                await ApplyEditAsync(new Retarget(g.Id.Anchor, new SkipTarget(), false, g.Videos)).ConfigureAwait(true);
                break;
            case RetargetKind.Append when option.FolderPath is { } path:
                await AppendToAsync(g, path, option.FolderDate).ConfigureAwait(true);
                break;
            case RetargetKind.Browse:
            default:
                break; // Browse: the App shows the FolderPicker, then calls BrowseRetargetAsync.
        }
    }

    /// <summary>Validates a Browse existing… result (Ref §9.4 item 4, §8.9 RetargetIntoReservedFolder).</summary>
    public async Task BrowseRetargetAsync(GroupCardVm card, string? pickedFolder)
    {
        if (card.Group is not { } g || string.IsNullOrWhiteSpace(pickedFolder)) return;
        var settings = Plan.Base.Scan.Settings;
        var picked = pickedFolder.TrimEnd('\\');
        var videoRoot = settings.VideoRoot.TrimEnd('\\');
        string[] reserved = [LedgerPaths.For(videoRoot), settings.PhotoRoot.TrimEnd('\\'), .. settings.PreviousPhotoRoots.Select(p => p.TrimEnd('\\'))];
        if (reserved.Any(r => IsSameOrUnder(picked, r)))
        {
            LastError = "That folder is reserved for uas-sort history or photos";
            return;
        }
        if (!IsSameOrUnder(picked, videoRoot) || string.Equals(picked, videoRoot, StringComparison.OrdinalIgnoreCase))
        {
            LastError = $"Pick a folder inside {Path.GetFileName(videoRoot)}";
            return;
        }
        var m = DatedFolder().Match(Path.GetFileName(picked));
        DateOnly? date = m.Success
            ? new DateOnly(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                           int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture))
            : null;
        await AppendToAsync(g, picked, date).ConfigureAwait(true);
    }

    private async Task AppendToAsync(VideoGroup g, string path, DateOnly? folderDate)
    {
        var confirmed = false;
        if (folderDate is { } d && d > g.Start)
        {
            var answer = await _s.Dialogs.ShowAsync(new DialogRequest("Append to a later folder?",
                $"Folder is dated {Fmt.Day(d)}; these clips start {Fmt.Day(g.Start)}", "Append", null, "Cancel")).ConfigureAwait(true);
            if (answer != DialogResult.Primary) return;
            confirmed = true;
        }
        await ApplyEditAsync(new Retarget(g.Id.Anchor, new AppendTo(path), confirmed, g.Videos)).ConfigureAwait(true);
    }

    private async Task<bool> ApplyEditAsync(PlanEdit edit)
    {
        if (IsReadOnly) return false;
        return Handle(await Session.ApplyAsync(edit, CancellationToken.None).ConfigureAwait(true));
    }

    private async Task ApplyEditsAsync(IReadOnlyList<PlanEdit> edits)
    {
        if (IsReadOnly) return;
        Handle(await Session.ApplyAllAsync(edits, CancellationToken.None).ConfigureAwait(true));
    }

    private bool Handle(EditResult result) => result switch
    {
        Applied a => OnApplied(a.Plan),
        Rejected r => OnRejected(r),
    };

    private bool OnApplied(Plan p)
    {
        LastError = null;
        UpdateUndo();
        Accept(p);
        OnEditCommitted();
        return true;
    }

    private bool OnRejected(Rejected r)
    {
        LastError = r.Message;
        return false;
    }

    private async Task UndoAsync()
    {
        if (!Session.CanUndo || IsReadOnly) return;
        var p = await Session.UndoAsync().ConfigureAwait(true);
        AfterHistoryMove(p);
    }

    private async Task RedoAsync()
    {
        if (!Session.CanRedo || IsReadOnly) return;
        var p = await Session.RedoAsync().ConfigureAwait(true);
        AfterHistoryMove(p);
    }

    private void AfterHistoryMove(Plan p)
    {
        Tuning.SetCommitted(p.Tuning);
        UpdateUndo();
        Accept(p);
        OnEditCommitted();
    }

    /// <summary>CanUndo/CanRedo mirror PlanSession's own history (Part 06); the VM keeps no counters of its own.</summary>
    private void UpdateUndo()
    {
        CanUndo = Session.CanUndo;
        CanRedo = Session.CanRedo;
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    void ITuningHost.Preview(Tuning t)
    {
        if (IsReadOnly) return;
        Session.Preview(t);
        _map?.Send(new MapSetRadius(t.RadiusMiles));
    }

    async Task ITuningHost.CommitTuningAsync(Tuning t)
    {
        if (IsReadOnly) return;
        await Session.CommitTuningAsync().ConfigureAwait(true);
        UpdateUndo();
        OnEditCommitted();
    }

    private static string CleanForCheck(string s)
    {
        var chars = s.Select(c => c < 0x20 || "<>:\"/\\|?*".Contains(c, StringComparison.Ordinal) ? ' ' : c).ToArray();
        return new string(chars).Trim().TrimEnd('.', ' ');
    }

    private static bool IsSameOrUnder(string path, string root)
        => string.Equals(path, root, StringComparison.OrdinalIgnoreCase)
           || path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase);
}
