// tests/UasSort.Testing/Offload/OffloadVolumes.cs
using UasSort.Core;

namespace UasSort.Testing.Offload;

/// <summary>Destination volumes for the post-loop flush tests: C: NTFS fixed, D: exFAT on USB.</summary>
public static class OffloadVolumes
{
    public static readonly VolumeInfo NtfsC = new(@"C:\", new CardIdentity(0xC0C0C0C0, "Windows", "NTFS", 1_000_000_000_000), "Fixed",
        true, false, true, false, 500_000_000_000, "Nvme", false, true);
    public static readonly VolumeInfo ExFatD = new(@"D:\", new CardIdentity(0xD0D0D0D0, "Photos", "exFAT", 2_000_000_000_000), "Fixed",
        true, false, false, false, 1_000_000_000_000, "Usb", false, false);
}
