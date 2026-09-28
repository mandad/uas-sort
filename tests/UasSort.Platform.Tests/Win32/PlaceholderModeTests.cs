using UasSort.Platform.Win32;

namespace UasSort.Platform.Tests.Win32;

/// <summary>Ref §4.3/§4.4: RtlSetProcessPlaceholderCompatibilityMode(PHCM_EXPOSE_PLACEHOLDERS) at process start.</summary>
public sealed class PlaceholderModeTests
{
    [Fact]
    public void ExposePlaceholders_Succeeds_AndLeavesTheProcessInExposeMode()
    {
        var previous = PlaceholderMode.ExposePlaceholders();
        Assert.True(previous >= 0, $"PHCM error {previous}");
        Assert.Equal(PlaceholderMode.PhcmExposePlaceholders, PlaceholderMode.ExposePlaceholders());
    }
}
