// src/UasSort.Core/Planning/Planner.Derive.cs
using System.Globalization;
using UasSort.Core.Library;
using UasSort.Core.Naming;

namespace UasSort.Core.Planning;

public sealed partial class Planner : IPlanDeriver
{
    private static readonly StringComparer PathCmp = StringComparer.OrdinalIgnoreCase;

    private sealed record Roots(bool VideoMissing, bool PhotoMissing, ImmutableArray<string> Unavailable);

    public Plan Derive(PlanBase b, Tuning t, IReadOnlyList<PlanEdit> edits, SessionFlags flags, int revision, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var scan = b.Scan;
        var lib = scan.Library;
        var settings = scan.Settings;

        // 1–2. auto-cluster + structural edits
        var videos = b.Items.Where(i => i.Raw.Kind == ItemKind.Video).ToList();
        var cr = Clusterer.Cluster(videos, t, edits.Where(PlanEditRefs.IsStructural).ToList(), out _);
        ct.ThrowIfCancellationRequested();

        // 3. inclusion
        var roots = RootState(b);
        var included = Inclusion(b, edits, roots);

        // 4. decide in two passes
        var decider = new FolderDecider(lib, cr.Groups, t);
        var pass1 = cr.Groups.ToDictionary(g => g.Id, g => decider.Decide(g, included, null));
        var auto = new Dictionary<GroupId, GroupTarget>(pass1);
        foreach (var g in cr.Groups)
            if (decider.BordersUserSplit(g)) auto[g.Id] = decider.Decide(g, included, pass1);
        ct.ThrowIfCancellationRequested();

        // 5–6. names, pins, issues
        var issues = ImmutableArray.CreateBuilder<Issue>();
        var groups = ImmutableArray.CreateBuilder<VideoGroup>(cr.Groups.Length);
        var color = 0;
        foreach (var g in cr.Groups)
            groups.Add(BuildGroup(g, auto[g.Id], edits, included, decider, settings, lib, issues, ref color));
        SharedTargets(groups, issues);
        ItemIssues(b, issues);
        PlanIssues(b, flags, roots, included, issues);
        return new Plan(revision, b, t, groups.MoveToImmutable(), cr.Boundaries, included.ToImmutableHashSet(), issues.ToImmutable());
    }

    private static Roots RootState(PlanBase b)
    {
        var s = b.Scan.Settings;
        var un = b.Scan.Library.UnavailableRoots;
        bool Eq(string a, string c) => PathCmp.Equals(a.TrimEnd('\\'), c.TrimEnd('\\'));
        var video = un.Any(r => Eq(r, s.VideoRoot)) || b.Scan.Ledger.Status.State == LedgerFolderState.VideoRootMissing;
        return new Roots(video, video && s.PhotoRoot.StartsWith(s.VideoRoot, StringComparison.OrdinalIgnoreCase) || un.Any(r => Eq(r, s.PhotoRoot)), un);
    }

    private static HashSet<ItemId> Inclusion(PlanBase b, IReadOnlyList<PlanEdit> edits, Roots roots)
    {
        var byId = b.Items.ToDictionary(i => i.Raw.Unit.Id);
        bool RootOk(Item i) => i.Raw.Kind == ItemKind.Video ? !roots.VideoMissing : !roots.PhotoMissing;
        static bool Includable(Item i) => i.Newness is IsNew or Conflict or ProbablyImported;
        var inc = b.Items.Where(i => i.Newness is IsNew && !i.Flags.HasFlag(ItemFlags.Truncated) && RootOk(i))
                         .Select(i => i.Raw.Unit.Id).ToHashSet();
        foreach (var e in edits)
        {
            switch (e)
            {
                case SetIncluded s:
                    foreach (var id in s.Items)
                        if (byId.TryGetValue(id, out var it) && Includable(it))
                        {
                            if (s.Included) inc.Add(id);
                            else inc.Remove(id);
                        }
                    break;
                case SetDayIncluded d:
                    foreach (var it in b.Items)
                        if (it.Raw.Kind != ItemKind.Video && it.Time.LocalDate == d.Day && Includable(it))
                        {
                            if (d.Included) inc.Add(it.Raw.Unit.Id);
                            else inc.Remove(it.Raw.Unit.Id);
                        }
                    break;
            }
        }
        return inc;
    }

