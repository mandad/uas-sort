// src/UasSort.Core/Time/ClockModel.Conversion.cs
namespace UasSort.Core;

// Drone-clock conversions on the learned clock; the record's primary declaration (and its doc comment) is Part 02's Model/Clock.cs.
public sealed partial record ClockModel
{
    // siteZoneId: the zone that SiteLocal (or Setting with SettingMode SiteLocal) converts through, chosen by the caller (§6.1);
    // ignored by the other modes
    public (DateTime Utc, TimeSource Src)? ToUtc(DateTime droneStamp, string? siteZoneId)
        => ClockConversion.ToUtc(this, droneStamp, siteZoneId);

    public TimeSpan OffsetAt(DateTime droneStamp, string? siteZoneId)   // the drone clock's UTC offset at that stamp (§6.5)
        => ClockConversion.OffsetAt(this, droneStamp, siteZoneId);
}
