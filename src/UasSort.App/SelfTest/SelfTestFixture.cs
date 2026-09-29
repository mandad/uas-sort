// src/UasSort.App/SelfTest/SelfTestFixture.cs
namespace UasSort.App.SelfTest;

/// <summary>The synthetic card and ledger the selftest scans (Ref §13 "built-in fake plan", produced by the real pipeline).</summary>
internal static class SelfTestFixture
{
    public const string ClipA = "DCIM/DJI_001/DJI_20260726035000_0001_D.MP4";   // Jul 25 23:50 AKDT, Anvil
    public const string ClipB = "DCIM/DJI_001/DJI_20260726041000_0002_D.MP4";   // Jul 26 00:10 AKDT, Anvil (day split)
    public const string ClipC = "DCIM/DJI_001/DJI_20260927140127_0003_D.MP4";   // Sep 27 10:01 AKDT, Zachar Bay (63 days later)
    public const string Dng = "DCIM/DJI_001/DJI_20260927140500_0004_D.DNG";     // Sep 27 10:05 AKDT, Zachar Bay
    public const string LedgerFile = "ledger-SELFTEST.jsonl";

    private const string ResourcePrefix = "UasSort.App.SelfTest.";                // Part 01's LogicalName

    private static readonly (string Resource, string CardRel)[] CardFiles =
    [
        ("selftest-0001.mp4", ClipA), ("selftest-0002.mp4", ClipB), ("selftest-0003.mp4", ClipC), ("selftest.dng", Dng),
    ];

    public static Stream Open(string resourceName) =>
        typeof(SelfTestFixture).Assembly.GetManifestResourceStream(ResourcePrefix + resourceName)
        ?? throw new InvalidOperationException("missing embedded selftest asset " + resourceName);

    /// <summary>The card files under the sandbox's CardRoot and the test ledger in VideoRoot\.uas-sort (through SelfTestSandbox.WriteFile).</summary>
    public static void Materialize(SelfTestSandbox s)
    {
        var card = Path.GetRelativePath(s.Root, s.CardRoot);
        var video = Path.GetRelativePath(s.Root, s.VideoRoot);
        foreach (var (resource, rel) in CardFiles)
            s.WriteFile(Path.Join(card, rel.Replace('/', Path.DirectorySeparatorChar)), ReadAll(resource));
        s.WriteFile(Path.Join(video, LedgerPaths.FolderName, LedgerFile), ReadAll("ledger-v1.jsonl"));
    }

    public static byte[] ReadAll(string resourceName)
    {
        using var src = Open(resourceName);
        using var ms = new MemoryStream();
        src.CopyTo(ms);
        return ms.ToArray();
    }
}
