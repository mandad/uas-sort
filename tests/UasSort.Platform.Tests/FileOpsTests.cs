using System.IO.Hashing;

namespace UasSort.Platform.Tests;

public sealed class FileOpsTests
{
    private static readonly DateTime Creation = new(2026, 9, 27, 18, 6, 27, DateTimeKind.Utc);
    private static readonly DateTime Mtime = new(2026, 9, 27, 18, 8, 1, DateTimeKind.Utc);

    private static (GuardedFileOps Ops, string Folder) Setup(TestEnv env, string desc = "Zachar Bay", bool unbuffered = true)
    {
        var year = Path.Join(env.VideoRoot, "2026");
        var month = Path.Join(year, "2026-09");
        var folder = Path.Join(month, $"2026-09-27 {desc}");
        // The run's full NewFolderDirs set, as OffloadCompiler.NewFolderDirs builds it: YYYY, YYYY-MM and the event folder.
        var newDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { year, month, folder };
        var ops = new GuardedFileOps(env.Settings, env.AppData, TestEnv.Machine, env.Facts, newDirs, new WindowsFileOps(unbuffered));
        return (ops, folder);
    }

    private static byte[] Data(int n) { var b = new byte[n]; new Random(n).NextBytes(b); return b; }

    private static string CopyOne(GuardedFileOps ops, string folder, string name, byte[] data, out VerifyResult verify)
    {
        ops.EnsureDirectory(folder, allowCreate: true);
        var final = Path.Join(folder, name);
        string temp;
        using (var s = ops.CreateTemp(final, data.Length, out temp))
        {
            Assert.EndsWith(".uas-sort.tmp", temp, StringComparison.Ordinal);
            Assert.True((File.GetAttributes(temp) & (FileAttributes.Hidden | FileAttributes.NotContentIndexed))
                        == (FileAttributes.Hidden | FileAttributes.NotContentIndexed));
            s.Write(data);
            ops.FlushToDisk(s);
        }
        verify = ops.VerifyHash(temp, data.Length, XxHash128.HashToUInt128(data), CancellationToken.None);
        ops.FinalizeAttributes(temp, Creation, Mtime);
        Assert.True(ops.RenameNoReplace(temp, final) is Renamed);
        return final;
    }

    [Fact]
    public void FullProtocol_UnbufferedVerify_TimesCopied_HiddenCleared_Confirmed_Flushed()
    {
        using var env = new TestEnv();
        var (ops, folder) = Setup(env);
        var data = Data(1_500_000);
        var final = CopyOne(ops, folder, "DJI_20260927140627_0128_D.MP4", data, out var verify);
        Assert.True(verify is HashMatch { Mode: VerifyMode.Unbuffered });
        Assert.Equal(data, File.ReadAllBytes(final));
        Assert.Equal(Creation, File.GetCreationTimeUtc(final));
        Assert.Equal(Mtime, File.GetLastWriteTimeUtc(final));
        Assert.Equal((FileAttributes)0, File.GetAttributes(final) & FileAttributes.Hidden);
        Assert.True(ops.ConfirmFinal(final, data.Length));
        Assert.False(ops.ConfirmFinal(final, data.Length + 1));
        Assert.False(ops.ConfirmFinal(final.ToLowerInvariant(), data.Length));   // the name must match exactly
        ops.FlushDestination(folder, [final]);                                     // file + directory flush on NTFS
        Assert.True(Directory.Exists(Path.Join(env.VideoRoot, "2026", "2026-09")));
    }

    [Fact]
    public void Verify_Mismatch_ReportsTheHashItGot()
    {
        using var env = new TestEnv();
        var (ops, folder) = Setup(env);
        ops.EnsureDirectory(folder, true);
        var data = Data(10_000);
        string temp;
        using (var s = ops.CreateTemp(Path.Join(folder, "a.MP4"), data.Length, out temp))
        {
            s.Write(data);
            ops.FlushToDisk(s);
        }
        var r = ops.VerifyHash(temp, data.Length, UInt128.One, CancellationToken.None);
        Assert.True(r is HashMismatch { Mode: VerifyMode.Unbuffered } m && m.Got == XxHash128.HashToUInt128(data));
        ops.DeleteOwnTemp(temp);
        Assert.False(File.Exists(temp));
    }

    [Fact]
    public void Verify_BufferedFallback_IsRecordedAsCached()
    {
        using var env = new TestEnv();
        var (ops, folder) = Setup(env, unbuffered: false);
        CopyOne(ops, folder, "b.DNG", Data(5000), out var verify);
        Assert.True(verify is HashMatch { Mode: VerifyMode.Cached });
    }