    private static bool PinsOverlap(ItemId aIn, ImmutableArray<ItemId> aPinned, ItemId bIn, ImmutableArray<ItemId> bPinned)
    {
        var a = new HashSet<ItemId>(aPinned.IsDefault ? [] : aPinned) { aIn };
        return a.Contains(bIn) || (!bPinned.IsDefault && bPinned.Any(a.Contains));
    }

    private static (List<Rename> Renames, List<Retarget> Retargets) Pins(GroupDraft g, IReadOnlyList<PlanEdit> edits)
    {
        var members = g.Videos.Select(v => v.Raw.Unit.Id).ToHashSet();
        var renames = new List<Rename>();
        var retargets = new List<Retarget>();
        foreach (var e in edits)
        {
            if (e is Rename r && members.Contains(r.InGroup))
            {
                renames.RemoveAll(o => PinsOverlap(o.InGroup, o.PinnedMembers, r.InGroup, r.PinnedMembers));
                renames.Add(r);
            }
            else if (e is Retarget rt && members.Contains(rt.InGroup))
            {
                retargets.RemoveAll(o => PinsOverlap(o.InGroup, o.PinnedMembers, rt.InGroup, rt.PinnedMembers));
                retargets.Add(rt);
            }
        }
        renames.RemoveAll(r => r.Description is null);                // back to suggestion
        retargets.RemoveAll(r => r.Choice is AutoTarget);              // back to auto
        return (renames, retargets);
    }

    private static string ChoiceKey(TargetChoice c) => c switch
    {
        AutoTarget => "auto",
        NewFolderTarget => "new",
        AppendTo a => "append:" + a.FolderFullPath.TrimEnd('\\').ToUpperInvariant(),
        SkipTarget => "skip",
    };

    private static string ChoiceLabel(TargetChoice c) => c switch
    {
        AutoTarget => "Auto",
        NewFolderTarget => "New folder",
        AppendTo a => Path.GetFileName(a.FolderFullPath.TrimEnd('\\')),
        SkipTarget => "Skip",
    };

    private static bool SameMembers(ImmutableArray<ItemId> pinned, ImmutableArray<ItemId> now) =>
        !pinned.IsDefault && pinned.Length == now.Length && pinned.ToHashSet().SetEquals(now);

    internal static LibraryFolderRef FolderRefFor(string path, DateOnly fallback, LibraryIndex lib)
    {
        var p = path.TrimEnd('\\');
        if (lib.Folders.FirstOrDefault(f => PathCmp.Equals(f.Ref.FullPath.TrimEnd('\\'), p)) is { } found) return found.Ref;
        var leaf = PathRules.FileName(p);
        return EventFolderName.TryParse(leaf, out var date, out var description)      // Part 05's event-folder rule
            ? new LibraryFolderRef(p, date, description)
            : new LibraryFolderRef(p, fallback, leaf);
    }

