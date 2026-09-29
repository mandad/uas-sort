// tests/UasSort.Testing/Planning/ItemFactory.cs

namespace UasSort.Testing.Planning;

/// <summary>Builds Items directly (no TimeResolver) for component tests: UTC from mvhd (else stamp + 4 h), zone as given.</summary>
public static class ItemFactory
{
    public static Item Of(RawItem r, Newness? n = null, SessionKey? session = null, string tz = "America/Anchorage")
    {
        var utc = r.Mp4?.MvhdUtc ?? DateTime.SpecifyKind(r.DroneStamp!.Value.AddHours(4), DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById(tz));
        GpsFix? gps = null;
        if (r.Mp4 is { } m && m.First is GpsFix f) gps = f;
        else if (r.Still is { } st && st.Gps is GpsFix g) gps = g;
        var time = new ItemTime(utc, r.Mp4?.MvhdUtc is null ? TimeSource.DroneClockZone : TimeSource.Mvhd, tz, TzSource.Gps,
                                DateOnly.FromDateTime(local), local);
        return new Item(r, time, gps, session, gps is null ? ItemFlags.NoGps : ItemFlags.None, n ?? new IsNew(NewReason.NoMatch, null));
    }

    public static LibraryFolderRef Folder(string rel)
    {
        var leaf = rel[(rel.LastIndexOf('\\') + 1)..];
        return new LibraryFolderRef($@"{PlanScenario.VideoRoot}\{rel}", DateOnly.ParseExact(leaf[..10], "yyyy-MM-dd"), leaf.Length > 11 ? leaf[11..] : "");
    }

    public static Imported ImportedInto(string rel) => new(Evidence.LibraryNameSize, Folder(rel), "test");
}
