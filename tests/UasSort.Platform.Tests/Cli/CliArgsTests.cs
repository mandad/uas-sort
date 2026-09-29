using System.Globalization;
using UasSort.Cli;

namespace UasSort.Platform.Tests.Cli;

public sealed class CliArgsTests
{
    [Fact]
    public void Minimal_PlanWithCard()
    {
        Assert.True(CliArgs.TryParse(["plan", "--card", @"E:\"], out var a, out var error), error);
        Assert.Equal(new CliArgs(@"E:\", null, null, null, null, null, false, null), a);
    }

    [Fact]
    public void AllOptions_AreRead()
    {
        string[] args = ["plan", "--card", @"E:\", "--video-root", @"C:\v", "--photo-root", @"C:\p", "--radius-mi", "25.5",
                         "--gap-days", "3", "--settings", @"C:\s.json", "--json", "--expect", @"C:\e.json"];
        Assert.True(CliArgs.TryParse(args, out var a, out var error), error);
        Assert.Equal(new CliArgs(@"E:\", @"C:\v", @"C:\p", 25.5, 3, @"C:\s.json", true, @"C:\e.json"), a);
    }

    [Fact]
    public void Radius_IsParsedWithTheInvariantCulture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.True(CliArgs.TryParse(["plan", "--card", "E:", "--radius-mi", "50.5"], out var a, out var error), error);
            Assert.Equal(50.5, a.RadiusMiles);
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }

    [Theory]
    [InlineData("5", 5.0)]
    [InlineData("100", 100.0)]
    [InlineData("50", 50.0)]
    public void Radius_InRange(string value, double expected)
    {
        Assert.True(CliArgs.TryParse(["plan", "--card", "E:", "--radius-mi", value], out var a, out var error), error);
        Assert.Equal(expected, a.RadiusMiles);
    }

    [Theory]
    [InlineData("4.9")]
    [InlineData("100.1")]
    [InlineData("abc")]
    [InlineData("NaN")]
    public void Radius_OutOfRange_IsAnError(string value)
    {
        Assert.False(CliArgs.TryParse(["plan", "--card", "E:", "--radius-mi", value], out _, out var error));
        Assert.Contains("--radius-mi", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("7", 7)]
    public void GapDays_InRange(string value, int expected)
    {
        Assert.True(CliArgs.TryParse(["plan", "--card", "E:", "--gap-days", value], out var a, out var error), error);
        Assert.Equal(expected, a.GapDays);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("8")]
    [InlineData("1.5")]
    public void GapDays_OutOfRange_IsAnError(string value)
    {
        Assert.False(CliArgs.TryParse(["plan", "--card", "E:", "--gap-days", value], out _, out var error));
        Assert.Contains("--gap-days", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(new string[0], "missing command")]
    [InlineData(new[] { "cleanup", "--card", "E:" }, "unknown command")]
    [InlineData(new[] { "plan" }, "--card <path> is required")]
    [InlineData(new[] { "plan", "--card" }, "--card needs a value")]
    [InlineData(new[] { "plan", "--card", "--json" }, "--card needs a value")]
    [InlineData(new[] { "plan", "--card", "E:", "--card", "F:" }, "--card given twice")]
    [InlineData(new[] { "plan", "--card", "E:", "--json", "--json" }, "--json given twice")]
    [InlineData(new[] { "plan", "--card", "E:", "--delete" }, "unknown argument '--delete'")]
    public void BadArguments_AreErrors(string[] args, string expectedError)
    {
        Assert.False(CliArgs.TryParse(args, out var a, out var error));
        Assert.Null(a);
        Assert.Contains(expectedError, error, StringComparison.Ordinal);
    }

    [Fact]
    public void Usage_ListsEveryOption()
    {
        foreach (var option in new[] { "--card", "--video-root", "--photo-root", "--radius-mi", "--gap-days", "--settings", "--json", "--expect" })
            Assert.Contains(option, CliArgs.Usage, StringComparison.Ordinal);
    }
}
