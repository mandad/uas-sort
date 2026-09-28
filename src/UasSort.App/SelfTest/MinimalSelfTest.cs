using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.WinUI.Controls;
using GeoTimeZone;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Jpeg;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.Storage.Pickers;
using UasSort.App.Pages;
using UasSort.Core.Json;
using UasSort.Core.StackProof;
using UasSort.Platform.Stores;
using UasSort.Platform.Win32;
using Windows.UI.Text;

namespace UasSort.App.SelfTest;

/// <summary>
/// The stack-proof step's minimal selftest (Ref §13, §14 step 1): probe page renders, WebView2 reaches the virtual
/// host, MetadataExtractor Stream read of the embedded stack-exif.jpg, GeoTimeZone lookup, closed-record JSON
/// round-trip; plus the named mutex and the sandbox deletion. With --only, only the named checks run (the page still
/// loads and the sandbox is still deleted). Part 11 Task 11.4 deletes it for SelfTestRunner, keeping the command
/// line and the result shape.
/// </summary>
internal static class MinimalSelfTest
{
    internal const string StackExifResource = "UasSort.App.SelfTest.stack-exif.jpg";

    /// <summary>Every check, in run order; --only selects by these exact (case-sensitive) names.</summary>
    private static readonly string[] CheckNames =
        ["probePage", "folderPicker", "webView2", "metadataExtractor", "geoTimeZone", "closedRecordJson", "namedMutex", "sandboxDeleted"];

    public static async Task RunAndExitAsync(MainWindow window, LaunchOptions options, SelfTestSandbox sandbox)
    {
        var checks = new List<SelfTestCheck>();
        double firstFrameMs = -1;
        bool Selected(string name) => options.Only is null || options.Only.Contains(name);
        try
        {
            var unknown = (options.Only ?? Enumerable.Empty<string>())
                .Where(n => !CheckNames.Contains(n, StringComparer.Ordinal))
                .Order(StringComparer.Ordinal)
                .ToList();
            if (unknown.Count > 0)
            {
                checks.Add(SelfTestCheck.Fail("harness", "unknown check name(s) in --only: " + string.Join(", ", unknown)));
            }

            firstFrameMs = await window.FirstFrameMs.WaitAsync(TimeSpan.FromSeconds(20));
            var page = await WaitForPageAsync(window);
            if (Selected("probePage"))
            {
                checks.Add(await RunAsync("probePage", () => CheckProbePageAsync(page)));
            }

            if (Selected("folderPicker"))
            {
                checks.Add(Run("folderPicker", () => CheckFolderPicker(window)));
            }

            if (Selected("webView2"))
            {
                checks.Add(await RunAsync("webView2", () => page.MapRoundTrip.WaitAsync(TimeSpan.FromSeconds(20))));
            }

            if (Selected("metadataExtractor"))
            {
                checks.Add(Run("metadataExtractor", CheckMetadataExtractor));
            }

            if (Selected("geoTimeZone"))
            {
                checks.Add(Run("geoTimeZone", CheckGeoTimeZone));
            }

            if (Selected("closedRecordJson"))
            {
                checks.Add(Run("closedRecordJson", CheckClosedRecordJson));
            }

            if (Selected("namedMutex"))
            {
                checks.Add(Run("namedMutex", CheckNamedMutex));
            }

            page.CloseWebView();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            // WebView2 releases its user data folder asynchronously after Close(); allow ~12 s (4 x TryDelete's 3 s).
            var deleted = false;
            for (var attempt = 0; attempt < 4 && !deleted; attempt++)
            {
                deleted = sandbox.TryDelete();
            }

            if (Selected("sandboxDeleted"))
            {
                checks.Add(deleted
                    ? SelfTestCheck.Pass("sandboxDeleted", sandbox.Root)
                    : SelfTestCheck.Fail("sandboxDeleted", "could not delete " + sandbox.Root));
            }
        }
#pragma warning disable CA1031 // selftest: every failure becomes a failed check in the result file
        catch (Exception ex)
#pragma warning restore CA1031
        {
            checks.Add(SelfTestCheck.Fail("harness", Describe(ex)));
        }

        Finish(options, checks, firstFrameMs);
    }

    internal static void FailAndExit(LaunchOptions options, SelfTestSandbox? sandbox, Exception ex)
    {
        sandbox?.TryDelete();
        Finish(options, [SelfTestCheck.Fail("harness", "unhandled: " + Describe(ex))], -1);
    }

    private static void Finish(LaunchOptions options, List<SelfTestCheck> checks, double firstFrameMs)
    {
        var ok = checks.TrueForAll(c => c.Status != "fail");
        var result = new SelfTestResult(ok, Math.Round(firstFrameMs), checks);
        try
        {
            SelfTestSandbox.WriteResult(
                options.ResultPath, JsonSerializer.SerializeToUtf8Bytes(result, SelfTestJsonContext.Default.SelfTestResult));
        }
#pragma warning disable CA1031 // a result that cannot be written is a failed selftest, reported by the exit code
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Trace.WriteLine("could not write the selftest result: " + ex);
            ok = false;
        }

