// src/UasSort.App/Services/UiFormat.cs — functions for x:Bind (no converters, no reflection)
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace UasSort.App.Services;

/// <summary>x:Bind helper functions. Each name is unique (x:Bind functions are not overloaded by type).</summary>
public static class UiFormat
{
    private static readonly Dictionary<string, SolidColorBrush> Brushes = [];

    public static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility Hidden(bool value) => value ? Visibility.Collapsed : Visibility.Visible;
    public static Visibility VisibleIfText(string? value) => string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
    public static Visibility VisibleIfAny(int count) => count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility VisibleIfNone(int count) => count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility VisibleIfNotNull(object? value) => value is null ? Visibility.Collapsed : Visibility.Visible;
    public static bool Not(bool value) => !value;
    public static Windows.UI.Text.FontStyle Italic(bool value) => value ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal;
    public static Windows.UI.Text.FontWeight Weight(bool strong) =>
        strong ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
    public static string Count(int value) => value.ToString(System.Globalization.CultureInfo.CurrentCulture);
    public static double ToDouble(int value) => value;

    /// <summary>Rows that can't be used (not a DJI card) are dimmed, not hidden (Ref §9.1 Card).</summary>
    public static double EnabledOpacity(bool enabled) => enabled ? 1.0 : 0.55;

    /// <summary>InfoBarVm, SetupVm and SettingsPageVm severities (Review's InfoSeverity).</summary>
    public static InfoBarSeverity BarSeverity(InfoSeverity s) => s switch
    {
        InfoSeverity.Error => InfoBarSeverity.Error,
        InfoSeverity.Warning => InfoBarSeverity.Warning,
        InfoSeverity.Success => InfoBarSeverity.Success,
        _ => InfoBarSeverity.Informational,
    };

    public static Visibility VisibleIfError(InfoSeverity s) => s == InfoSeverity.Error ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Plan issues (Core's IssueSeverity).</summary>
    public static InfoBarSeverity IssueBarSeverity(IssueSeverity s) => s switch
    {
        IssueSeverity.Blocking => InfoBarSeverity.Error,
        IssueSeverity.Warning => InfoBarSeverity.Warning,
        _ => InfoBarSeverity.Informational,
    };

    /// <summary>IssueVm.Code as shown under the message in the issues flyout.</summary>
    public static string IssueCodeText(IssueCode code) => code.ToString();

    /// <summary>The ledger folder is derived from the video root, never a setting (Ref §9.1 Setup).</summary>
    public static string LedgerFolder(string videoRoot) => LedgerPaths.For(videoRoot);

    /// <summary>"The photo folder is inside the video folder" note (Ref §9.14).</summary>
    public static Visibility VisibleIfInside(string photoRoot, string videoRoot) =>
        photoRoot.Length > 0 && videoRoot.Length > 0 && PathRules.IsStrictlyUnder(photoRoot, videoRoot) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>MapSettings.Base / ReviewVm.MapBase values in the order of the base-map radio buttons.</summary>
    public static readonly IReadOnlyList<string> MapBases = (string[])["streets", "satellite", "none"];

    public static int BaseIndex(string mapBase) => Math.Max(0, MapBases.ToList().IndexOf(mapBase));

    public static string BaseAt(int index) => MapBases[Math.Clamp(index, 0, MapBases.Count - 1)];

    /// <summary>SettingsPageVm.IsSiteLocal as the index of the drone-clock radio buttons (0 site-local, 1 fixed zone).</summary>
    public static int ClockModeIndex(bool siteLocal) => siteLocal ? 0 : 1;

    public static int ModeIndex(CleanupMode mode) => mode == CleanupMode.FreeSpace ? 1 : 0;

    public static int FreeKindIndex(FreeSpaceKind kind) => kind == FreeSpaceKind.FreeUp ? 1 : 0;

    public static bool IsKeep(RowDecision d) => d == RowDecision.Keep;

    public static bool IsDelete(RowDecision d) => d == RowDecision.Delete;

    /// <summary>"#RRGGBB" (MapBridge palette, Ref §9.4) → a cached brush.</summary>
    public static SolidColorBrush Brush(string hex)
    {
        if (Brushes.TryGetValue(hex, out var b)) return b;
        var v = hex.StartsWith('#') ? hex[1..] : hex;
        var c = v.Length == 6
            ? Color.FromArgb(255, Convert.ToByte(v[..2], 16), Convert.ToByte(v[2..4], 16), Convert.ToByte(v[4..6], 16))
            : Color.FromArgb(255, 128, 128, 128);
        return Brushes[hex] = new SolidColorBrush(c);
    }

    public static SolidColorBrush VerdictBrush(VerdictLevel level) => level switch
    {
        VerdictLevel.Safe => Brush("#107C10"),
        VerdictLevel.SafeWithAssumptions => Brush("#9D5D00"),
        _ => Brush("#C42B1C"),
    };
}