    [Fact]
    public void Rename_NeverReplaces_ReturnsTargetExists()
    {
        using var env = new TestEnv();
        var (ops, folder) = Setup(env);
        ops.EnsureDirectory(folder, true);
        var final = Path.Join(folder, "c.MP4");
        File.WriteAllText(final, "existing");
        string temp;
        using (var s = ops.CreateTemp(final, 3, out temp)) s.Write([1, 2, 3]);
        ops.FinalizeAttributes(temp, Creation, Mtime);
        Assert.True(ops.RenameNoReplace(temp, final) is TargetExists);
        Assert.Equal("existing", File.ReadAllText(final));
        Assert.True(File.Exists(temp));
        ops.DeleteOwnTemp(temp);
    }

    [Fact]
    public void Path300Characters_RoundTrip()
    {
        using var env = new TestEnv();
        var (ops, folder) = Setup(env, desc: new string('x', 220));
        var final = CopyOne(ops, folder, "DJI_20260927140627_0128_D.MP4", Data(70_000), out var verify);
        Assert.True(final.Length > 300, $"path is only {final.Length} characters");
        Assert.True(verify is HashMatch);
        Assert.True(ops.ConfirmFinal(final, 70_000));
        Assert.True(ops.TryGetSize(final, out var size) && size == 70_000);
    }

    [Fact]
    public void Preallocation_BeyondFreeSpace_FailsAtCreate()
    {
        using var env = new TestEnv();
        var (ops, folder) = Setup(env);
        ops.EnsureDirectory(folder, true);
        var tooBig = ops.FreeBytes(folder) + (10L << 30);
        Assert.ThrowsAny<IOException>(() => ops.CreateTemp(Path.Join(folder, "d.MP4"), tooBig, out _));
    }

    [Fact]
    public void Refusals_FollowThePolicy()
    {
        using var env = new TestEnv();
        var (ops, folder) = Setup(env);
        var existing = env.Temp.File(@"video\2026\2026-08\2026-08-01 Anvil\DJI_0001.MP4");
        Assert.Throws<UnsafeIoException>(() => ops.CreateTemp(Path.Join(env.Temp.Path, "elsewhere", "x.MP4"), 1, out _));
        Assert.Throws<UnsafeIoException>(() => ops.VerifyHash(existing, 16, UInt128.Zero, CancellationToken.None));
        Assert.Throws<UnsafeIoException>(() => ops.DeleteOwnTemp(existing));
        Assert.Throws<UnsafeIoException>(() => ops.EnsureDirectory(Path.Join(env.VideoRoot, "2026", "2026-10", "not planned"), true));
        Assert.Throws<DirectoryNotFoundException>(() => ops.EnsureDirectory(Path.Join(env.VideoRoot, "gone append target"), false));
        ops.EnsureDirectory(folder, true);
        using (ops.CreateTemp(Path.Join(folder, "e.MP4"), 1, out _)) { }
        Assert.Throws<UnsafeIoException>(() => ops.RenameNoReplace(Path.Join(folder, "e.MP4.uas-sort.tmp"), Path.Join(folder, "other.MP4")));
    }

    [Fact]
    public void NewFolderDirs_AreUsedVerbatim_NoAncestorIsAdded()
    {
        using var env = new TestEnv();
        var folder = Path.Join(env.VideoRoot, "2026", "2026-09", "2026-09-27 Zachar Bay");
        var onlyTheFolder = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { folder };
        var ops = new GuardedFileOps(env.Settings, env.AppData, TestEnv.Machine, env.Facts, onlyTheFolder, new WindowsFileOps());
        Assert.Throws<UnsafeIoException>(() => ops.EnsureDirectory(folder, allowCreate: true));   // 2026 is not in the set
        Assert.False(Directory.Exists(Path.Join(env.VideoRoot, "2026")));
    }

    [Fact]
    public void StaleTemp_InAnAppendFolder_CanBeDeleted()
    {
        using var env = new TestEnv();
        var (ops, _) = Setup(env);
        var stale = env.Temp.File(@"video\2026\2026-08\2026-08-01 Anvil\DJI_0002.MP4.uas-sort.tmp", attributes: FileAttributes.Hidden);
        ops.EnsureDirectory(Path.GetDirectoryName(stale)!, allowCreate: false);
        ops.DeleteOwnTemp(stale);
        Assert.False(File.Exists(stale));
    }

    [Fact]
    public void TryGetSize_And_FreeBytes()
    {
        using var env = new TestEnv();
        var (ops, _) = Setup(env);
        Assert.False(ops.TryGetSize(Path.Join(env.VideoRoot, "none.MP4"), out _));
        Assert.True(ops.FreeBytes(Path.Join(env.VideoRoot, "not", "yet", "there")) > 0);
    }
}
