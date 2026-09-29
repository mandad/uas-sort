// src/UasSort.Core/Offload/CommitTailRecords.cs
using System.Collections.Immutable;
using System.Globalization;

namespace UasSort.Core.Offload;

/// <summary>Ledger records written at the end of every Commit, including after Cancel, failures and stops (Ref §10.4).</summary>
public static class CommitTailRecords
{
    public static ImmutableArray<FolderRecord> CardLeftovers(OffloadBatch batch, string machine)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var withJobs = batch.Jobs.Where(j => j.Group is not null).Select(j => j.Group!.Value).ToHashSet();
        return [.. batch.Folders.Where(f => !withJobs.Contains(f.Group))
                                .Select(f => OffloadRecords.Folder(f, "cardLeftovers", batch.RunId, machine))];
    }

    public static ImmutableArray<SeenRecord> Seen(OffloadBatch batch, Plan plan, OffloadResult result, string machine, DateTime atUtc)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(result);
        var items = plan.Base.Items.ToDictionary(i => i.Raw.Unit.Id);
        var outcomes = new Dictionary<string, CopyOutcome>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in result.Outcomes) outcomes[o.Job.CardRelPath] = o;

        var records = ImmutableArray.CreateBuilder<SeenRecord>();
        foreach (var id in batch.SeenIfNotCopied)
        {
            if (!items.TryGetValue(id, out var item)) continue;
            IReadOnlyList<CardEntry> files;
            string? set = null;
            switch (item.Raw.Unit)
            {
                case PhotoUnit p:
                    files = [p.Primary];
                    break;
                case SetUnit s:
                    // a Resume set copies only its missing members; the rest are already in the library (Ref §10.4, §7.3)
                    files = plan.Base.Sets.TryGetValue(id, out var placement) && placement.Resolution == SetResolution.Resume
                        ? [.. s.Members.Where(m => placement.MembersToCopy.Contains(OffloadPaths.FileName(m.RelPath), StringComparer.OrdinalIgnoreCase))]
                        : s.Members;
                    set = s.SetName;
                    break;
                default:
                    continue;
            }
            bool included = plan.Included.Contains(id);
            var status = item.Newness is Conflict ? "Conflict" : "New";
            foreach (var f in files)
            {
                outcomes.TryGetValue(f.RelPath, out var o);
                if (o is Verified or AlreadyThere) continue;
                var why = !included ? "unticked" : o is null ? "notCopied" : Why(o);
                records.Add(new SeenRecord(OffloadRecords.Version, OffloadRecords.NewId(), machine, batch.RunId, atUtc,
                    OffloadPaths.FileName(f.RelPath), f.Size, f.RelPath, item.Time.CaptureUtc, status, why, set));
            }
        }
        return records.ToImmutable();
    }

    private static string Why(CopyOutcome o) => o switch
    {
        Verified => "verified",
        AlreadyThere => "alreadyThere",
        ConflictAtRename => "conflictAtRename",
        ChangedOnCard => "changedOnCard",
        CardSwapped => "cardSwapped",
        Failed => "failed",
        Cancelled => "cancelled",
        NotStarted => "notStarted",
    };

    public static RunRecord Run(OffloadBatch batch, Plan plan, OffloadResult result, FormatVerdict verdict, string machine, string appVersion)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(verdict);
        var inventory = plan.Base.Scan.Inventory;
        var settings = plan.Base.Scan.Settings;
        return new RunRecord(OffloadRecords.Version, OffloadRecords.NewId(), machine, batch.RunId, result.StartUtc, result.EndUtc, appVersion,
            Card(batch.Card, inventory.CameraModel, inventory.InventoryHash), new RunRoots(settings.VideoRoot, settings.PhotoRoot),
            verdict.Level.ToString(),
            verdict.Counts.Where(kv => kv.Value > 0).ToImmutableDictionary(kv => kv.Key.ToString(), kv => kv.Value));
    }

    public static RunCard Card(CardIdentity id, string? model, string inventoryHash)
    {
        ArgumentNullException.ThrowIfNull(id);
        return new RunCard(id.VolumeSerial.ToString("X8", CultureInfo.InvariantCulture), id.Label, id.FileSystem, model, inventoryHash);
    }
}
