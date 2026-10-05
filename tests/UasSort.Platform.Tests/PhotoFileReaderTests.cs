namespace UasSort.Platform.Tests;

public sealed class PhotoFileReaderTests
{
    [Fact]
    public void OpenRead_ReadsPictureOffloadAndLightroomFiles_ButNothingElse()
    {
        using var env = new TestEnv();
        var lightroom = env.Temp.Sub("lightroom");
        var dng = new SyntheticDngBuilder().Build();
        string Put(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, dng);
            return path;
        }
        var photo = Put(Path.Join(env.PhotoRoot, "A.DNG"));
        var member = Put(Path.Join(env.PhotoRoot, "001_0042", "PANO_0001.DNG"));
        var library = Put(Path.Join(lightroom, "2026", "Damian_20260601_001.dng"));
        var catalog = Put(Path.Join(lightroom, "LR_Catalog", "Lightroom Catalog.lrcat"));
        var video = Put(Path.Join(env.VideoRoot, "DJI_20250601120000_0001_D.MP4"));
        var reader = new GuardedPhotoFileReader(env.Settings with { LightroomFolder = lightroom }, env.AppData, TestEnv.Machine, env.Facts);

        foreach (var ok in new[] { photo, member, library })
        {
            using var s = reader.OpenRead(ok);
            Assert.Equal(SyntheticDngBuilder.Pano0001Dto, StillProbe.Read(s).DtoNaive);
        }
        Assert.Throws<UnsafeIoException>(() => reader.OpenRead(catalog));
        Assert.Throws<UnsafeIoException>(() => reader.OpenRead(video));
        using (reader.OpenRead(photo))
        {
            File.Move(photo, photo + ".moved");                                   // FileShare.Delete: an open read never blocks a move
        }
    }

    [Fact]
    public void FolderFacts_SeesOnlyExistingFolders()
    {
        using var env = new TestEnv();
        Assert.True(FolderFacts.Exists(env.PhotoRoot));
        Assert.False(FolderFacts.Exists(env.Temp.File("x.txt")));
        Assert.False(FolderFacts.Exists(Path.Join(env.Temp.Path, "missing")));
        Assert.False(FolderFacts.Exists(""));
    }
}
