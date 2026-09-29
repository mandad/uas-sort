using System.Text.Json;
using UasSort.Cli;

namespace UasSort.Platform.Tests.Cli;

public sealed class ExpectedFileTests
{
    private static readonly string[] FolderProperties = ["relPath", "clips", "target"];
    private static readonly string[] RequiredFolderProperties = ["relPath", "clips"];

    private static string Acceptance(string name) => RepoPaths.Of($"tests/acceptance/{name}");

    [Fact]
    public void TheCheckedInExample_Parses()
    {
        var e = ExpectedFile.Parse(File.ReadAllText(Acceptance("first-card-expected.example.json")));
        Assert.Equal(2, e.Folders.Count);
        Assert.Equal(@"2026\2026-10\2026-10-04 Nome Roads", e.Folders[0].RelPath);
        Assert.Equal(3, e.Folders[0].Clips.Count);
        Assert.Equal(GroupTargetKind.NewFolder, e.Folders[1].Target);
    }

    [Fact]
    public void TheSchema_DescribesTheSameProperties()
    {
        using var schema = JsonDocument.Parse(File.ReadAllText(Acceptance("first-card-expected.schema.json")));
        var folder = schema.RootElement.GetProperty("properties").GetProperty("folders").GetProperty("items");
        Assert.Equal(FolderProperties, folder.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(RequiredFolderProperties, folder.GetProperty("required").EnumerateArray().Select(e => e.GetString()!).ToArray());
        Assert.Equal(Enum.GetNames<GroupTargetKind>(),
                     folder.GetProperty("properties").GetProperty("target").GetProperty("enum").EnumerateArray().Select(e => e.GetString()!).ToArray());
    }

    [Fact]
    public void CommentsAndTrailingCommas_AreAccepted()
    {
        var e = ExpectedFile.Parse("""
            // written by hand
            { "folders": [ { "relPath": "2026\\2026-10\\2026-10-04 Nome Roads", "clips": [ "DJI_20261004163012_0151_D.MP4", ], }, ] }
            """);
        Assert.Single(e.Folders);
        Assert.Null(e.Folders[0].Target);
    }

    [Theory]
    [InlineData("not json", "not valid JSON")]
    [InlineData("{ }", "no folders")]
    [InlineData("{ \"folders\": [] }", "no folders")]
    [InlineData("{ \"folders\": [ { \"relPath\": \" \", \"clips\": [\"a.MP4\"] } ] }", "empty relPath")]
    [InlineData("{ \"folders\": [ { \"relPath\": \"x\", \"clips\": [] } ] }", "no clips")]
    [InlineData("{ \"folders\": [ { \"relPath\": \"x\", \"clips\": [\"DCIM/DJI_001/a.MP4\"] } ] }", "bare file name")]
    [InlineData("{ \"folders\": [ { \"relPath\": \"x\", \"clips\": [\"a.MP4\"], \"target\": \"Merge\" } ] }", "not valid JSON")]
    public void InvalidFiles_AreFormatErrors(string json, string expected)
    {
        var ex = Assert.Throws<FormatException>(() => ExpectedFile.Parse(json));
        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
    }
}
