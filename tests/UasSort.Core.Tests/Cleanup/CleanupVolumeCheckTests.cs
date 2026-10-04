// tests/UasSort.Core.Tests/Cleanup/CleanupVolumeCheckTests.cs
namespace UasSort.Core.Tests.Cleanup;

public class CleanupVolumeCheckTests
{
    private const string AppData = @"C:\Users\u\AppData\Local\uas-sort";
    private static readonly DateTime T = new(2026, 9, 27, 18, 0, 0, DateTimeKind.Utc);

    private static Settings S(string video = @"C:\Lib\UAS Videos", string photo = @"C:\Lib\UAS Videos\Picture Offload")
        => new(1, video, photo, [], 50, 1, StoredClockMode.Zone, "America/New_York", true,
               new MapSettings("streets", "https://s", "https://d", "https://t", ImmutableDictionary<string, string>.Empty),
               new LayoutSettings(380, 0.45), true);

    private static VolumeInfo V(string root = @"E:\", string fs = "exFAT", string bus = "Sd", bool removable = true,
                                bool system = false, bool readOnly = false)
        => new(root, new CardIdentity(0x1A2B3C4D, null, fs, 256_060_514_304), "Removable", true, readOnly,
               fs == "NTFS", bus is "Sd" or "Usb", 12_400_000_000, bus, removable, system);

    private static FsEntry F(string root, string rel, bool dir = false)
        => new(root.TrimEnd('\\') +"\\" + rel, rel, dir, dir ? 0 : 10, T, T, T, dir ? 0x10u : 0x20u);

