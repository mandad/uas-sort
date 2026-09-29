namespace UasSort.Platform.Tests.Cli;

/// <summary>Ref §4.3: Program.Main calls PlaceholderMode.ExposePlaceholders() as its first statement, before anything else runs.</summary>
public sealed class ProgramTests
{
    [Fact]
    public void Main_ExposesPlaceholdersFirst_ThenSetsTheEncoding_ThenRunsThePlanCommand()
    {
        string source = File.ReadAllText(RepoPaths.Of("src/UasSort.Cli/Program.cs"));
        int main = source.IndexOf("public static int Main(string[] args)", StringComparison.Ordinal);
        Assert.True(main >= 0, "Program.Main not found");
        int bodyStart = source.IndexOf('{', main) + 1;
        int expose = source.IndexOf("PlaceholderMode.ExposePlaceholders()", bodyStart, StringComparison.Ordinal);
        int quiet = source.IndexOf("CriticalErrorMode.FailQuietly()", bodyStart, StringComparison.Ordinal);
        int encoding = source.IndexOf("Console.OutputEncoding", bodyStart, StringComparison.Ordinal);
        int run = source.IndexOf("PlanCommand.RunAsync(", bodyStart, StringComparison.Ordinal);

        Assert.True(expose > 0, "Program.Main must call PlaceholderMode.ExposePlaceholders()");
        Assert.DoesNotContain(";", source[bodyStart..expose], StringComparison.Ordinal);   // it is the first statement
        Assert.True(quiet > expose && quiet < encoding, "critical-error dialogs are turned off right after ExposePlaceholders()");
        Assert.True(encoding > expose, "the console encoding is set after ExposePlaceholders()");
        Assert.True(run > encoding, "PlanCommand.RunAsync runs after ExposePlaceholders() and the encoding");
    }
}