        Environment.Exit(ok ? 0 : 1);
    }

    private static async Task<StackProbePage> WaitForPageAsync(MainWindow window)
    {
        for (var i = 0; i < 100; i++)
        {
            if (window.ProbePage is { IsLoaded: true } page)
            {
                return page;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("StackProbePage did not load within 10 s");
    }

    private static async Task<string> CheckProbePageAsync(StackProbePage page)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var texts = Descendants(page).OfType<TextBlock>().ToList();
            var video = texts.Find(t => t.Text == "Anvil Mountain");
            var photo = texts.Find(t => t.Text == "Zachar Bay");
            var splitter = Descendants(page).OfType<GridSplitter>().FirstOrDefault();
            if (video is not null && photo is not null && splitter is not null)
            {
                Require(video.FontStyle == FontStyle.Normal && photo.FontStyle == FontStyle.Italic,
                    "the template selector chose the wrong template");
                Require(page.RootCardHeader == "Video root", "SettingsCard header was " + page.RootCardHeader);
                return "ItemsView realised both templates (x:Bind text), SettingsCard and GridSplitter rendered";
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("probe page items, SettingsCard or GridSplitter not realised within 5 s");
    }

    private static string CheckFolderPicker(MainWindow window)
    {
        var picker = new FolderPicker(window.AppWindow.Id) { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        Require(picker.SuggestedStartLocation == PickerLocationId.PicturesLibrary, "FolderPicker lost its start location");
        return string.Create(CultureInfo.InvariantCulture, $"constructed with owner window {window.AppWindow.Id.Value} (not shown)");
    }

    private static string CheckMetadataExtractor()
    {
        using var stream = typeof(MinimalSelfTest).Assembly.GetManifestResourceStream(StackExifResource)
            ?? throw new InvalidOperationException("missing embedded resource " + StackExifResource);
        var directories = JpegMetadataReader.ReadMetadata(stream);
        var dto = directories.OfType<ExifSubIfdDirectory>()
            .Select(d => d.GetString(ExifDirectoryBase.TagDateTimeOriginal))
            .FirstOrDefault(s => s is not null);
        Require(dto == "2026:09:27 14:01:27", $"DateTimeOriginal was '{dto}'");
        var gps = directories.OfType<GpsDirectory>().FirstOrDefault() ?? throw new InvalidOperationException("no GPS directory");
        Require(gps.TryGetGeoLocation(out var location), "GPS directory has no location");
        Require(Math.Abs(location.Latitude - 57.5368) < 1e-6 && Math.Abs(location.Longitude + 153.7484) < 1e-6,
            string.Create(CultureInfo.InvariantCulture, $"GPS was {location.Latitude},{location.Longitude}"));
        return string.Create(CultureInfo.InvariantCulture, $"DTO {dto}; GPS {location.Latitude:F4},{location.Longitude:F4}");
    }

    private static string CheckGeoTimeZone()
    {
        var tz = TimeZoneLookup.GetTimeZone(57.5368, -153.7484).Result;
        Require(tz == "America/Anchorage", "GeoTimeZone returned " + tz);
        var info = TimeZoneInfo.FindSystemTimeZoneById(tz);
        Require(info.BaseUtcOffset == TimeSpan.FromHours(-9), "ICU base offset was " + info.BaseUtcOffset);
        return "Zachar Bay -> America/Anchorage (ICU base offset -09:00)";
    }

    private static string CheckClosedRecordJson()
    {
        List<ProbeOutcome> outcomes =
            [new ProbeCopied("2026/2026-09/DJI_0001.MP4", 1024), new ProbeSkipped("dup"), new ProbeConflict("DJI_0002.MP4", 7)];
        var json = JsonSerializer.Serialize(outcomes, StackProofJsonContext.Default.ListProbeOutcome);
        var back = JsonSerializer.Deserialize(json, StackProofJsonContext.Default.ListProbeOutcome);
        Require(back is not null && back.SequenceEqual(outcomes), "round trip changed the values: " + json);
        Require(json.StartsWith("""[{"t":"copied",""", StringComparison.Ordinal), "discriminator missing: " + json);
        return json;
    }

    private static string CheckNamedMutex()
    {
        var name = @"Local\uas-sort-selftest-" + Guid.NewGuid().ToString("N");
        using var first = NamedMutexLock.TryAcquire(name) ?? throw new InvalidOperationException("first acquire failed");
        using var second = NamedMutexLock.TryAcquire(name);
        Require(second is null, "a second acquire succeeded while the first was held");
        return "held; second acquire refused";
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var pending = new Stack<DependencyObject>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            yield return node;
            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < count; i++)
            {
                pending.Push(VisualTreeHelper.GetChild(node, i));
            }
        }
    }

    private static SelfTestCheck Run(string name, Func<string> check)
    {
        try
        {
            return SelfTestCheck.Pass(name, check());
        }
#pragma warning disable CA1031 // selftest: every failure becomes a failed check
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return SelfTestCheck.Fail(name, Describe(ex));
        }
    }

    private static async Task<SelfTestCheck> RunAsync(string name, Func<Task<string>> check)
    {
        try
        {
            return SelfTestCheck.Pass(name, await check());
        }
#pragma warning disable CA1031 // selftest: every failure becomes a failed check
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return SelfTestCheck.Fail(name, Describe(ex));
        }
    }

    private static string Describe(Exception ex) => ex.GetType().Name + ": " + ex.Message;

    private static void Require([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
