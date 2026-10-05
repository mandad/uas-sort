// tests/UasSort.Core.Tests/Config/LightroomFolderSettingsTests.cs
namespace UasSort.Core.Tests.Config;

public sealed class LightroomFolderSettingsTests
{
    private const string Pictures = @"C:\Users\u\Pictures";

    [Fact]
    public void Settings_LightroomFolder_DefaultsToNull_AndRoundTrips()
    {
        Settings d = SettingsDefaults.Derive(Pictures);
        Assert.Null(d.LightroomFolder);
        var s = d with { RootsConfirmed = true, LightroomFolder = @"X:\Photos\Lightroom" };
        string json = SettingsCodec.Serialize(s);
        Assert.Contains("\"lightroomFolder\": \"X:\\\\Photos\\\\Lightroom\"", json, StringComparison.Ordinal);
        SettingsParse back = SettingsCodec.Parse(json);
        Assert.Null(back.Error);
        Assert.Equal(@"X:\Photos\Lightroom", back.Settings!.LightroomFolder);
        Assert.Equal(json, SettingsCodec.Serialize(back.Settings));
    }

    [Fact]
    public void Settings_Parse_AFileWithoutTheKey_HasNoLightroomFolder()
    {
        SettingsParse p = SettingsCodec.Parse("""
            { "schema": 1, "videoRoot": "C:\\V", "photoRoot": "C:\\V\\Picture Offload", "radiusMiles": 50, "gapDays": 1,
              "droneClockMode": "Zone", "droneClockZone": "America/New_York", "copyJpgTwin": true, "rootsConfirmed": true }
            """);
        Assert.Null(Assert.IsType<Settings>(p.Settings).LightroomFolder);
    }

    [Theory]
    [InlineData(@"C:\\V\\Picture Offload\\LR")]
    [InlineData(@"D:\\LR_Catalog")]
    [InlineData(@"C:\\V\\Exports")]
    [InlineData(@"C:\\")]
    public void Settings_Parse_AnInvalidLightroomFolder_IsDropped_NotFatal(string jsonPath)
    {
        SettingsParse p = SettingsCodec.Parse($$"""
            { "schema": 1, "videoRoot": "C:\\V", "photoRoot": "C:\\V\\Picture Offload", "radiusMiles": 50, "gapDays": 1,
              "droneClockMode": "Zone", "droneClockZone": "America/New_York", "copyJpgTwin": true, "rootsConfirmed": true,
              "lightroomFolder": "{{jsonPath}}" }
            """);
        Assert.Null(p.Error);
        Assert.Null(Assert.IsType<Settings>(p.Settings).LightroomFolder);
    }
}
