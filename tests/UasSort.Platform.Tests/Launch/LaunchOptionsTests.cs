using UasSort.App;

namespace UasSort.Platform.Tests.Launch;

/// <summary>
/// uas-sort.exe command line (Part 13 contract): --selftest --result &lt;path&gt; [--only a,b] and --single-instance-mutex;
/// anything else is rejected.
/// </summary>
public sealed class LaunchOptionsTests
{
    private static readonly DateTime Start = new(2026, 9, 27, 14, 0, 0, DateTimeKind.Utc);
    private static readonly string[] SelfTestWithResult = ["--selftest", "--result", @"C:\uas-sort-test\r.json"];
    private static readonly string[] SelfTestWithOnly = ["--selftest", "--only", "probePage, webView2"];
    private static readonly string[] ExpectedOnly = ["probePage", "webView2"];
    private static readonly string[] MutexOnly = ["--single-instance-mutex"];

    [Fact]
    public void Parse_NoArguments_IsANormalLaunch_WithTheTempDefaultResultPath()
    {
        var options = LaunchOptions.Parse([], Start);
        Assert.False(options.SelfTest);
        Assert.False(options.ForceMutex);
        Assert.Null(options.Only);
        Assert.Equal(Path.Join(Path.GetTempPath(), "uas-sort-selftest-result.json"), options.ResultPath);
        Assert.Equal(Start, options.ProcessStartUtc);
    }

    [Fact]
    public void Parse_SelfTestWithResult_TakesTheFullResultPath()
    {
        var options = LaunchOptions.Parse(SelfTestWithResult, Start);
        Assert.True(options.SelfTest);
        Assert.Equal(@"C:\uas-sort-test\r.json", options.ResultPath);
        Assert.Null(options.Only);
    }

    [Fact]
    public void Parse_Only_SplitsCommaSeparatedNames_CaseSensitively()
    {
        var options = LaunchOptions.Parse(SelfTestWithOnly, Start);
        Assert.NotNull(options.Only);
        Assert.Equal(ExpectedOnly, options.Only.Order(StringComparer.Ordinal));
        Assert.DoesNotContain("probepage", options.Only);
    }

    [Fact]
    public void Parse_SingleInstanceMutex_ForcesTheFallback_WithoutSelfTest()
    {
        var options = LaunchOptions.Parse(MutexOnly, Start);
        Assert.True(options.ForceMutex);
        Assert.False(options.SelfTest);
    }

    [Theory]
    [InlineData("--selftest-result")]
    [InlineData("--verbose")]
    [InlineData("stray")]
    public void Parse_UnknownArgument_IsRejected(string argument)
    {
        Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(["--selftest", argument, @"C:\uas-sort-test\r.json"], Start));
    }

    [Theory]
    [InlineData("--result")]
    [InlineData("--only")]
    public void Parse_FlagWithoutValue_IsRejected(string flag)
    {
        Assert.Throws<ArgumentException>(() => LaunchOptions.Parse(["--selftest", flag], Start));
    }
}
