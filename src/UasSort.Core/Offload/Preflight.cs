// src/UasSort.Core/Offload/Preflight.cs
using System.Collections.Immutable;

namespace UasSort.Core.Offload;

/// <summary>Ref §10.2: the checks before Start offload. Writes nothing: listings, attribute reads, identity and a lock probe only.</summary>
public static class Preflight
{
    private const long OneGiB = 1L << 30;
    private static readonly IReadOnlySet<string> NoExclusions = ImmutableHashSet<string>.Empty;

    public static PreflightReport Check(OffloadBatch batch, Plan plan, IFileOps files, IDirectoryLister lister, ICardReader card,
                                        ILedgerStore ledger, IOffloadLock offloadLock, Settings settings)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(lister);
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(offloadLock);
        ArgumentNullException.ThrowIfNull(settings);

        var issues = new IssueList();
        var cardRoot = plan.Base.Scan.Inventory.Source.Root;
        var drive = OffloadPaths.DriveLabel(OffloadPaths.VolumeRoot(cardRoot));

        foreach (var i in plan.Issues)
            if (i.Severity == IssueSeverity.Blocking || i.RequiresAckAtPreflight || i.Code == IssueCode.LedgerNoHistory) issues.Add(i);

        try
        {
            if (card.CurrentIdentity() != batch.Card)
                issues.Block(IssueCode.CardIdentityChanged, $"A different card is in {drive}; rescan");
        }
        catch (Exception e) when (Failures.IsIo(e))
        {
            issues.Block(IssueCode.CardUnreadable, "The card's top level can't be read");
        }
        if (TryList(lister, cardRoot) is not { } top || top.Errors.Length > 0)
            issues.Block(IssueCode.CardUnreadable, "The card's top level can't be read");

        using (var probe = offloadLock.TryAcquire())
            if (probe is null) issues.Block(IssueCode.OffloadLockHeld, "Another uas-sort window is offloading");

        var targeted = new List<string> { settings.VideoRoot };
        if (batch.Jobs.Any(j => j.Root == DestRoot.Photo)) targeted.Add(settings.PhotoRoot);
        foreach (var root in targeted)
            if (OffloadPaths.IsUnder(root, cardRoot) || TryList(lister, root) is null)
                issues.Block(IssueCode.RootMissing, $"{root} is not available; its items are unticked");

        if (!settings.RootsConfirmed)
            issues.Block(IssueCode.RootsUnconfirmed, "Confirm the video and photo folders (settings were recovered)");

        var ledgerFolder = LedgerPaths.For(settings.VideoRoot);
        try
        {
            switch (ledger.Check().State)
            {
                case LedgerFolderState.CloudOnly:
                    issues.Block(IssueCode.LedgerCloudOnly,
                                 $"Set {OffloadPaths.FileName(settings.VideoRoot)}\\{LedgerPaths.FolderName} to Always keep on this device");
                    break;
                case LedgerFolderState.Unwritable:
                    issues.Block(IssueCode.LedgerUnwritable, $"Can't write the history file in {ledgerFolder}");
                    break;
                case LedgerFolderState.VideoRootMissing:
                    issues.Block(IssueCode.RootMissing, $"{settings.VideoRoot} is not available; its items are unticked");
                    break;
                case LedgerFolderState.Missing or LedgerFolderState.Empty:
                    issues.Info(IssueCode.LedgerNoHistory, "No history yet; this offload starts it");
                    break;
            }
        }
        catch (Exception e) when (Failures.IsIo(e))
        {
            issues.Block(IssueCode.LedgerUnlistable, $"Can't list {ledgerFolder}");
        }

        var groupsWithJobs = batch.Jobs.Where(j => j.Group is not null).Select(j => j.Group!.Value).ToHashSet();
        foreach (var f in batch.Folders.Where(f => !f.Create && groupsWithJobs.Contains(f.Group)))
            if (TryList(lister, f.FullPath) is null)
                issues.Block(IssueCode.AppendTargetGone,
                             $"'{OffloadPaths.FileName(f.FullPath)}' was renamed or moved since the scan; rescan", f.Group.Anchor);

