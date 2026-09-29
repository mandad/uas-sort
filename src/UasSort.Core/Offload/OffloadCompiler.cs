using System.Collections.Immutable;

namespace UasSort.Core.Offload;

/// <summary>Ref §10.1: turns a Plan into the ordered copy jobs of one Commit. Pure.</summary>
public static partial class OffloadCompiler
{
    public static OffloadBatch Compile(Plan plan) => Compile(plan, Guid.NewGuid().ToString("N"));

    public static OffloadBatch Compile(Plan plan, string runId)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var scan = plan.Base.Scan;
        var settings = scan.Settings;
        var card = scan.Inventory.Source.Identity
                   ?? throw new InvalidOperationException("The card identity isn't pinned; rescan the card.");
        var items = plan.Base.Items.ToDictionary(i => i.Raw.Unit.Id);
        var jobs = ImmutableArray.CreateBuilder<CopyJob>();
        var folders = ImmutableArray.CreateBuilder<FolderPlan>();
        var seen = ImmutableArray.CreateBuilder<ItemId>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new NameAllocator(scan.Library, scan.Ledger, taken);

        var groups = plan.Groups
            .OrderBy(g => g.Videos.Min(v => items[v].Time.CaptureUtc))
            .ThenBy(g => g.Id.Anchor.CardRelPath, StringComparer.Ordinal);
        foreach (var g in groups)
        {
            (string Dir, bool Create)? dest = g.Target switch
            {
                NewFolder nf => (OffloadPaths.Join(settings.VideoRoot, nf.RelPath), true),
                Append a => (a.Folder.FullPath, false),
                AlreadyImported or NothingToCopy or SkipGroup => null,
            };
            if (dest is not { } d)
            {
                if (g.Target is AlreadyImported ai && g.Centroid is not null && !scan.Ledger.Folders.ContainsKey(ai.Folder.FullPath))
                    folders.Add(new FolderPlan(g.Id, ai.Folder.FullPath, false, ai.Folder.Description, g.Centroid, g.Start, g.End, TzOf(g, items)));
                continue;
            }

            int before = jobs.Count;
            var videos = g.Videos.OrderBy(v => items[v].Time.CaptureUtc).ThenBy(v => v.CardRelPath, StringComparer.Ordinal);
            foreach (var id in videos)
            {
                var item = items[id];
                if (!plan.Included.Contains(id) || item.Newness is not (IsNew or Conflict)) continue;
                var mp4 = ((VideoUnit)item.Raw.Unit).Mp4;
                var name = OffloadPaths.FileName(mp4.RelPath);
                if (item.Newness is Conflict) name = names.Free(d.Dir, [(name, mp4.Size)])[0];
                jobs.Add(Job(id, mp4, OffloadPaths.Join(d.Dir, name), DestRoot.Video, g.Id, d.Create, taken));
            }
            if (jobs.Count > before)
                folders.Add(new FolderPlan(g.Id, d.Dir, d.Create, g.Description, g.Centroid, g.Start, g.End, TzOf(g, items)));
        }

        var photos = plan.Base.Items.Where(i => i.Raw.Unit is PhotoUnit)
            .OrderBy(i => i.Time.CaptureUtc).ThenBy(i => i.Raw.Unit.Id.CardRelPath, StringComparer.Ordinal);
        foreach (var item in photos)
        {
            var unit = (PhotoUnit)item.Raw.Unit;
            if (item.Newness is Imported or Decided) continue;
            bool included = plan.Included.Contains(unit.Id);
            if (included || item.Newness is IsNew or Conflict) seen.Add(unit.Id);
            if (!included) continue;
            var files = new List<(string Name, long Size)> { (OffloadPaths.FileName(unit.Primary.RelPath), unit.Primary.Size) };
            bool twin = settings.CopyJpgTwin && unit.JpgTwin is not null;
            if (twin) files.Add((OffloadPaths.FileName(unit.JpgTwin!.RelPath), unit.JpgTwin.Size));
            var destNames = item.Newness is Conflict ? names.Free(settings.PhotoRoot, files) : files.Select(f => f.Name).ToList();
            jobs.Add(Job(unit.Id, unit.Primary, OffloadPaths.Join(settings.PhotoRoot, destNames[0]), DestRoot.Photo, null, false, taken));
            if (twin)
                jobs.Add(Job(unit.Id, unit.JpgTwin!, OffloadPaths.Join(settings.PhotoRoot, destNames[1]), DestRoot.Photo, null, false, taken));
        }

