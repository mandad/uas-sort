// src/UasSort.Core/Settings/SettingsLoadPolicy.cs
using System.Globalization;
using UasSort.Core;

namespace UasSort.Core.Config;

public sealed record SettingsLoadDecision(SettingsLoad Load, string? MoveCorruptTo);

/// <summary>What ISettingsStore.Load does with the file it found (Ref §11 Settings row, §4.5 read-only load).</summary>
public static class SettingsLoadPolicy
{
    public static SettingsLoadDecision Decide(string settingsPath, string? fileText, bool readOnly, Settings derivedDefaults,
                                              DateTime nowUtc, Func<RunRoots?> rootsFromLastRun)
    {
        ArgumentNullException.ThrowIfNull(derivedDefaults);
        ArgumentNullException.ThrowIfNull(rootsFromLastRun);
        Settings defaults = derivedDefaults with { RootsConfirmed = false };
        if (fileText is null) return new SettingsLoadDecision(new SettingsLoad(defaults, false, null, null), null);

        SettingsParse parsed = SettingsCodec.Parse(fileText);
        if (parsed.Settings is { } settings) return new SettingsLoadDecision(new SettingsLoad(settings, false, null, null), null);

        if (readOnly) return new SettingsLoadDecision(new SettingsLoad(defaults, true, null, null), null);

        string corrupt = settingsPath + ".corrupt-" + nowUtc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return new SettingsLoadDecision(new SettingsLoad(defaults, true, corrupt, rootsFromLastRun()), corrupt);
    }
}
