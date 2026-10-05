// src/UasSort.Core/Cleanup/CleanupPlanner.Candidates.cs
namespace UasSort.Core.Cleanup;

/// <summary>Ref §10.6: pure; the only caller of CleanupPlan's constructor (Build, Task 08.5).</summary>
public static partial class CleanupPlanner
{
    private const uint DirectoryAttribute = 0x10;

    private static bool IsDirectory(CardEntry e) => (e.RawAttributes & DirectoryAttribute) != 0;

    public static ImmutableArray<CleanupCandidate> Candidates(CleanupInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var ctx = new Context(inputs);
        var result = new List<CleanupCandidate>();
        foreach (var unit in inputs.Inventory.Units)
        {
            if (!ctx.Items.TryGetValue(unit.Id, out var item)) continue;   // no metadata: its files stay in LooseFiles (Never)
            var c = Candidate(ctx, unit, item);
            if (c is not null) result.Add(c);
        }
        return [.. result.OrderBy(c => c.CaptureUtc).ThenBy(c => c.Unit.CardRelPath, StringComparer.Ordinal)];
    }

    public static ImmutableArray<CleanupKept> LooseFiles(CleanupInputs inputs, ImmutableArray<CleanupCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var ctx = new Context(inputs);
        var inUnits = new HashSet<string>(candidates.SelectMany(c => c.Files).Select(f => CleanupPaths.Rel(f.RelPath)),
                                          StringComparer.OrdinalIgnoreCase);
        var cluster = inputs.Space.ClusterBytes;
        return [.. inputs.Inventory.Entries
            .Where(e => !IsDirectory(e) && !inUnits.Contains(CleanupPaths.Rel(e.RelPath)))
            .OrderBy(e => CleanupPaths.Rel(e.RelPath), StringComparer.Ordinal)
            .Select(e => new CleanupKept(null, [CleanupPaths.Rel(e.RelPath)], CleanupPaths.Allocated(e.Size, cluster), null,
                                         CleanupRules.LooseReason(e, ctx.Changed(e.RelPath), ctx.UnderEnumerationError(e.RelPath))))];
    }

