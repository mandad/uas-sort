// src/UasSort.Core/Library/MemberStartResolver.cs
using System.Globalization;
using System.Text.RegularExpressions;
using UasSort.Core;

namespace UasSort.Core.Library;

public readonly record struct MemberStart(DateTime Utc, bool FromMtime);

/// <summary>Ref §7.1 member start: filename stamp through the card's ClockModel, checked against mtime.</summary>
public static partial class MemberStartResolver
{
    public static readonly TimeSpan MaxMtimeAfterStart = TimeSpan.FromHours(2);

    [GeneratedRegex(@"^DJI_([0-9]{14})_([0-9]{4})_([A-Z])(?:_[^.]*)?\.([A-Za-z0-9]+)$",
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex DjiName();

    public static bool TryStamp(string fileName, out DateTime droneStamp)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        droneStamp = default;
        Match m = DjiName().Match(fileName);
        return m.Success && DateTime.TryParseExact(m.Groups[1].Value, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                                                   DateTimeStyles.None, out droneStamp);
    }

    public static MemberStart Resolve(string fileName, DateTime mtimeUtc, string? folderLedgerTz, ClockModel clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DateTime mtime = mtimeUtc.Kind == DateTimeKind.Utc ? mtimeUtc : DateTime.SpecifyKind(mtimeUtc, DateTimeKind.Utc);
        if (!TryStamp(fileName, out DateTime stamp)) return new MemberStart(mtime, true);
        if (clock.ToUtc(stamp, folderLedgerTz ?? clock.SettingZoneId) is not { } converted) return new MemberStart(mtime, true);
        DateTime start = converted.Utc;
        if (mtime < start || mtime > start + MaxMtimeAfterStart) return new MemberStart(mtime, true);
        return new MemberStart(start, false);
    }
}