    private VideoGroup BuildGroup(GroupDraft g, GroupTarget auto, IReadOnlyList<PlanEdit> edits, HashSet<ItemId> included,
                                  FolderDecider decider, Settings s, LibraryIndex lib, ImmutableArray<Issue>.Builder issues, ref int color)
    {
        var anchor = g.Id.Anchor;
        var memberIds = g.Videos.Select(v => v.Raw.Unit.Id).ToImmutableArray();
        var (renames, retargets) = Pins(g, edits);

        // target pin (Retarget)
        var rtConflict = retargets.Select(r => ChoiceKey(r.Choice)).Distinct().Count() > 1;
        var rt = retargets.Count == 0 ? null : rtConflict ? retargets[0] : retargets[^1];
        GroupTarget target = rt is null ? auto : rt.Choice switch
        {
            AutoTarget => auto,
            NewFolderTarget => (GroupTarget)new NewFolder(""),
            AppendTo a => new Append(FolderRefFor(a.FolderFullPath, g.Start, lib), Confidence.High, "chosen by you", null),
            SkipTarget => new SkipGroup(),
        };
        PinState? targetPin = rt is null ? null : new PinState(!SameMembers(rt.PinnedMembers, memberIds), rt.PinnedMembers.Length, memberIds.Length);

        // suggestions (the append target's description first when it is not the wall)
        var suggestions = DescriptionSuggester.Suggest(g, lib, _places).ToList();
        if (target is Append ap && (g.Wall is null || !Clusterer.SameFolder(ap.Folder, g.Wall)) && ap.Folder.Description.Length > 0)
            suggestions.Insert(0, new Suggestion(ap.Folder.Description, DescSource.ExistingFolder, null, null));
        suggestions = suggestions.DistinctBy(x => x.Text, StringComparer.OrdinalIgnoreCase).Take(DescriptionSuggester.Max).ToList();

        // name pin (Rename)
        var rnConflict = renames.Select(r => FolderNamer.Clean(r.Description!).ToUpperInvariant()).Distinct().Count() > 1;
        var rn = renames.Count == 0 ? null : rnConflict ? renames[0] : renames[^1];
        PinState? namePin = rn is null ? null : new PinState(!SameMembers(rn.PinnedMembers, memberIds), rn.PinnedMembers.Length, memberIds.Length);

        string description;
        DescSource descSource;
        var editable = false;
        var hints = ImmutableArray.CreateBuilder<string>();
        switch (target)
        {
            case NewFolder:
            {
                var top = suggestions.FirstOrDefault(x => DescriptionSuggester.Prefills(x.Source));
                (description, descSource) = rn is not null ? (FolderNamer.Clean(rn.Description!), DescSource.User)
                                          : top is not null ? (FolderNamer.Clean(top.Text), top.Source)
                                          : ("", DescSource.None);
                editable = true;
                var rel = FolderNamer.NewFolderRel(g.Start, description);
                var full = Path.Join(s.VideoRoot, rel);
                var existing = lib.Folders.FirstOrDefault(f => PathCmp.Equals(f.Ref.FullPath.TrimEnd('\\'), full));
                if (existing is not null)
                {
                    target = new Append(existing.Ref, Confidence.High, FolderNamer.FolderExistsWhy, null);
                    description = existing.Ref.Description;
                    descSource = DescSource.ExistingFolder;
                    issues.Add(new Issue(IssueSeverity.Info, IssueCode.FolderExistsAppending, FolderNamer.FolderExistsWhy, anchor, [], false));
                }
                else
                {
                    target = new NewFolder(rel);
                    if (description.Length == 0)
                        issues.Add(new Issue(IssueSeverity.Blocking, IssueCode.EmptyFolderName, "Name this folder", anchor,
                                             [new QuickFix("Name it", [])], false));
                    var longest = g.Videos.Where(v => included.Contains(v.Raw.Unit.Id)).Select(v => v.Raw.Name)
                                   .DefaultIfEmpty(g.Videos[0].Raw.Name).MaxBy(n => n.Length)!;
                    var tmp = FolderNamer.TempPath(Path.Join(full, longest));
                    if (tmp.Length > FolderNamer.MaxTempPath)
                        issues.Add(new Issue(IssueSeverity.Blocking, IssueCode.TempPathTooLong,
                                             $"Path too long for OneDrive ({tmp.Length} > {FolderNamer.MaxTempPath} characters): {tmp}", anchor, [], false));
                }
                break;
            }
            case Append a:
                description = a.Folder.Description;
                descSource = DescSource.ExistingFolder;
                editable = a.Why == FolderNamer.FolderExistsWhy;
                break;
            case AlreadyImported ai:
                description = ai.Folder.Description;
                descSource = DescSource.ExistingFolder;
                break;
            default:
                description = "";
                descSource = DescSource.None;
                break;
        }

        // target issues and hints
        if (target is Append { Confidence: Confidence.Medium } medium)
            issues.Add(new Issue(IssueSeverity.Warning, IssueCode.MediumAppend, $"Appending to '{medium.Folder.Description}': {medium.Why}", anchor,
                                 medium.Hint is { } h ? [new QuickFix("New folder instead", h.Fix)] : [], true));
        if (target is Append { Hint: { } hint }) hints.Add(hint.Text);
        if (rt is null && target is NewFolder && decider.NewBeforeWallSplitPoint(g, included) is { } sp && g.Wall is { } wall)
            issues.Add(new Issue(IssueSeverity.Info, IssueCode.NewBeforeWallFolder,
                                 $"These clips start before '{wall.Description}' (dated {PlanText.ShortDate(wall.NameDate)})", anchor,
                                 [new QuickFix("Split here", [new SplitBefore(sp)])], false));

        // day splits
        var daySplits = DaySplitFinder.Find(g.Videos);
        foreach (var ds in daySplits.Where(d => d.Emphasised))
            issues.Add(new Issue(IssueSeverity.Warning, IssueCode.EmphasisedDaySplit,
                                 $"{PlanText.ShortDate(ds.From)} → {PlanText.ShortDate(ds.To)} · {PlanText.Miles(ds.Apart!.Value)} apart: likely separate outing",
                                 ds.FirstOfDay, [new QuickFix("Split here", [new SplitBefore(ds.FirstOfDay)])], true));
        if (daySplits.Any(d => d.Emphasised))
        {
            var days = g.Videos.Select(v => v.Time.LocalDate).Distinct().Count();
            var apart = daySplits.Where(d => d.Emphasised).Max(d => d.Apart!.Value.Meters);
            hints.Add($"{PlanText.Count(days, "day", "days")} · {PlanText.Miles(new Distance(apart))} apart");
        }

        // pin issues
        if (rt is not null)
        {
            if (rtConflict)
                issues.Add(new Issue(IssueSeverity.Blocking, IssueCode.ConflictingPins,
                    $"Two choices for this group: '{ChoiceLabel(retargets[0].Choice)}' vs '{ChoiceLabel(retargets[1].Choice)}'", anchor,
                    [new QuickFix("Use first", [retargets[0] with { PinnedMembers = memberIds }]),
                     new QuickFix("Use second", [retargets[1] with { PinnedMembers = memberIds }])], false));
            else if (targetPin is { MembershipChanged: true })
                issues.Add(new Issue(IssueSeverity.Warning, IssueCode.PinMembershipChanged,
                    $"{ChoiceLabel(rt.Choice)} chosen for {rt.PinnedMembers.Length} clips; group now has {memberIds.Length}", anchor,
                    [new QuickFix("Keep", [rt with { PinnedMembers = memberIds }]),
                     new QuickFix("Reset to Auto", [new Retarget(anchor, new AutoTarget(), false, [])])], true));
        }
        if (rn is not null)
        {
            if (rnConflict)
                issues.Add(new Issue(IssueSeverity.Blocking, IssueCode.ConflictingPins,
                    $"Two choices for this group: '{renames[0].Description}' vs '{renames[1].Description}'", anchor,
                    [new QuickFix("Use first", [renames[0] with { PinnedMembers = memberIds }]),
                     new QuickFix("Use second", [renames[1] with { PinnedMembers = memberIds }])], false));
            else if (namePin is { MembershipChanged: true })
                issues.Add(new Issue(IssueSeverity.Warning, IssueCode.PinMembershipChanged,
                    $"'{rn.Description}' chosen for {rn.PinnedMembers.Length} clips; group now has {memberIds.Length}", anchor,
                    [new QuickFix("Keep", [rn with { PinnedMembers = memberIds }]),
                     new QuickFix("Reset to Auto", [new Rename(anchor, null, [])])], true));
        }

        var foldable = target is AlreadyImported
            && !g.Videos.Any(v => v.Newness is Conflict or IsNew || (v.Flags & (ItemFlags.Truncated | ItemFlags.ProbeFailed)) != 0);
        var colorIndex = target is AlreadyImported ? -1 : color++ % 10;
        var spread = PlanningGeo.MaxPairwise(g.Videos.Where(v => v.Gps is not null).Select(v => v.Gps!.Point).ToList());

        return new VideoGroup(g.Id, memberIds, g.Centroid, spread, g.Start, g.End, g.Wall, target, targetPin,
                              description, descSource, editable, namePin, [.. suggestions], daySplits, hints.ToImmutable(), foldable, colorIndex);
    }