    private sealed class Context
    {
        public Context(CleanupInputs inputs)
        {
            Inputs = inputs;
            Fresh = FreshEvidence.From(inputs.FreshListings);
            Items = inputs.Plan.Base.Items.ToDictionary(i => i.Raw.Unit.Id);
            foreach (var u in inputs.Audit.Units)
                foreach (var l in u.Lines) AuditByPath[CleanupPaths.Rel(l.CardRelPath)] = l;
            if (inputs.Offload is { } off)
                foreach (var o in off.Outcomes)
                {
                    if (o is ChangedOnCard ch) ChangedInRun.Add(CleanupPaths.Rel(ch.Job.CardRelPath));
                    if (o is Verified or AlreadyThere) Copied.Add(o.Job.Item);
                }
            EnumerationErrorDirs = [.. inputs.Inventory.Warnings
                .Where(w => w.ForcesNotSafe && w.RelPath is not null).Select(w => CleanupPaths.Rel(w.RelPath!))];
            FilesByDir = inputs.Inventory.Entries.Where(e => !IsDirectory(e))
                .GroupBy(e => CleanupPaths.Dir(e.RelPath), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        }

        public CleanupInputs Inputs { get; }
        public FreshEvidence Fresh { get; }
        public Dictionary<ItemId, Item> Items { get; }
        public Dictionary<string, AuditLine> AuditByPath { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ChangedInRun { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<ItemId> Copied { get; } = [];
        public List<string> EnumerationErrorDirs { get; }
        public Dictionary<string, List<CardEntry>> FilesByDir { get; }

        public bool Changed(string relPath)
        {
            var rel = CleanupPaths.Rel(relPath);
            return ChangedInRun.Contains(rel)
                || (AuditByPath.TryGetValue(rel, out var l)
                    && l.Detail.StartsWith(CleanupRules.ChangedSinceScanDetail, StringComparison.OrdinalIgnoreCase));
        }

        public bool UnderEnumerationError(string relPath)
            => EnumerationErrorDirs.Exists(d => CleanupPaths.IsUnder(relPath, d));
    }

    private static CleanupCandidate? Candidate(Context ctx, MediaUnit unit, Item item)
    {
        var inputs = ctx.Inputs;
        var (kind, primaries, companions, deleteOrder) = unit switch
        {
            VideoUnit v => VideoFiles(ctx, v),
            PhotoUnit p => PhotoFiles(p, inputs.Settings.CopyJpgTwin),
            SetUnit s => SetFiles(s),
        };
        if (primaries.Count == 0) return null;

        var tz = item.Time.TzId;
        var truncated = kind == ItemKind.Video && item.Flags.HasFlag(ItemFlags.Truncated);
        var probeError = item.Raw.ProbeError is not null || item.Flags.HasFlag(ItemFlags.ProbeFailed);

        FileFacts Facts(CardEntry e, bool companion)
        {
            var rel = CleanupPaths.Rel(e.RelPath);
            var category = ctx.AuditByPath.TryGetValue(rel, out var line) ? line.Category : AuditCategory.Unaccounted;
            var key = CleanupKeys.Key(rel, e.Size);
            inputs.FreshLedger.Decisions.TryGetValue(key, out var decision);
            inputs.FreshLedger.Files.TryGetValue(key, out var record);
            inputs.FreshLedger.PhotoDeletes.TryGetValue(key, out var removed);
            if (kind == ItemKind.Video
                || (unit is SetUnit su && !string.Equals(removed?.Set, su.SetName, StringComparison.OrdinalIgnoreCase))) removed = null;
            return new FileFacts(e, kind, companion, ctx.Changed(rel), ctx.UnderEnumerationError(rel), probeError, truncated,
                                 category, item.Newness, decision, ctx.Fresh.ListedFolder(key) is not null, record, tz, removed);
        }

        var primaryVerdicts = primaries.Select(p => (File: p, Facts: Facts(p, false), Verdict: CleanupRules.Classify(Facts(p, false))!)).ToList();
        var companionNever = companions.Select(c => CleanupRules.Classify(Facts(c, true))).FirstOrDefault(v => v is not null);

        var worst = primaryVerdicts.Max(x => x.Verdict.Eligibility);
        FileVerdict decisive;
        if (worst != CleanupEligibility.Never && companionNever is not null) { worst = CleanupEligibility.Never; decisive = companionNever; }
        else decisive = primaryVerdicts.First(x => x.Verdict.Eligibility == worst).Verdict;

        var evidence = primaryVerdicts.Max(x => x.Facts.Category);
        EvidenceSource? source = worst == CleanupEligibility.Evidence
            ? primaryVerdicts.Exists(x => x.Verdict.Source == EvidenceSource.HistoryOnly) ? EvidenceSource.HistoryOnly : EvidenceSource.Listed
            : null;
        var proofs = primaryVerdicts.Select(x =>
        {
            var key = CleanupKeys.Key(x.File.RelPath, x.File.Size);
            return new FileProof(CleanupPaths.Rel(x.File.RelPath), key, x.Facts.Category, ctx.Fresh.ListedFolder(key),
                                 x.Facts.LedgerRecord is { Verify: VerifyKind.Unbuffered or VerifyKind.Cached },
                                 x.Facts.Category == AuditCategory.ConfirmedByYou ? x.Facts.Decision?.Id : null);
        }).ToImmutableArray();

        var ticked = worst == CleanupEligibility.NotInLibrary && inputs.Plan.Included.Contains(unit.Id) && !ctx.Copied.Contains(unit.Id);
        var cluster = inputs.Space.ClusterBytes;
        var point = item.Gps?.Point;
        return new CleanupCandidate(unit.Id, [.. deleteOrder], deleteOrder.Sum(f => CleanupPaths.Allocated(f.Size, cluster)),
            item.Time.CaptureUtc, item.Time.LocalDate, tz, worst, evidence, decisive.Reason, item.Raw.Mp4?.Duration, point,
            Place(inputs.Places, point), kind, item.Time.LocalTime,
            worst == CleanupEligibility.NotInLibrary ? decisive.NotInLibrary : null,
            unit is SetUnit ? CleanupPaths.Rel(unit.Id.CardRelPath) : null, item.Session, proofs, source, ticked, [.. companions]);
    }

    private static (ItemKind, List<CardEntry>, List<CardEntry>, List<CardEntry>) VideoFiles(Context ctx, VideoUnit v)
    {
        var companions = VideoCompanions(ctx, v.Mp4);
        return (ItemKind.Video, [v.Mp4], companions, [.. companions, v.Mp4]);   // companions first, the MP4 last
    }

    private static List<CardEntry> VideoCompanions(Context ctx, CardEntry mp4)
    {
        var name = CleanupPaths.Name(mp4.RelPath);
        var dot = name.LastIndexOf('.');
        var stem = dot < 0 ? name : name[..dot];
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { stem + ".LRF", stem + ".SRT", "." + name + ".trinf", "." + name + ".avc1", "." + stem + ".avc1", stem + ".JPG" };
        if (!ctx.FilesByDir.TryGetValue(CleanupPaths.Dir(mp4.RelPath), out var siblings)) return [];
        // Skip class only: the cover JPG is a companion once the "video cover" Skip rule is on; while Unknown it stays loose (Never)
        return [.. siblings.Where(e => e.Class == EntryClass.Skip && wanted.Contains(CleanupPaths.Name(e.RelPath)))
                           .OrderBy(e => CleanupPaths.Name(e.RelPath), StringComparer.Ordinal)];
    }

    private static (ItemKind, List<CardEntry>, List<CardEntry>, List<CardEntry>) PhotoFiles(PhotoUnit p, bool copyJpgTwin)
    {
        if (p.JpgTwin is not { } twin) return (ItemKind.Photo, [p.Primary], [], [p.Primary]);
        return copyJpgTwin
            ? (ItemKind.Photo, [p.Primary, twin], [], [twin, p.Primary])
            : (ItemKind.Photo, [p.Primary], [twin], [twin, p.Primary]);           // twin never copied: a companion (row 8)
    }

    private static (ItemKind, List<CardEntry>, List<CardEntry>, List<CardEntry>) SetFiles(SetUnit s)
    {
        var members = s.Members.OrderBy(m => CleanupPaths.Name(m.RelPath), StringComparer.Ordinal).ToList();
        return (ItemKind.Set, members, [], members);
    }

    private static string? Place(IPlaceIndex? places, GeoPoint? point)
    {
        if (places is null || point is not { } p) return null;
        var feature = places.Near(p, Distance.FromMiles(1.5), PlaceClass.Feature, 1);
        if (feature.Count > 0) return Label(feature[0]);
        var populated = places.Near(p, Distance.FromMiles(3), PlaceClass.Populated, 1);
        if (populated.Count > 0) return Label(populated[0]);
        PlaceHit? town = null;
        foreach (var h in places.Near(p, Distance.FromMiles(30), PlaceClass.Populated, 50))
            if (h.Population >= 1000 && (town is null || h.Away.Meters < town.Away.Meters)) town = h;
        return town is null ? null : Label(town);
    }

    private static string Label(PlaceHit h) => $"near {h.Name} · {CleanupFormat.Miles(h.Away)}";
}
