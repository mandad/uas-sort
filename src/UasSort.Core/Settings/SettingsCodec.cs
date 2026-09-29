using System.Text.Json;

namespace UasSort.Core.Config;

public sealed record SettingsParse(Settings? Settings, string? Error);

/// <summary>settings.json to and from <see cref="Settings"/> (Ref §11). There is no ledgerDir key because there is no such property.</summary>
public static class SettingsCodec
{
    public const int Schema = 1;

    public static string Serialize(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return JsonSerializer.Serialize(settings, CoreJsonContext.Default.Settings);
    }

    public static SettingsParse Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        Settings? s;
        try
        {
            s = JsonSerializer.Deserialize(json, CoreJsonContext.Default.Settings);
        }
        catch (JsonException ex)
        {
            return new SettingsParse(null, "unreadable: " + ex.Message);
        }
        catch (NotSupportedException ex)
        {
            return new SettingsParse(null, "unreadable: " + ex.Message);
        }
        if (s is null) return new SettingsParse(null, "empty settings");

        string? error =
            s.Schema != Schema ? $"unsupported schema {s.Schema}"
            : string.IsNullOrWhiteSpace(s.VideoRoot) || string.IsNullOrWhiteSpace(s.PhotoRoot) ? "missing videoRoot or photoRoot"
            : double.IsNaN(s.RadiusMiles) || s.RadiusMiles < 5 || s.RadiusMiles > 100 ? "radiusMiles outside 5..100"
            : s.GapDays is < 0 or > 7 ? "gapDays outside 0..7"
            : !Enum.IsDefined(s.DroneClockMode) ? "bad droneClockMode"
            : string.IsNullOrWhiteSpace(s.DroneClockZone) ? "missing droneClockZone"
            : null;
        if (error is not null) return new SettingsParse(null, error);

        return new SettingsParse(s with
        {
            PreviousPhotoRoots = s.PreviousPhotoRoots.IsDefault ? [] : s.PreviousPhotoRoots,
            Map = s.Map ?? SettingsDefaults.Map(),
            Layout = s.Layout ?? SettingsDefaults.Layout(),
        }, null);
    }
}
