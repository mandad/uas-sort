// tests/UasSort.Core.Tests/Library/MemberStartResolverTests.cs
using UasSort.Core.Library;
using UasSort.Core;

namespace UasSort.Core.Tests.Library;

public sealed class MemberStartResolverTests
{
    private static readonly ClockModel EasternZone =
        new(ClockMode.Zone, "America/New_York", [], null, StoredClockMode.Zone, "America/New_York");
    private static readonly ClockModel SiteLocal =
        new(ClockMode.SiteLocal, null, [], null, StoredClockMode.SiteLocal, "America/New_York");

    private static DateTime Utc(int y, int mo, int d, int h, int mi, int s) => new(y, mo, d, h, mi, s, DateTimeKind.Utc);

    [Fact]
    public void MemberStart_ZoneClock_ConvertsTheFilenameStamp()   // Zachar Bay 0148: the watermark clip
        => Assert.Equal(new MemberStart(Utc(2026, 9, 27, 18, 24, 16), false),
                        MemberStartResolver.Resolve("DJI_20260927142416_0148_D.MP4", Utc(2026, 9, 27, 18, 25, 46), null, EasternZone));

    [Fact]
    public void MemberStart_SiteLocal_UsesTheFolderLedgerZone()
        => Assert.Equal(new MemberStart(Utc(2026, 9, 27, 18, 1, 27), false),
                        MemberStartResolver.Resolve("DJI_20260927100127_0123_D.MP4", Utc(2026, 9, 27, 18, 2, 57), "America/Anchorage", SiteLocal));

    [Fact]
    public void MemberStart_SiteLocal_WithoutLedgerZone_UsesTheStoredZone()
        => Assert.Equal(new MemberStart(Utc(2026, 9, 27, 14, 1, 27), false),
                        MemberStartResolver.Resolve("DJI_20260927100127_0123_D.MP4", Utc(2026, 9, 27, 14, 2, 57), null, SiteLocal));

    [Theory]
    [InlineData(18, 0, 0, true)]      // mtime before the stamp start → mtime, flagged
    [InlineData(20, 24, 17, true)]    // more than 2 h after → mtime, flagged
    [InlineData(20, 24, 16, false)]   // exactly 2 h after → the stamp stands
    [InlineData(18, 24, 16, false)]   // equal → the stamp stands
    public void MemberStart_MtimeCheck(int h, int mi, int s, bool fromMtime)
    {
        DateTime mtime = Utc(2026, 9, 27, h, mi, s);
        MemberStart r = MemberStartResolver.Resolve("DJI_20260927142416_0148_D.MP4", mtime, null, EasternZone);
        Assert.Equal(fromMtime, r.FromMtime);
        Assert.Equal(fromMtime ? mtime : Utc(2026, 9, 27, 18, 24, 16), r.Utc);
    }

    [Theory]
    [InlineData("MAX_0061.MP4")]                       // Autel: always mtime
    [InlineData("Council edit.mp4")]
    [InlineData("DJI_20261399000000_0001_D.MP4")]      // impossible stamp
    public void MemberStart_NonDjiNames_UseMtime(string name)
        => Assert.Equal(new MemberStart(Utc(2022, 3, 27, 15, 1, 0), true),
                        MemberStartResolver.Resolve(name, Utc(2022, 3, 27, 15, 1, 0), null, EasternZone));

    [Theory]
    [InlineData("dji_20260927142416_0148_d.mp4")]
    [InlineData("DJI_20260927142416_0148_D_Tele.MP4")]
    public void MemberStart_StampVariants(string name)
    {
        Assert.True(MemberStartResolver.TryStamp(name, out DateTime stamp));
        Assert.Equal(new DateTime(2026, 9, 27, 14, 24, 16), stamp);
        Assert.Equal(DateTimeKind.Unspecified, stamp.Kind);
    }
}