    private static void SharedTargets(ImmutableArray<VideoGroup>.Builder groups, ImmutableArray<Issue>.Builder issues)
    {
        static string? Key(VideoGroup v) => v.Target switch
        {
            Append a => "A:" + a.Folder.FullPath.TrimEnd('\\').ToUpperInvariant(),
            NewFolder n when v.Description.Length > 0 => "N:" + n.RelPath.ToUpperInvariant(),
            _ => null,
        };
        var byKey = Enumerable.Range(0, groups.Count).Where(i => Key(groups[i]) is not null).GroupBy(i => Key(groups[i])!);
        foreach (var set in byKey.Where(s => s.Count() > 1))
        {
            var idx = set.ToList();
            foreach (var i in idx)
            {
                var other = groups[idx.First(j => j != i)];
                var me = groups[i];
                issues.Add(new Issue(IssueSeverity.Info, IssueCode.SharedTarget,
                    $"Also targeted by {PlanText.DateRange(other.Start, other.End)}; both land in the same folder", me.Id.Anchor,
                    [new QuickFix("Merge", [new Merge(me.Id.Anchor, other.Id.Anchor)])], false));
            }
        }
    }

    private static void ItemIssues(PlanBase b, ImmutableArray<Issue>.Builder issues)
    {
        foreach (var i in b.Items)
        {
            var src = i.Time.Source;
            if (i.Flags.HasFlag(ItemFlags.CheckDate))
            {
                var window = src is TimeSource.Mtime or TimeSource.ExifWithOffset ? 60 : 75;
                issues.Add(new Issue(IssueSeverity.Warning, IssueCode.CheckDate,
                    $"Check date: {PlanText.LocalTime(i.Time.CaptureUtc, i.Time.TzId)} is within {window} min of midnight ({src})",
                    i.Raw.Unit.Id, [], false));
            }
            if (i.Flags.HasFlag(ItemFlags.ClockNotSet))
                issues.Add(new Issue(IssueSeverity.Warning, IssueCode.ClockNotSet,
                    $"Clock not set: {i.Time.CaptureUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC ({src})",
                    i.Raw.Unit.Id, [], false));
        }
    }

