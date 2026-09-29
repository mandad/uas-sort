using System.Globalization;
using System.Text;

namespace UasSort.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        sbyte previousMode = PlaceholderMode.ExposePlaceholders();   // Ref §4.3: first, before any file-system access
        Console.OutputEncoding = Encoding.UTF8;
        if (previousMode < 0)
            Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"warning: RtlSetProcessPlaceholderCompatibilityMode failed ({previousMode}); cloud placeholders may not show as placeholders"));
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        return PlanCommand.RunAsync(args, Console.Out, Console.Error, new WindowsCliHost(), cts.Token).GetAwaiter().GetResult();
    }
}
