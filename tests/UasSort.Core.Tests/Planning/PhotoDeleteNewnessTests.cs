// tests/UasSort.Core.Tests/Planning/PhotoDeleteNewnessTests.cs
using System.Globalization;
using UasSort.Core.Planning;
using UasSort.Testing.Planning;

namespace UasSort.Core.Tests.Planning;

public sealed class PhotoDeleteNewnessTests
{
    private static readonly DateTime Watermark = new(2026, 9, 27, 18, 24, 16, DateTimeKind.Utc);
    private static readonly DateTime RemovedAt = new(2026, 10, 4, 20, 0, 0, DateTimeKind.Utc);

    private static ItemTime T(RawItem r)
    {
        var utc = Clip.Utc(r.DroneStamp!.Value.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture));
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById("America/Anchorage"));
        return new ItemTime(utc, TimeSource.DroneClockZone, "America/Anchorage", TzSource.Gps, DateOnly.FromDateTime(local), local);
    }

    private static LedgerPhotoDelete Removed(string name, long size, string? set = null)
        => new($"p-{name}", FileKey.Of(name, size), @"X:\Picture Offload\" + name, RemovedAt, null, set, "lightroom", "verify",
               new DateOnly(2026, 9, 30), "DESKTOP-A", "run-p");

    private static Newness Of(RawItem r, params LedgerPhotoDelete[] removed)
    {
        var s = new PlanScenario().Card(r).Build();
        var ledger = s.Ledger with { PhotoDeletes = removed.ToImmutableDictionary(d => d.Key) };
        return NewnessRules.Photo(r.Unit, T(r), s.Library, ledger, null, new HashSet<DateOnly>(), Watermark);
    }

    [Fact]
    public void Rule1_APhotoRemovedFromPictureOffload_IsImported()
    {
        var d = Clip.Dng("20260815200000", 119, Sites.Anvil);
        var imported = Assert.IsType<Imported>(Of(d, Removed(d.Name, d.Bytes)));
        Assert.Equal(Evidence.LedgerVerified, imported.By);
        Assert.Equal("removed from Picture Offload on Oct 4", imported.Why);
    }

    [Fact]
    public void Rule1_ASetCountsOnlyWhenEveryMemberWasRemovedUnderItsSetName()
    {
        var set = Clip.Set("001_0087", "2026-08-15 20:00:00", Sites.Anvil,
            ("PANO_0001.DNG", 13_751_808, new DateTime(2026, 8, 16, 0, 0, 0, DateTimeKind.Utc)),
            ("PANO_0002.DNG", 13_751_900, new DateTime(2026, 8, 16, 0, 0, 2, DateTimeKind.Utc)));
        Assert.IsType<Imported>(Of(set, Removed("PANO_0001.DNG", 13_751_808, "001_0087"), Removed("PANO_0002.DNG", 13_751_900, "001_0087")));
        Assert.IsNotType<Imported>(Of(set, Removed("PANO_0001.DNG", 13_751_808, "001_0087")));
        Assert.IsNotType<Imported>(Of(set, Removed("PANO_0001.DNG", 13_751_808, "002_0001"), Removed("PANO_0002.DNG", 13_751_900, "002_0001")));
    }
}
