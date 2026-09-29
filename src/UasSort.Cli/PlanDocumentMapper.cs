using System.Globalization;

namespace UasSort.Cli;

/// <summary>Plan → the v1 JSON document of Ref §4.5.</summary>
internal static class PlanDocumentMapper
{
    public static PlanDocument Map(Plan plan, CardIdentity? identity)
    {
        var scan = plan.Base.Scan;
        var inventory = scan.Inventory;
        string videoRoot = scan.Settings.VideoRoot;
        var items = plan.Base.Items.ToDictionary(i => i.Raw.Unit.Id);

        var groups = plan.Groups.Select(g => MapGroup(g, plan, items, videoRoot)).ToList();

        var photoDays = plan.Base.PhotoDays.Select(d => new PhotoDayJson(
            d.Date, d.TzId, d.Items.Length,
            d.Items.Count(id => items.TryGetValue(id, out var it) && it.Newness is IsNew),
            d.Items.Count(id => items.TryGetValue(id, out var it) && it.Newness is ProbablyImported),
            d.Reason)).ToList();

        var setUnits = inventory.Units.OfType<SetUnit>().ToDictionary(u => u.Id);
        var sets = plan.Base.Sets.OrderBy(kv => kv.Key.CardRelPath, StringComparer.Ordinal)
            .Select(kv => new SetJson(kv.Key.CardRelPath, kv.Value.FolderName, kv.Value.Resolution,
                                      setUnits.TryGetValue(kv.Key, out var unit) ? unit.Members.Length : kv.Value.MembersToCopy.Length))
            .ToList();

        var other = inventory.Entries.Where(e => e.Class is EntryClass.Unknown or EntryClass.Skip)
            .Select(e => new OtherJson(e.RelPath, e.Class, e.Rule)).ToList();

        var issues = plan.Issues.Select(i => new IssueJson(i.Severity, i.Code, i.Anchor?.CardRelPath, i.Message, i.RequiresAckAtPreflight))
            .ToList();

        var clock = plan.Base.Clock;
        return new PlanDocument(
            1,
            new CardJson(inventory.Source.Root,
                         identity is { } id
                             ? new IdentityJson(id.VolumeSerial.ToString("X8", CultureInfo.InvariantCulture), id.Label, id.FileSystem, id.TotalBytes)
                             : null,
                         inventory.CameraModel, inventory.Entries.Length, inventory.InventoryHash),
            new SettingsJson(scan.Settings.VideoRoot, scan.Settings.PhotoRoot, plan.Tuning.RadiusMiles, plan.Tuning.GapDays),
            new ClockJson(clock.Mode, clock.ZoneId, clock.SampleCount, new MismatchJson(clock.MismatchItems, clock.MismatchSiteZones)),
            plan.Base.WatermarkUtc is { } w ? DateTime.SpecifyKind(w, DateTimeKind.Utc) : null,
            groups, photoDays, sets, other, issues);
    }

    private static GroupJson MapGroup(VideoGroup g, Plan plan, Dictionary<ItemId, Item> items, string videoRoot)
    {
        (GroupTargetKind Kind, string? RelPath, Confidence? Confidence, string? Why) target = g.Target switch
        {
            NewFolder n => (GroupTargetKind.NewFolder, n.RelPath, null, null),
            Append a => (GroupTargetKind.Append, Rel(videoRoot, a.Folder.FullPath), a.Confidence, a.Why),
            AlreadyImported ai => (GroupTargetKind.AlreadyImported, Rel(videoRoot, ai.Folder.FullPath), null, null),
            NothingToCopy nt => (GroupTargetKind.NothingToCopy, null, null, nt.Summary),
            SkipGroup => (GroupTargetKind.SkipGroup, null, null, null),
        };

        var boundary = plan.Boundaries.FirstOrDefault(b => b.Right == g.Id);
        var videos = g.Videos.Select(id =>
        {
            var item = items[id];
            return new VideoJson(id.CardRelPath, Status(item.Newness), plan.Included.Contains(id),
                                 DateTime.SpecifyKind(item.Time.CaptureUtc, DateTimeKind.Utc), item.Time.LocalDate,
                                 item.Time.Source, Flags(item.Flags));
        }).ToList();
        var daySplits = g.DaySplits.Select(s => new DaySplitJson(s.FirstOfDay.CardRelPath, s.From, s.To,
                                                                 s.Apart is { } d ? Math.Round(d.Miles, 1) : null, s.Emphasised)).ToList();
        var groupIssues = plan.Issues.Where(i => i.Anchor is { } a && g.Videos.Contains(a)).Select(i => i.Code).Distinct().ToList();

        return new GroupJson(g.Id.Anchor.CardRelPath, target.Kind, target.RelPath, target.Confidence, target.Why, g.Start, g.End,
            boundary is null ? null : new BoundaryJson(boundary.Cause, boundary.Jump is { } j ? Math.Round(j.Miles, 1) : null,
                                                       Math.Round(boundary.Gap.TotalHours, 1), boundary.DayGap),
            videos, daySplits, groupIssues);
    }

    private static NewnessStatus Status(Newness n) => n switch
    {
        IsNew => NewnessStatus.New,
        Imported => NewnessStatus.Imported,
        Decided => NewnessStatus.Decided,
        ProbablyImported => NewnessStatus.ProbablyImported,
        Conflict => NewnessStatus.Conflict,
    };

    private static List<string> Flags(ItemFlags flags) =>
        Enum.GetValues<ItemFlags>().Where(f => f != ItemFlags.None && flags.HasFlag(f)).Select(f => f.ToString()).ToList();

    private static string Rel(string videoRoot, string fullPath) => Path.GetRelativePath(videoRoot, fullPath);
}
