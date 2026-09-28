namespace UasSort.Platform.Win32;

/// <summary>
/// Makes cloud placeholders visible as such to this process (Ref §4.3: "At process start, Platform calls
/// RtlSetProcessPlaceholderCompatibilityMode(PHCM_EXPOSE_PLACEHOLDERS)"). Program.Main calls it first thing.
/// </summary>
public static class PlaceholderMode
{
    public const sbyte PhcmExposePlaceholders = 2;

    /// <summary>Returns the previous mode (0 = PHCM_APPLICATION_DEFAULT); a negative value is a PHCM_ERROR_* code.</summary>
    public static sbyte ExposePlaceholders() => NativeMethods.RtlSetProcessPlaceholderCompatibilityMode(PhcmExposePlaceholders);
}