    private static ListingResult Card(string root = @"E:\", bool misc = true, bool idxOnly = false)
    {
        var e = new List<FsEntry> { F(root, "DCIM", true), F(root, @"DCIM\DJI_001", true), F(root, @"DCIM\DJI_001\DJI_20260927140627_0128_D.MP4") };
        if (misc) { e.Add(F(root, "MISC", true)); e.Add(idxOnly ? F(root, @"MISC\IDX", true) : F(root, @"MISC\FC9113.db")); }
        return new ListingResult([.. e], []);
    }

    [Fact]
    public void Sd_exfat_card_with_misc_index_passes()
        => Assert.Null(CleanupVolumeCheck.Refusal(V(), Card(), S(), AppData));

    [Fact]
    public void Usb_reader_with_removable_media_and_idx_folder_passes()
        => Assert.Null(CleanupVolumeCheck.Refusal(V(bus: "Usb", fs: "FAT32"), Card(idxOnly: true), S(), AppData));

    public static TheoryData<string, VolumeInfo, ListingResult, Settings> Refused() => new()
    {
        { "fixed USB exFAT volume holding a card copy", V(bus: "Usb", removable: false), Card(), S() },
        { "NVMe bus", V(bus: "Nvme", removable: false), Card(), S() },
        { "fixed SATA disk", V(bus: "Sata", removable: false), Card(), S() },
        { "fixed disk behind a SCSI or PCIe bridge", V(bus: "Scsi", removable: false), Card(), S() },
        { "a non-root folder", V(root: @"E:\backup\card\"), Card(@"E:\backup\card\"), S() },
        { "listing not taken at the volume root", V(), Card(@"E:\backup\card\"), S() },
        { "NTFS", V(fs: "NTFS"), Card(), S() },
        { "system volume", V(system: true), Card(), S() },
        { "volume holds the photo root", V(), Card(), S(photo: @"E:\Photos") },
        { "volume holds the video root", V(), Card(), S(video: @"e:\UAS Videos") },
        { "no MISC index", V(), Card(misc: false), S() },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void Refused_volumes_get_the_backup_drive_tooltip(string why, VolumeInfo v, ListingResult card, Settings s)
    {
        Assert.NotNull(why);
        Assert.Equal("This doesn't look like a drone card (it may be a backup drive)", CleanupVolumeCheck.Refusal(v, card, s, AppData));
    }

    [Fact]
    public void AppData_on_the_volume_is_refused()
        => Assert.Equal(CleanupVolumeCheck.NotACard, CleanupVolumeCheck.Refusal(V(), Card(), S(), @"E:\AppData\uas-sort"));

    [Fact]
    public void Write_protected_card_gets_the_lock_switch_tooltip()
        => Assert.Equal("The card is write-protected (lock switch)", CleanupVolumeCheck.Refusal(V(readOnly: true), Card(), S(), AppData));

    private static readonly IReadOnlySet<string> NoExcludes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    [Fact] // F7: the button's check gets the root's and MISC's children from a non-recursive lister, RelPaths under the card root
    public void CardListing_FromANonRecursiveLister_FindsTheMiscIndex()
    {
        var fs = FakeLayout.NewFileSystem();
        fs.AddFile(@"E:\DCIM\DJI_001\DJI_20260927140627_0128_D.MP4", 10, T);
        fs.AddFile(@"E:\MISC\FC1.db", 10, T);

        var listing = CleanupVolumeCheck.CardListing(fs, @"E:\");

        Assert.Contains(listing.Entries, e => e.RelPath == @"MISC\FC1.db" && e.FullPath == @"E:\MISC\FC1.db");
        Assert.Null(CleanupVolumeCheck.Refusal(V(), listing, S(), AppData));
        Assert.Equal(CleanupVolumeCheck.NotACard,                                          // the top-level-only listing never passes
                     CleanupVolumeCheck.Refusal(V(), fs.Enumerate(@"E:\", false, NoExcludes), S(), AppData));
    }

    [Fact] // F7: no MISC folder (a card copy) stays refused
    public void CardListing_WithoutMisc_IsNotACard()
    {
        var fs = FakeLayout.NewFileSystem();
        fs.AddFile(@"E:\DCIM\DJI_001\DJI_20260927140627_0128_D.MP4", 10, T);
        Assert.Equal(CleanupVolumeCheck.NotACard, CleanupVolumeCheck.Refusal(V(), CleanupVolumeCheck.CardListing(fs, @"E:\"), S(), AppData));
    }

    // Task U4 (user decision 2026-10-04): rule 4 accepts removable media on any bus (a PCIe card reader reports Scsi or Unknown);
    // Sd and Mmc pass regardless of the flag; fixed media (removable false) on any other bus stays refused.
    [Theory]
    [InlineData("Sd", true)]
    [InlineData("Sd", false)]
    [InlineData("Mmc", true)]
    [InlineData("Mmc", false)]
    [InlineData("Usb", true)]
    [InlineData("Scsi", true)]
    [InlineData("Unknown", true)]
    [InlineData("Sata", true)]
    public void Bus_rule_accepts_removable_media_on_any_bus(string bus, bool removable)
        => Assert.Equal((null, null), CleanupVolumeCheck.Evaluate(V(bus: bus, removable: removable), Card(), S(), AppData));

    [Theory]
    [InlineData("Usb", "rule 4: bus Usb, removable media false")]
    [InlineData("Scsi", "rule 4: bus Scsi, removable media false")]
    [InlineData("Unknown", "rule 4: bus Unknown, removable media false")]
    [InlineData("Sata", "rule 4: bus Sata, removable media false")]
    [InlineData("Nvme", "rule 4: bus Nvme, removable media false")]
    public void Bus_rule_refuses_fixed_media_with_a_rule_4_detail(string bus, string detail)
        => Assert.Equal((CleanupVolumeCheck.NotACard, detail),
                        CleanupVolumeCheck.Evaluate(V(bus: bus, removable: false), Card(), S(), AppData));

    public static TheoryData<string, VolumeInfo, ListingResult, Settings, string, string> Details() => new()
    {
        { "read-only", V(readOnly: true), Card(), S(), CleanupVolumeCheck.WriteProtected, "the volume is read-only" },
        { "non-root", V(root: @"E:\backup\card\"), Card(@"E:\backup\card\"), S(), CleanupVolumeCheck.NotACard,
          @"rule 2: E:\backup\card\ is not a drive root" },
        { "listing elsewhere", V(), Card(@"E:\backup\card\"), S(), CleanupVolumeCheck.NotACard,
          @"rule 2: the card was listed at E:\backup\card\, not at E:\" },
        { "NTFS", V(fs: "NTFS"), Card(), S(), CleanupVolumeCheck.NotACard, "rule 3: file system NTFS" },
        { "system", V(system: true), Card(), S(), CleanupVolumeCheck.NotACard, "rule 5: the system, boot or paging volume" },
        { "photo root", V(), Card(), S(photo: @"E:\Photos"), CleanupVolumeCheck.NotACard, @"rule 5: E:\Photos lies on this volume" },
        { "no index", V(), Card(misc: false), S(), CleanupVolumeCheck.NotACard, @"rule 6: no MISC\FC*.db or MISC\IDX" },
    };

    [Theory]
    [MemberData(nameof(Details))]
    public void Evaluate_names_the_failing_rule_and_its_facts(string why, VolumeInfo v, ListingResult card, Settings s, string refusal, string detail)
    {
        Assert.NotNull(why);
        Assert.Equal((refusal, detail), CleanupVolumeCheck.Evaluate(v, card, s, AppData));
        Assert.Equal(refusal, CleanupVolumeCheck.Refusal(v, card, s, AppData));       // Refusal is Evaluate's first half
    }

    [Fact]
    public void AppData_on_the_volume_names_rule_5()
        => Assert.Equal((CleanupVolumeCheck.NotACard, @"rule 5: E:\AppData\uas-sort lies on this volume"),
                        CleanupVolumeCheck.Evaluate(V(), Card(), S(), @"E:\AppData\uas-sort"));

    [Fact] // F7: a MISC\IDX folder is the other drone index
    public void CardListing_WithMiscIdxFolder_Passes()
    {
        var fs = FakeLayout.NewFileSystem();
        fs.AddFile(@"E:\MISC\IDX\a.idx", 10, T);
        Assert.Null(CleanupVolumeCheck.Refusal(V(), CleanupVolumeCheck.CardListing(fs, @"E:\"), S(), AppData));
    }
}