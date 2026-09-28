namespace UasSort.Cli;

public static class Program
{
    private const string Usage =
        "usage: uas-sort-cli plan --card <path> [--video-root <path>] [--photo-root <path>] [--radius-mi <5..100>] "
        + "[--gap-days <0..7>] [--settings <path>] [--json] [--expect <expected.json>]";

    public static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        Console.Error.WriteLine(Usage);
        return 2;
    }
}
