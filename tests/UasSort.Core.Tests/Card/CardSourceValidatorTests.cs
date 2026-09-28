namespace UasSort.Core.Tests.Card;

public class CardSourceValidatorTests
{
    private const string V = FakeLayout.VideoRoot;
    private const string A = FakeLayout.AppDataDir;
    private static readonly DateTime T = new(2026, 9, 27, 18, 8, 1, DateTimeKind.Utc);
    private static readonly Settings S = FakeLayout.Settings(photoRoot: @"F:\Photos", previousPhotoRoots: [@"D:\Old Photos"]);
    private static readonly CardSourceValidator Validator = new();

    private static FakeFileSystem CardAt(params string[] anchors)
    {
        var fs = FakeLayout.NewFileSystem();
        foreach (var a in anchors) fs.AddFile(PathRules.Join(a, @"DCIM\DJI_001\DJI_20260927140627_0128_D.MP4"), 100, T);
        return fs;
    }

    private static VolumeInfo Volume(bool readOnly) => new(@"E:\", FakeLayout.CardId, "Removable", true, readOnly, false, true,
                                                             12_400_000_000, "Sd", true, false);

    private static CardSource Ok(CardSourceCheck c) => c switch
    {
        SourceOk ok => ok.Source,
        SourceRefused r => throw new Xunit.Sdk.XunitException($"refused: {r.Reason}"),
    };

    private static string Refused(CardSourceCheck c) => c switch
    {
        SourceOk ok => throw new Xunit.Sdk.XunitException($"accepted: {ok.Source.Root}"),
        SourceRefused r => r.Reason,
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DetectedVolume_IsNotBrowsed_AndCarriesItsWriteProtection(bool readOnly)
    {
        var src = Ok(Validator.Validate(@"E:\", Volume(readOnly), S, CardAt(@"E:\"), new FakePathFacts(), A));
        Assert.Equal(@"E:\", src.Root);
        Assert.False(src.IsBrowsedFolder);
        Assert.Equal(readOnly, src.IsWriteProtected);
        Assert.Equal(FakeLayout.CardId, src.Identity);
    }

    [Theory]
    [InlineData(@"E:\")]
    [InlineData(@"E:\DCIM")]
    [InlineData(@"E:\DCIM\DJI_001")]
    public void Browse_AnchorsAtTheFolderHoldingDcim_AndIsAlwaysBrowsed(string chosen)
    {
        var src = Ok(Validator.Validate(chosen, null, S, CardAt(@"E:\"), new FakePathFacts(), A));
        Assert.Equal(@"E:\", src.Root);
        Assert.True(src.IsBrowsedFolder);
        Assert.False(src.IsWriteProtected);
        Assert.Null(src.Identity);
    }

    [Fact]
    public void Browse_ACopiedCardInATempFolder_AnchorsThere()
        => Assert.Equal(@"C:\Temp\card",
            Ok(Validator.Validate(@"C:\Temp\card\DCIM\DJI_001", null, S, CardAt(@"C:\Temp\card"), new FakePathFacts(), A)).Root);

    [Fact]
    public void Browse_WithoutDcim_IsRefusedWithTheRightReason()
    {
        var fs = FakeLayout.NewFileSystem();
        fs.AddFile(@"C:\Temp\loose\DJI_20260927140627_0128_D.MP4", 100, T);
        fs.AddFile(@"C:\Temp\empty\notes.txt", 1, T);
        Assert.Equal(CardSourceValidator.PickTopFolder, Refused(Validator.Validate(@"C:\Temp\loose", null, S, fs, new FakePathFacts(), A)));
        Assert.Equal(CardSourceValidator.NoDcim, Refused(Validator.Validate(@"C:\Temp\empty", null, S, fs, new FakePathFacts(), A)));
    }

    [Theory]
    [InlineData(V)]                                       // equals the video root
    [InlineData(V + @"\2026\card copy")]                  // inside it
    [InlineData(@"C:\Users\u\OneDrive\Pictures")]         // contains it
    [InlineData(@"F:\Photos")]                            // equals the photo root
    [InlineData(@"F:\Photos\sub")]
    [InlineData(@"F:\")]
    [InlineData(@"D:\Old Photos")]                        // a previous photo root
    [InlineData(@"D:\Old Photos\x")]
    [InlineData(@"D:\")]
    [InlineData(V + @"\.uas-sort")]                       // the ledger folder, browsed straight into
    [InlineData(V + @"\.uas-sort\x")]
    [InlineData(A)]                                       // app data
    [InlineData(A + @"\drafts")]
    [InlineData(@"C:\Users\u\AppData\Local")]
    public void Overlaps_WithLibraryLedgerOrAppData_AreRefused(string anchor)
        => Assert.Equal(CardSourceValidator.PartOfLibrary,
            Refused(Validator.Validate(anchor, null, S, CardAt(anchor), new FakePathFacts(), A)));

    [Fact]
    public void Overlaps_WithACloudSyncRoot_AreRefused()
    {
        var facts = new FakePathFacts();
        facts.AddSyncRoot(@"G:\Sync");
        Assert.Equal(CardSourceValidator.PartOfLibrary, Refused(Validator.Validate(@"G:\Sync\card", null, S, CardAt(@"G:\Sync\card"), facts, A)));
        Assert.Equal(CardSourceValidator.PartOfLibrary, Refused(Validator.Validate(@"G:\", null, S, CardAt(@"G:\"), facts, A)));
        Assert.Equal(@"H:\", Ok(Validator.Validate(@"H:\", null, S, CardAt(@"H:\"), facts, A)).Root);
    }

    [Fact]
    public void Canonicalisation_SeesThroughSubstAndJunctions()
    {
        var facts = new FakePathFacts();
        facts.AddAlias(@"S:\", V + @"\2026");          // subst S: into the library
        facts.AddAlias(@"C:\Temp\link", A);            // junction into app data
        Assert.Equal(CardSourceValidator.PartOfLibrary, Refused(Validator.Validate(@"S:\", null, S, CardAt(@"S:\"), facts, A)));
        Assert.Equal(CardSourceValidator.PartOfLibrary, Refused(Validator.Validate(@"C:\Temp\link", null, S, CardAt(@"C:\Temp\link"), facts, A)));
    }

    [Fact]
    public void Validation_OpensNothing()
    {
        var fs = CardAt(@"E:\");
        Ok(Validator.Validate(@"E:\DCIM\DJI_001", null, S, fs, new FakePathFacts(), A));
        Assert.Empty(fs.GuardLog);
    }
}