    private static void PlanIssues(PlanBase b, SessionFlags flags, Roots roots, HashSet<ItemId> included, ImmutableArray<Issue>.Builder issues)
    {
        var s = b.Scan.Settings;
        var ledger = b.Scan.Ledger;

        if (b.Clock.MismatchItems > 0 && b.Items.Any(i => i.Flags.HasFlag(ItemFlags.ClockMismatch)))
            issues.Add(new Issue(IssueSeverity.Info, IssueCode.ClockMismatch, ClockMismatchMessage(b), null, [], false));

        bool Eq(string a, string c) => PathCmp.Equals(a.TrimEnd('\\'), c.TrimEnd('\\'));
        var photoIncluded = b.Items.Any(i => i.Raw.Kind != ItemKind.Video && included.Contains(i.Raw.Unit.Id));
        var missing = roots.Unavailable.ToList();
        if (roots.VideoMissing && !missing.Any(r => Eq(r, s.VideoRoot))) missing.Insert(0, s.VideoRoot);
        foreach (var r in missing)
        {
            var blocking = Eq(r, s.VideoRoot) || (Eq(r, s.PhotoRoot) && photoIncluded);
            issues.Add(new Issue(blocking ? IssueSeverity.Blocking : IssueSeverity.Warning, IssueCode.RootMissing,
                                 $"{r} is not available; its items are unticked", null, [], false));
        }
        if (!s.RootsConfirmed)
            issues.Add(new Issue(IssueSeverity.Blocking, IssueCode.RootsUnconfirmed,
                                 "Confirm the video and photo folders (settings were recovered)", null, [new QuickFix("Open Settings", [])], false));

        foreach (var p in ledger.ParseIssues)
        {
            var msg = $"Ledger line {p.File}:{p.Line} can't be read: {p.Reason}";
            issues.Add(flags.LedgerIssuesAccepted
                ? new Issue(IssueSeverity.Warning, IssueCode.LedgerParseIssue, msg, null, [], true)
                : new Issue(IssueSeverity.Blocking, IssueCode.LedgerParseIssue, msg, null, [new QuickFix("Accept and continue", [])], false));
        }

        var st = ledger.Status;
        var display = $@"{Path.GetFileName(s.VideoRoot.TrimEnd('\\'))}\.uas-sort";
        if (st.State == LedgerFolderState.CloudOnly || !st.CloudOnlyFiles.IsDefaultOrEmpty)
            issues.Add(new Issue(IssueSeverity.Blocking, IssueCode.LedgerCloudOnly, $"Set {display} to Always keep on this device", null,
                                 [new QuickFix("Keep on this device", [])], false));
        if (st.State == LedgerFolderState.Unwritable || (st.Exists && !st.Writable))
            issues.Add(new Issue(IssueSeverity.Blocking, IssueCode.LedgerUnwritable, $"Can't write the history file in {st.Folder}", null, [], false));
        if (st.State == LedgerFolderState.NotPinned)
            issues.Add(new Issue(IssueSeverity.Warning, IssueCode.LedgerNotPinned, $"Set {display} to Always keep on this device", null,
                                 [new QuickFix("Keep on this device", [])], false));
        if (st.State is LedgerFolderState.Missing or LedgerFolderState.Empty)
            issues.Add(new Issue(IssueSeverity.Info, IssueCode.LedgerNoHistory, "No history yet; this offload starts it", null, [], false));

        if (b.Items.All(i => i.Newness is Imported or Decided))
            issues.Add(new Issue(IssueSeverity.Blocking, IssueCode.NothingNew,
                                 "Nothing new on this card: every clip and photo is already in your library or history", null, [], false));
    }

    private static string ClockMismatchMessage(PlanBase b)
    {
        var c = b.Scan.Clock;
        var flagged = b.Items.Where(i => i.Flags.HasFlag(ItemFlags.ClockMismatch)).ToList();
        var first = flagged[0];
        var offset = first.Raw.DroneStamp is { } stamp ? c.OffsetAt(stamp, first.Time.TzId) : c.Modal ?? TimeSpan.Zero;
        var clock = PlanText.Offset(offset) + (c.Mode == ClockMode.Zone && c.ZoneId is { } z ? $" ({z})" : "");
        var sites = flagged.Select(i => $"{PlanText.ZoneName(i.Time.TzId)} ({PlanText.Offset(PlanText.Zone(i.Time.TzId).GetUtcOffset(i.Time.CaptureUtc))})")
                           .Distinct(StringComparer.Ordinal);
        return $"Drone clock is set to {clock}, but footage on this card was shot in {string.Join(", ", sites)}. "
             + "Dates here use local time at each site. To fix the drone clock: RC 2 → Settings → System → Date & time → turn off the "
             + "network-provided time zone and set the zone for where you are flying. Network time zones can be wrong on ship or hotel "
             + "Wi-Fi, and the RC keeps the last one it saw until it reconnects.";
    }
}
