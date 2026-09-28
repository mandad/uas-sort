using System.Text.Json;
using System.Xml.Linq;
using UasSort.Testing;

namespace UasSort.Core.Tests.Build;

/// <summary>Guards the build configuration of Ref §2.1, §2.4, §2.5 against drift.</summary>
public sealed class BuildConfigTests
{
    private static readonly string[] ExpectedPins =
    [
        "CommunityToolkit.Mvvm=8.4.2",
        "CommunityToolkit.WinUI.Controls.SettingsControls=8.3.260402-preview2",
        "CommunityToolkit.WinUI.Controls.Sizers=8.3.260402-preview2",
        "GeoTimeZone=6.1.0",
        "MetadataExtractor=2.9.3",
        "Microsoft.CodeAnalysis.BannedApiAnalyzers=5.6.0",
        "Microsoft.Extensions.TimeProvider.Testing=10.10.0",
        "Microsoft.Web.WebView2=1.0.4191.47",
        "Microsoft.Windows.SDK.BuildTools=10.0.28000.2705",
        "Microsoft.WindowsAppSDK.Foundation=2.3.12",
        "Microsoft.WindowsAppSDK.InteractiveExperiences=2.1.9",
        "Microsoft.WindowsAppSDK.WinUI=2.3.9",
        "System.IO.Hashing=11.0.0-rc.1.26425.128",
        "xunit.v3.mtp-v2=4.0.1",
    ];

    private static readonly string[] ForbiddenProperties = ["InvariantGlobalization", "UseNls"];

    [Fact]
    public void GlobalJson_PinsRc1SdkAndMicrosoftTestingPlatform()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(RepoPaths.Of("global.json")));
        var sdk = doc.RootElement.GetProperty("sdk");
        Assert.Equal("11.0.100-rc.1.26425.128", sdk.GetProperty("version").GetString());
        Assert.Equal("latestFeature", sdk.GetProperty("rollForward").GetString());
        Assert.True(sdk.GetProperty("allowPrerelease").GetBoolean());
        Assert.Equal("Microsoft.Testing.Platform", doc.RootElement.GetProperty("test").GetProperty("runner").GetString());
    }

    [Fact]
    public void GlobalJson_IsTheOnlyOneInTheBuildTree()
    {
        var extra = RepoPaths.EnumerateFiles("global.json", "src", "tests", "tools").Select(RepoPaths.Relative).ToList();
        Assert.Empty(extra);
    }

    [Fact]
    public void DirectoryPackagesProps_PinsExactlyTheApprovedVersions()
    {
        var doc = XDocument.Load(RepoPaths.Of("Directory.Packages.props"));
        var pins = doc.Descendants("PackageVersion")
            .Select(e => $"{(string?)e.Attribute("Include")}={(string?)e.Attribute("Version")}")
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.Equal(ExpectedPins, pins);
    }

    [Fact]
    public void EveryProject_UsesCentralVersions_AndNeverTheFullWindowsAppSdk()
    {
        foreach (var csproj in RepoPaths.EnumerateFiles("*.csproj", "src", "tests"))
        {
            var doc = XDocument.Load(csproj);
            foreach (var reference in doc.Descendants("PackageReference"))
            {
                var name = (string?)reference.Attribute("Include");
                Assert.NotEqual("Microsoft.WindowsAppSDK", name);
                Assert.Null(reference.Attribute("Version"));
                Assert.Null(reference.Attribute("VersionOverride"));
            }
        }
    }

    [Fact]
    public void DirectoryBuildProps_EnforcesAnalysisAndWarnings()
    {
        var doc = XDocument.Load(RepoPaths.Of("Directory.Build.props"));
        var global = doc.Root!.Elements("PropertyGroup")
            .Where(g => g.Attribute("Condition") is null)
            .Elements()
            .Where(e => e.Attribute("Condition") is null)
            .GroupBy(e => e.Name.LocalName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Value.Trim(), StringComparer.Ordinal);
        Assert.Equal("enable", global["Nullable"]);
        Assert.Equal("enable", global["ImplicitUsings"]);
        Assert.Equal("11.0-recommended", global["AnalysisLevel"]);
        Assert.Equal("true", global["TreatWarningsAsErrors"]);
        Assert.Equal("true", global["ManagePackageVersionsCentrally"]);
    }

    [Fact]
    public void NoBuildFile_SetsInvariantGlobalizationOrUseNls()
    {
        var files = RepoPaths.EnumerateFiles("*.csproj", "src", "tests")
            .Append(RepoPaths.Of("Directory.Build.props"));
        foreach (var file in files)
        {
            var doc = XDocument.Load(file);
            foreach (var property in ForbiddenProperties)
            {
                Assert.Empty(doc.Descendants(property));
            }
        }
    }

    [Fact]
    public void Core_HasNoProjectReferences_AndCliNeverReferencesReviewOrApp()
    {
        var core = XDocument.Load(RepoPaths.Of("src/UasSort.Core/UasSort.Core.csproj"));
        Assert.Empty(core.Descendants("ProjectReference"));

        var cli = XDocument.Load(RepoPaths.Of("src/UasSort.Cli/UasSort.Cli.csproj"));
        var cliRefs = cli.Descendants("ProjectReference").Select(e => (string?)e.Attribute("Include") ?? "").ToList();
        Assert.DoesNotContain(cliRefs, r => r.Contains("UasSort.Review", StringComparison.Ordinal));
        Assert.DoesNotContain(cliRefs, r => r.Contains("UasSort.App", StringComparison.Ordinal));
    }

    [Fact]
    public void Solution_ListsEveryProjectExceptTheBannedApiProbe()
    {
        var slnx = XDocument.Load(RepoPaths.Of("uas-sort.slnx"));
        var listed = slnx.Descendants("Project").Select(p => (string)p.Attribute("Path")!).ToHashSet(StringComparer.Ordinal);
        var onDisk = RepoPaths.EnumerateFiles("*.csproj", "src", "tests").Select(RepoPaths.Relative).ToList();

        const string probe = "tests/UasSort.BannedApi.Probe/UasSort.BannedApi.Probe.csproj";
        Assert.DoesNotContain(probe, listed);
        var missing = onDisk.Where(p => p != probe && !listed.Contains(p)).Order(StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0, "missing from uas-sort.slnx: " + string.Join(", ", missing));
    }
}
