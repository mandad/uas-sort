// src/UasSort.Core/Cleanup/Photos/ExifStamp.cs
using System.Globalization;

namespace UasSort.Core.Cleanup;

/// <summary>What identifies one shot across Picture Offload and the Lightroom library (spec 2026-10-04 §4): DateTimeOriginal to the
/// second (naive camera clock), its sub-seconds when present, and the camera Model. Lightroom's "Copy as DNG" keeps all three.</summary>
public sealed record ExifStamp(DateTime Second, string? SubSec, string Model)
{
    public static ExifStamp? From(StillInfo? info)
    {
        if (info?.DtoNaive is not { } dto || string.IsNullOrWhiteSpace(info.Model)) return null;
        var second = new DateTime(dto.Ticks - dto.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Unspecified);
        return new ExifStamp(second, info.SubSec, info.Model.Trim());
    }

    public bool SameSecondAndModel(ExifStamp o)
    {
        ArgumentNullException.ThrowIfNull(o);
        return Second == o.Second && string.Equals(Model, o.Model, StringComparison.OrdinalIgnoreCase);
    }

    public bool BothHaveSubSec(ExifStamp o)
    {
        ArgumentNullException.ThrowIfNull(o);
        return SubSec is not null && o.SubSec is not null;
    }

    /// <summary>Same second and Model, and the same sub-seconds when both have them ("5" = "500"; "05" ≠ "5").</summary>
    public bool SameShot(ExifStamp o)
        => SameSecondAndModel(o) && (!BothHaveSubSec(o) || string.Equals(Ticks(SubSec!), Ticks(o.SubSec!), StringComparison.Ordinal));

    private static string Ticks(string digits) => digits.Length >= 7 ? digits[..7] : digits.PadRight(7, '0');

    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"{Second:yyyy-MM-dd HH:mm:ss}{(SubSec is null ? "" : "." + SubSec)} {Model}");
}
