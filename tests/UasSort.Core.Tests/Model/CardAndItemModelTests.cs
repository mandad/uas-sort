// tests/UasSort.Core.Tests/Model/CardAndItemModelTests.cs
namespace UasSort.Core.Tests.Model;

public class CardAndItemModelTests
{
    private static readonly CardIdentity Card = new(0x1A2B3C4D, null, "exFAT", 256_060_514_304);

    [Fact]
    public void DraftKey_UsesTheVolumeSerialForADetectedCard()
        => Assert.Equal("vol-1A2B3C4D", new CardSource(@"E:\", Card, false, false).DraftKey);

    [Fact]
    public void DraftKey_HashesTheLowercaseRootForABrowsedFolder()
    {
        var a = new CardSource(@"C:\Temp\Card", null, true, false).DraftKey;
        var b = new CardSource(@"c:\temp\card", null, true, false).DraftKey;
        Assert.Equal(a, b);
        Assert.Matches("^dir-[0-9a-f]{16}$", a);
    }

    [Fact]
    public void SessionKey_ToleratesTwoSecondsForTheSameSerial()
    {
        var t = new DateTime(2026, 9, 27, 17, 59, 28, DateTimeKind.Utc);
        var s = new SessionKey("SER1", t);
        Assert.True(s.SameSession(new SessionKey("SER1", t.AddSeconds(0.8))));
        Assert.False(s.SameSession(new SessionKey("SER1", t.AddSeconds(3))));
        Assert.False(s.SameSession(new SessionKey("SER2", t)));
    }

    [Fact]
    public void GpsProbe_IsAnExhaustiveUnion()
    {
        GpsProbe fix = new GpsFix(new GeoPoint(57.5504421, -153.738973), 12.5, 1, GpsSource.DjmdModelTable, "gps.lat");
        GpsProbe none = new NoFix(NoFixReason.AllProbedSamplesZero);
        Assert.Equal("fix", Describe(fix));
        Assert.Equal("none:AllProbedSamplesZero", Describe(none));

        static string Describe(GpsProbe p) => p switch
        {
            GpsFix => "fix",
            NoFix n => $"none:{n.Reason}",
        };
    }

    [Fact]
    public void MediaUnit_And_Newness_AreClosedHierarchies()
    {
        var mp4 = new CardEntry("DCIM/DJI_001/DJI_20260927140627_0128_D.MP4", 89_612_345,
            new DateTime(2026, 9, 27, 18, 8, 1, DateTimeKind.Utc), new DateTime(2026, 9, 27, 18, 6, 27, DateTimeKind.Utc),
            new DateTime(2026, 9, 27, 18, 8, 1, DateTimeKind.Utc), 0x20, EntryClass.Video, null);
        MediaUnit unit = new VideoUnit(new ItemId(mp4.RelPath), mp4, false);
        Assert.Equal(ItemKind.Video, KindOf(unit));

        Newness n = new Imported(Evidence.LibraryNameSize, null, "same name and size in the library");
        Assert.Equal("Imported", NameOf(n));

        static ItemKind KindOf(MediaUnit u) => u switch
        {
            VideoUnit => ItemKind.Video,
            PhotoUnit => ItemKind.Photo,
            SetUnit => ItemKind.Set,
        };
        static string NameOf(Newness x) => x switch
        {
            IsNew => "IsNew",
            Imported => "Imported",
            Decided => "Decided",
            ProbablyImported => "ProbablyImported",
            Conflict => "Conflict",
        };
    }

    [Fact]
    public void CardSourceCheck_IsAnExhaustiveUnion()
    {
        CardSourceCheck ok = new SourceOk(new CardSource(@"E:\", Card, false, true));
        CardSourceCheck refused = new SourceRefused("No DCIM folder here");
        Assert.True(IsOk(ok));
        Assert.False(IsOk(refused));

        static bool IsOk(CardSourceCheck c) => c switch { SourceOk => true, SourceRefused => false };
    }

    [Fact]
    public void ItemFlags_HaveTheSpecifiedBitValues()
    {
        Assert.Equal(256, (int)ItemFlags.ClockMismatch);
        Assert.Equal(1 | 2, (int)(ItemFlags.NoGps | ItemFlags.Truncated));
    }
}