        var sets = plan.Base.Items.Where(i => i.Raw.Unit is SetUnit)
            .OrderBy(i => i.Time.CaptureUtc).ThenBy(i => i.Raw.Unit.Id.CardRelPath, StringComparer.Ordinal);
        foreach (var item in sets)
        {
            var unit = (SetUnit)item.Raw.Unit;
            if (item.Newness is Imported or Decided) continue;
            if (!plan.Base.Sets.TryGetValue(unit.Id, out var placement) || placement.Resolution == SetResolution.Imported) continue;
            bool included = plan.Included.Contains(unit.Id);
            if (included || item.Newness is IsNew or Conflict) seen.Add(unit.Id);
            if (!included) continue;
            var dir = OffloadPaths.Join(settings.PhotoRoot, placement.FolderName);
            bool resume = placement.Resolution == SetResolution.Resume;
            var wanted = new HashSet<string>(placement.MembersToCopy, StringComparer.OrdinalIgnoreCase);
            foreach (var member in unit.Members.OrderBy(m => OffloadPaths.FileName(m.RelPath), StringComparer.OrdinalIgnoreCase))
            {
                var name = OffloadPaths.FileName(member.RelPath);
                if (resume && !wanted.Contains(name)) continue;
                jobs.Add(Job(unit.Id, member, OffloadPaths.Join(dir, name), DestRoot.Photo, null, !resume, taken));
            }
        }

        return new OffloadBatch(runId, card, jobs.ToImmutable(), seen.ToImmutable(), folders.ToImmutable());
    }

    private static CopyJob Job(ItemId id, CardEntry entry, string dest, DestRoot root, GroupId? group, bool creates, HashSet<string> taken)
    {
        taken.Add(dest);
        return new CopyJob(id, entry.RelPath, entry.Size, entry.MtimeUtc, entry.CreationUtc, dest, root, group, creates);
    }

    private static string TzOf(VideoGroup g, Dictionary<ItemId, Item> items)
        => items[g.Videos.MinBy(v => items[v].Time.CaptureUtc)].Time.TzId;

    /// <summary>Ref §7.4: the next free "stem (n).ext" across the listings, the ledger and this batch.</summary>
    private sealed class NameAllocator(LibraryIndex library, LedgerSnapshot ledger, HashSet<string> batch)
    {
        public List<string> Free(string dir, List<(string Name, long Size)> files)
        {
            for (int n = 2; ; n++)
            {
                var candidates = files.Select(f => OffloadPaths.WithCopyNumber(f.Name, n)).ToList();
                bool free = true;
                for (int i = 0; i < files.Count && free; i++) free = !Taken(dir, candidates[i], files[i].Size);
                if (free) return candidates;
            }
        }

        private bool Taken(string dir, string candidate, long size)
        {
            if (batch.Contains(OffloadPaths.Join(dir, candidate))) return true;
            var norm = OffloadPaths.NormName(candidate);
            bool Named(string path) => string.Equals(OffloadPaths.FileName(path), candidate, StringComparison.OrdinalIgnoreCase);
            if (library.SameNameOtherSize(norm, size).Any(f => Named(f.FullPath))) return true;
            if (library.Match(new FileKey(norm, size)).Any(f => Named(f.FullPath))) return true;
            return ledger.Files.Values.Any(f => f.Key.NormName == norm && Named(f.Dest));
        }
    }
}