        foreach (var dup in batch.Jobs.GroupBy(j => j.DestPath, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            issues.Block(IssueCode.DuplicateDestination, $"Two files would be written to {dup.Key}", dup.Skip(1).First().Item);

        var alreadyThere = new List<ItemId>();
        var alreadyPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var job in batch.Jobs)
        {
            var temp = OffloadPaths.TempOf(job.DestPath);
            if (temp.Length > OffloadPaths.MaxTempPathLength)
                issues.Block(IssueCode.TempPathTooLong,
                             $"Path too long for OneDrive ({temp.Length} > {OffloadPaths.MaxTempPathLength} characters): {job.DestPath}", job.Item);
            if (SizeOf(files, job.DestPath) == job.Size)
            {
                if (!alreadyThere.Contains(job.Item)) alreadyThere.Add(job.Item);
                alreadyPaths.Add(job.DestPath);
                issues.Info(IssueCode.DestinationAlreadyThere, "Already at the destination (same size); recorded, not copied", job.Item);
            }
        }

        var volumes = ImmutableArray.CreateBuilder<VolumeNeed>();
        var byVolume = batch.Jobs.Where(j => !alreadyPaths.Contains(j.DestPath))
                                 .GroupBy(j => OffloadPaths.VolumeRoot(j.DestPath), StringComparer.OrdinalIgnoreCase);
        foreach (var vol in byVolume)
        {
            long sum = vol.Sum(j => j.Size);
            long required = sum + Math.Max(OneGiB, (sum + 49) / 50);
            long free;
            try { free = files.FreeBytes(vol.Key); }
            catch (Exception e) when (Failures.IsIo(e)) { free = 0; }
            volumes.Add(new VolumeNeed(vol.Key, vol.Count(), sum, free, required));
            if (free < required)
                issues.Block(IssueCode.LowDiskSpace,
                             $"{OffloadPaths.DriveLabel(vol.Key)} needs {OffloadPaths.Gb(required)} free; {OffloadPaths.Gb(free)} available");
        }

        var stale = ImmutableArray.CreateBuilder<string>();
        foreach (var dir in batch.Jobs.Select(j => OffloadPaths.DirectoryOf(j.DestPath)).Distinct(StringComparer.OrdinalIgnoreCase))
            if (TryList(lister, dir) is { } listing)
                stale.AddRange(listing.Entries.Where(e => !e.IsDirectory && OffloadPaths.IsTemp(e.FullPath)).Select(e => e.FullPath));
        if (stale.Count > 0)
            issues.Info(IssueCode.StaleTempFiles, $"{stale.Count} unfinished temp files from an earlier run will be deleted when the offload starts");

        AddWarnings(plan, issues);

        var foldersToCreate = batch.Folders.Where(f => f.Create).Select(f => f.FullPath).ToImmutableArray();
        var appended = plan.Groups
            .Where(g => g.Target is Append && groupsWithJobs.Contains(g.Id))
            .Select(g => (((Append)g.Target).Folder.FullPath, ((Append)g.Target).Confidence))
            .ToImmutableArray();
        return new PreflightReport(issues.ToImmutable(), foldersToCreate, appended, volumes.ToImmutable(), stale.ToImmutable(),
                                   [.. alreadyThere]);
    }

    /// <summary>The sheet's warnings (Ref §10.2 "Warnings"): counted over the plan's items that are not ticked, plus ticked items whose
    /// date or place rests on an assumption (CheckDate, GpsGuessed, TzFallback, ClockFromSetting, ProbeFailed).</summary>
    private static void AddWarnings(Plan plan, IssueList issues)
    {
        const ItemFlags assumed = ItemFlags.CheckDate | ItemFlags.GpsGuessed | ItemFlags.TzFallback | ItemFlags.ClockFromSetting | ItemFlags.ProbeFailed;
        int assumptions = 0, probably = 0, unticked = 0, unfinished = 0, conflicts = 0;
        foreach (var item in plan.Base.Items)
        {
            bool included = plan.Included.Contains(item.Raw.Unit.Id);
            bool truncated = item.Flags.HasFlag(ItemFlags.Truncated) || item.Raw.Unit is VideoUnit { HasTrinf: true };
            if (included)
            {
                if ((item.Flags & assumed) != 0) assumptions++;
                continue;
            }
            switch (item.Newness)
            {
                case ProbablyImported when item.Raw.Unit is not VideoUnit: probably++; break;
                case IsNew when truncated: unfinished++; break;
                case IsNew: unticked++; break;
                case Conflict: conflicts++; break;
            }
        }
        if (assumptions > 0) issues.Warn(IssueCode.AssumptionsTicked, $"{assumptions} ticked items rely on assumptions");
        if (probably > 0) issues.Warn(IssueCode.ProbablyImportedLeftOut, $"{probably} probably-imported photos are left out");
        if (unticked > 0) issues.Warn(IssueCode.NewItemsUnticked, $"{unticked} new items are unticked");
        if (unfinished > 0) issues.Warn(IssueCode.UnfinishedRecordings, $"{unfinished} unfinished recordings");
        if (conflicts > 0) issues.Warn(IssueCode.ConflictsLeftOut, $"{conflicts} conflicts are left out");
    }

    /// <summary>A non-recursive listing of an existing directory, or null when it is missing, not a directory or unreadable.</summary>
    internal static ListingResult? TryList(IDirectoryLister lister, string dir)
    {
        try
        {
            var r = lister.Enumerate(dir, recurse: false, NoExclusions);
            return r.Errors.Any(e => OffloadPaths.Same(e.Path, dir)) ? null : r;
        }
        catch (Exception e) when (Failures.IsIo(e))
        {
            return null;
        }
    }

    private static long? SizeOf(IFileOps files, string path)
    {
        try { return files.TryGetSize(path, out var size) ? size : null; }
        catch (Exception e) when (Failures.IsIo(e)) { return null; }
    }

    private sealed class IssueList
    {
        private readonly List<Issue> _all = [];
        private readonly HashSet<(IssueCode, ItemId?, string)> _keys = [];

        public void Add(Issue i)
        {
            if (_keys.Add((i.Code, i.Anchor, i.Message))) _all.Add(i);
        }

        public void Block(IssueCode code, string message, ItemId? anchor = null)
            => Add(new Issue(IssueSeverity.Blocking, code, message, anchor, [], false));

        public void Warn(IssueCode code, string message, ItemId? anchor = null)
            => Add(new Issue(IssueSeverity.Warning, code, message, anchor, [], false));

        public void Info(IssueCode code, string message, ItemId? anchor = null)
            => Add(new Issue(IssueSeverity.Info, code, message, anchor, [], false));

        public ImmutableArray<Issue> ToImmutable() => [.. _all];
    }
}
