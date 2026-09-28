namespace UasSort.Testing;

/// <summary>The standard fake PC every fake-FS test uses (no real user paths are touched).</summary>
public static class FakeLayout
{
    public const string VideoRoot = @"C:\Users\u\OneDrive\Pictures\UAS Videos";
    public const string PhotoRoot = VideoRoot + @"\Picture Offload";
    public const string AppDataDir = @"C:\Users\u\AppData\Local\uas-sort";
    public const string CardRoot = @"E:\";
    public const string Machine = "DESKTOP-A";
    public const string SystemVolumeRoot = @"C:\";
    public static readonly CardIdentity CardId = new(0x1A2B3C4D, null, "exFAT", 256_060_514_304);
    public static readonly CardSpace CardSpace = new(12_400_000_000, 256_060_514_304, 131_072);

    public static GuardContext Context(string? cardRoot = CardRoot, IEnumerable<string>? newFolderDirs = null,
                                       IEnumerable<string>? previousPhotoRoots = null)
        => new(VideoRoot, PhotoRoot, [.. previousPhotoRoots ?? []], cardRoot, AppDataDir, Machine,
               (newFolderDirs ?? []).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase),
               ImmutableHashSet.Create<string>(StringComparer.OrdinalIgnoreCase),
               ImmutableHashSet.Create<string>(StringComparer.OrdinalIgnoreCase),
               SystemVolumeRoot, false, null);

    public static Settings Settings(string videoRoot = VideoRoot, string photoRoot = PhotoRoot, IEnumerable<string>? previousPhotoRoots = null)
        => new(1, videoRoot, photoRoot, [.. previousPhotoRoots ?? []], 50, 1, StoredClockMode.Zone, "America/New_York", true,
               new MapSettings("streets", "https://tiles.openfreemap.org/styles/liberty", "https://tiles.openfreemap.org/styles/dark",
                   "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}",
                   ImmutableDictionary<string, string>.Empty),
               new LayoutSettings(380, 0.45), true);

    public static FakeFileSystem NewFileSystem()
    {
        var fs = new FakeFileSystem(Context());
        fs.AddDirectory(VideoRoot);
        fs.AddDirectory(PhotoRoot);
        fs.AddDirectory(AppDataDir);
        fs.AddCardVolume(CardRoot, CardId, CardSpace);
        return fs;
    }
}
