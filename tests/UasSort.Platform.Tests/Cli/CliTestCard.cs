namespace UasSort.Platform.Tests.Cli;

internal static class CliTestCard
{
    public static readonly GeoPoint Zachar = new(57.5368, -153.7484);
    public const int FileCount = 5;

    public static readonly string[] Clips =
        ["DJI_20260927140127_0123_D.MP4", "DJI_20260927140144_0124_D.MP4", "DJI_20260927142416_0148_D.MP4"];

    /// <summary>Materialises the card under cardRoot (which must be under %TEMP%\uas-sort-test-*).</summary>
    public static void Write(string cardRoot)
    {
        var card = new FakeCardWriter(cardRoot);
        card.AddDjiVideo("DJI_001", new DateTime(2026, 9, 27, 14, 1, 27), 123, Zachar);
        card.AddDjiVideo("DJI_001", new DateTime(2026, 9, 27, 14, 1, 44), 124, Zachar);
        card.AddDjiVideo("DJI_001", new DateTime(2026, 9, 27, 14, 24, 16), 148, Zachar);
        card.AddFile("DCIM/DJI_001/DJI_20260927140127_0123_D.LRF", new byte[512]);
        card.AddFile("MISC/FC9113.db", "FC9113"u8.ToArray());
        card.Write();
    }
}
