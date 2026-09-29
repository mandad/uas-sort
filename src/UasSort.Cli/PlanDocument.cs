using System.Text.Json;
using System.Text.Json.Serialization;

namespace UasSort.Cli;

/// <summary>The GroupTarget case of a group, by name (Ref §3).</summary>
internal enum GroupTargetKind { NewFolder, Append, AlreadyImported, NothingToCopy, SkipGroup }

/// <summary>The Newness case of an item, by name; IsNew is written "New".</summary>
internal enum NewnessStatus { New, Imported, Decided, ProbablyImported, Conflict }

/// <summary>`uas-sort-cli plan --json`, schema v1 (Ref §4.5). Property order and names are the contract.</summary>
internal sealed record PlanDocument(int V, CardJson Card, SettingsJson Settings, ClockJson Clock, DateTime? WatermarkUtc,
    IReadOnlyList<GroupJson> Groups, IReadOnlyList<PhotoDayJson> PhotoDays, IReadOnlyList<SetJson> Sets,
    IReadOnlyList<OtherJson> Other, IReadOnlyList<IssueJson> Issues);

internal sealed record CardJson(string Root, IdentityJson? Identity, string? Model, int Files, string InventoryHash);

internal sealed record IdentityJson(string Serial, string? Label, string Fs, long TotalBytes);

internal sealed record SettingsJson(string VideoRoot, string PhotoRoot, double RadiusMiles, int GapDays);

internal sealed record ClockJson(ClockMode Mode, string? Zone, int Samples, MismatchJson Mismatch);

internal sealed record MismatchJson(int Items, IReadOnlyList<string> SiteZones);

internal sealed record GroupJson(string Id, GroupTargetKind Target, string? RelPath, Confidence? Confidence, string? Why,
    DateOnly Start, DateOnly End, BoundaryJson? BoundaryBefore, IReadOnlyList<VideoJson> Videos,
    IReadOnlyList<DaySplitJson> DaySplits, IReadOnlyList<IssueCode> Issues);

internal sealed record BoundaryJson(BoundaryCause Cause, double? JumpMiles, double GapHours, int DayGap);

internal sealed record VideoJson(string Id, NewnessStatus Status, bool Included, DateTime CaptureUtc, DateOnly LocalDate,
    TimeSource TimeSource, IReadOnlyList<string> Flags);

internal sealed record DaySplitJson(string FirstOfDay, DateOnly From, DateOnly To, double? ApartMiles, bool Emphasised);

internal sealed record PhotoDayJson(DateOnly Date, string Tz, int Units, int New, int ProbablyImported, string Reason);

internal sealed record SetJson(string Id, string Folder, SetResolution Resolution, int Members);

internal sealed record OtherJson(string RelPath, EntryClass Class, string? Rule);

internal sealed record IssueJson(IssueSeverity Severity, IssueCode Code, string? Anchor, string Message, bool RequiresAck);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true,
                             WriteIndented = true, PropertyNameCaseInsensitive = true,
                             ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(PlanDocument))]
internal sealed partial class CliJsonContext : JsonSerializerContext;
