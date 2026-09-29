namespace UasSort.Platform.Tests;

public sealed class PathFactsTests
{
    private readonly PathFacts _facts = new();

    [Fact]
    public void Canonical_MissingTail_KeepsNamesUnderCanonicalParent()
    {
        using var t = new TempDir();
        var c = _facts.Canonical(Path.Join(t.Path, "a", "b", "c.MP4"));
        Assert.Equal(Path.Join(_facts.Canonical(t.Path), "a", "b", "c.MP4"), c);
    }

    [Fact]
    public void Canonical_ReturnsOnDiskCase_AndNoPrefix()
    {
        using var t = new TempDir();
        var d = t.Sub("MixedCase");
        var c = _facts.Canonical(d.ToUpperInvariant());
        Assert.EndsWith(@"\MixedCase", c, StringComparison.Ordinal);
        Assert.DoesNotContain(@"\\?\", c, StringComparison.Ordinal);
        Assert.Equal(_facts.Canonical(d), c);
    }

    [Fact]
    public void Canonical_VolumeRootKeepsSeparator()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        Assert.Equal(root.ToUpperInvariant(), _facts.Canonical(root).ToUpperInvariant());
        Assert.EndsWith(@"\", _facts.Canonical(root), StringComparison.Ordinal);
    }

    [Fact]
    public void Canonical_ResolvesJunctionIntoLedgerFolder()
    {
        using var t = new TempDir();
        var ledger = t.Sub("video", ".uas-sort");
        File.WriteAllText(Path.Join(ledger, "ledger-A.jsonl"), "");
        var link = Path.Join(t.Path, "link");
        Assert.Equal(0, Cmd.Run($"mklink /J \"{link}\" \"{ledger}\""));
        Assert.Equal(_facts.Canonical(Path.Join(ledger, "ledger-A.jsonl")),
                     _facts.Canonical(Path.Join(link, "ledger-A.jsonl")));
        Assert.EndsWith(@"\video\.uas-sort\ledger-A.jsonl", _facts.Canonical(Path.Join(link, "ledger-A.jsonl")), StringComparison.Ordinal);
    }

    [Fact]
    public void Canonical_ResolvesSubstDrive()
    {
        using var t = new TempDir();
        var ledger = t.Sub("video", ".uas-sort");
        var letter = Cmd.FreeDriveLetter();
        Assert.Equal(0, Cmd.Run($"subst {letter}: \"{ledger}\""));
        try
        {
            Assert.Equal(_facts.Canonical(Path.Join(ledger, "ledger-A.jsonl")),
                         _facts.Canonical($@"{letter}:\ledger-A.jsonl"));
        }
        finally { Cmd.Run($"subst {letter}: /D"); }
    }

    [Fact]
    public void InSyncRoot_IsFalseForTemp()
    {
        using var t = new TempDir();
        Assert.False(_facts.InSyncRoot(_facts.Canonical(t.Path)));
    }

    [Fact]
    public void SyncRoots_AreFullyQualified()
    {
        foreach (var r in _facts.SyncRoots()) Assert.True(Path.IsPathFullyQualified(r), r);
    }

    [Fact]
    public void KnownFolders_AreFullyQualified()
    {
        Assert.True(Path.IsPathFullyQualified(KnownFolders.Pictures()));
        Assert.EndsWith(@"\uas-sort", KnownFolders.AppDataDir(), StringComparison.OrdinalIgnoreCase);
        Assert.Matches(@"^[A-Za-z]:\\$", KnownFolders.SystemVolumeRoot());
    }

    [Fact]
    public void TryGetAttributes_MissingIsNull_InvalidNameIsError()
    {
        using var t = new TempDir();
        Assert.Null(Kernel32.TryGetAttributes(Path.Join(t.Path, "none.bin"), out var e1));
        Assert.Equal(Kernel32.ERROR_FILE_NOT_FOUND, e1);
        Assert.Null(Kernel32.TryGetAttributes(Path.Join(t.Path, "bad|name"), out var e2));
        Assert.Equal(Kernel32.ERROR_INVALID_NAME, e2);
        var f = t.File("x.bin", attributes: FileAttributes.Hidden);
        Assert.True((Kernel32.TryGetAttributes(f, out _)!.Value & (uint)FileAttributes.Hidden) != 0);
    }
}
