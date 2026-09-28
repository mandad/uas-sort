namespace UasSort.App;

/// <summary>
/// Command line of uas-sort.exe. <c>--selftest --result &lt;path&gt; [--only &lt;check&gt;[,&lt;check&gt;…]]</c> is the contract
/// tools/run-selftest.ps1 and deploy.ps1 rely on (Part 13); <c>--single-instance-mutex</c> forces the named-mutex
/// fallback for testing. Any other argument is rejected. Plain C# only (no WinUI types): Platform.Tests compiles this
/// file through a linked Compile item. Part 11 extends it in place.
/// </summary>
internal sealed record LaunchOptions(bool SelfTest, string ResultPath, IReadOnlySet<string>? Only, bool ForceMutex, DateTime ProcessStartUtc)
{
    public const string SelfTestFlag = "--selftest";
    public const string ResultFlag = "--result";
    public const string OnlyFlag = "--only";
    public const string ForceMutexFlag = "--single-instance-mutex";
    public const string DefaultResultFileName = "uas-sort-selftest-result.json";

    /// <exception cref="ArgumentException">An unknown argument, or --result / --only without a value.</exception>
    public static LaunchOptions Parse(IReadOnlyList<string> args, DateTime processStartUtc)
    {
        ArgumentNullException.ThrowIfNull(args);
        var selfTest = false;
        var forceMutex = false;
        var resultPath = Path.Join(Path.GetTempPath(), DefaultResultFileName);
        HashSet<string>? only = null;
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case SelfTestFlag:
                    selfTest = true;
                    break;
                case ResultFlag:
                    resultPath = Path.GetFullPath(ValueAfter(args, ref i));
                    break;
                case OnlyFlag:
                    only = ValueAfter(args, ref i)
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToHashSet(StringComparer.Ordinal);
                    if (only.Count == 0)
                    {
                        throw new ArgumentException(OnlyFlag + " needs at least one check name", nameof(args));
                    }

                    break;
                case ForceMutexFlag:
                    forceMutex = true;
                    break;
                default:
                    throw new ArgumentException("unknown argument: " + args[i], nameof(args));
            }
        }

        return new LaunchOptions(selfTest, resultPath, only, forceMutex, processStartUtc);
    }

    private static string ValueAfter(IReadOnlyList<string> args, ref int i)
    {
        var flag = args[i];
        if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException(flag + " needs a value", nameof(args));
        }

        return args[++i];
    }
}
