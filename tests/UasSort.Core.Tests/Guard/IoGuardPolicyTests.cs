// tests/UasSort.Core.Tests/Guard/IoGuardPolicyTests.cs
namespace UasSort.Core.Tests.Guard;

public class IoGuardPolicyTests
{
    internal const string V = @"C:\Users\u\OneDrive\Pictures\UAS Videos";
    internal const string P = V + @"\Picture Offload";
    internal const string Prev = @"D:\Old Photos";
    internal const string L = V + @"\.uas-sort";
    internal const string A = @"C:\Users\u\AppData\Local\uas-sort";
    internal const string Z = V + @"\2026\2026-09\2026-09-27 Zachar Bay";
    internal const string Council = V + @"\2026\2026-07\2026-07-25 Council Road";
    internal const string OwnTemp = Z + @"\DJI_20260927140627_0128_D.MP4.uas-sort.tmp";
    internal const string RenamedFile = Z + @"\DJI_20260927140127_0123_D.MP4";
    internal const string LibraryFile = Council + @"\DJI_20260725232655_0117_D.MP4";
    private const long None = -1;

    internal static GuardContext Context(ConfirmedCleanupPlan? cleanup = null, string? cardRoot = @"E:\", bool verified = false) => new(
        V, P, [Prev], cardRoot, A, "DESKTOP-A",
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, V + @"\2026", V + @"\2026\2026-09", Z),
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, OwnTemp),
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, RenamedFile),
        @"C:\", verified, cleanup);

    internal static string Kind(GuardDecision d) => d switch
    {
        GuardAllow => "Allow",
        GuardUnsafe => "Unsafe",
        GuardCloudOnly => "CloudOnly",
        GuardHydration => "Hydration",
    };

    [Theory]
    // ── allowed (Ref §13 IO guard policy)
    [InlineData(IoOp.ReadData, L + @"\ledger-B.jsonl", 0x20, "Allow")]
    [InlineData(IoOp.ReadData, L + @"\ledger-B-DESKTOP-A.jsonl", 0x20, "Allow")]
    [InlineData(IoOp.AppendOwnLedger, L + @"\ledger-DESKTOP-A.jsonl", 0x20, "Allow")]
    [InlineData(IoOp.AppendOwnLedger, L + @"\ledger-DESKTOP-A.jsonl", None, "Allow")]
    [InlineData(IoOp.CreateDir, L, None, "Allow")]
    [InlineData(IoOp.SetPinned, L, 0x10, "Allow")]
    [InlineData(IoOp.SetPinned, L, 0x400010, "Allow")]
    [InlineData(IoOp.CreateNew, Z + @"\X.MP4.uas-sort.tmp", None, "Allow")]
    [InlineData(IoOp.ReadData, OwnTemp, 0x2022, "Allow")]
    [InlineData(IoOp.SetAttributesOrTimes, OwnTemp, 0x2022, "Allow")]
    [InlineData(IoOp.Rename, OwnTemp, 0x2020, "Allow")]
    [InlineData(IoOp.Delete, OwnTemp, 0x2022, "Allow")]
    [InlineData(IoOp.Delete, Council + @"\DJI_20260725232655_0117_D.MP4.uas-sort.tmp", 0x2022, "Allow")]
    [InlineData(IoOp.OpenForFlush, RenamedFile, 0x20, "Allow")]
    [InlineData(IoOp.OpenForFlush, Z, 0x10, "Allow")]
    [InlineData(IoOp.CreateDir, V + @"\2026", None, "Allow")]
    [InlineData(IoOp.CreateDir, V + @"\2026\2026-09", None, "Allow")]
    [InlineData(IoOp.CreateDir, Z, None, "Allow")]
    [InlineData(IoOp.ReadData, @"E:\DCIM\DJI_001\DJI_20260927140627_0128_D.MP4", 0x20, "Allow")]
    [InlineData(IoOp.ReadData, A + @"\settings.json", 0x20, "Allow")]
    [InlineData(IoOp.CreateNew, A + @"\drafts\vol-1A2B3C4D.json", None, "Allow")]
    [InlineData(IoOp.ReadData, V + @"\.UAS-SORT\LEDGER-B.JSONL", 0x20, "Allow")]
    // ── unsafe
    [InlineData(IoOp.AppendOwnLedger, L + @"\ledger-B.jsonl", 0x20, "Unsafe")]
    [InlineData(IoOp.CreateNew, L + @"\ledger-B.jsonl", None, "Unsafe")]
    [InlineData(IoOp.SetAttributesOrTimes, L + @"\ledger-B.jsonl", 0x20, "Unsafe")]
    [InlineData(IoOp.ReadData, L + @"\settings.json", 0x20, "Unsafe")]
    [InlineData(IoOp.ReadData, L + @"\sub\ledger-C.jsonl", 0x20, "Unsafe")]
    [InlineData(IoOp.Delete, L + @"\ledger-DESKTOP-A.jsonl", 0x20, "Unsafe")]
    [InlineData(IoOp.Rename, L + @"\ledger-B.jsonl", 0x20, "Unsafe")]
    [InlineData(IoOp.CreateDir, L + @"\sub", None, "Unsafe")]
    [InlineData(IoOp.ReadData, V + @"\ledger-X.jsonl", 0x20, "Unsafe")]
    [InlineData(IoOp.ReadData, P + @"\ledger-X.jsonl", 0x20, "Unsafe")]
    [InlineData(IoOp.ReadData, V + @"\.uas-sort2\ledger-A.jsonl", 0x20, "Unsafe")]
    [InlineData(IoOp.ReadData, LibraryFile, 0x20, "Unsafe")]
    [InlineData(IoOp.ReadData, Prev + @"\DJI_20260725233000_0116_D.DNG", 0x20, "Unsafe")]
    [InlineData(IoOp.CreateNew, Council + @"\DJI_20260726235645_0001_D.MP4", None, "Unsafe")]
    [InlineData(IoOp.CreateDir, Council, 0x10, "Unsafe")]
    [InlineData(IoOp.CreateDir, V + @"\2026\2026-10", None, "Unsafe")]
    [InlineData(IoOp.Rename, RenamedFile, 0x20, "Unsafe")]
    [InlineData(IoOp.Delete, LibraryFile, 0x20, "Unsafe")]
    [InlineData(IoOp.CreateNew, @"E:\DCIM\x.txt", None, "Unsafe")]
    [InlineData(IoOp.SetAttributesOrTimes, @"E:\DCIM\DJI_001\DJI_20260927140627_0128_D.MP4", 0x20, "Unsafe")]
    [InlineData(IoOp.OpenForFlush, @"E:\", 0x10, "Unsafe")]
    [InlineData(IoOp.Delete, @"E:\DCIM\DJI_001\DJI_20260927140627_0128_D.MP4", 0x20, "Unsafe")]
    [InlineData(IoOp.ReadData, Z + @"\missing.MP4", None, "Unsafe")]
    [InlineData(IoOp.ReadData, @"C:\Windows\win.ini", 0x20, "Unsafe")]
    // ── placeholders
    [InlineData(IoOp.ReadData, L + @"\ledger-B.jsonl", 0x400000, "CloudOnly")]
    [InlineData(IoOp.ReadData, LibraryFile, 0x401620, "Hydration")]
    [InlineData(IoOp.ReadData, LibraryFile, 0x40000, "Hydration")]
    [InlineData(IoOp.ReadData, LibraryFile, 0x1000, "Hydration")]
    [InlineData(IoOp.ReadData, L + @"\notes.txt", 0x400000, "Hydration")]
    public void Check_FollowsTheRuleTable(IoOp op, string path, long attributes, string expected)
        => Assert.Equal(expected, Kind(IoGuardPolicy.Check(op, path, attributes < 0 ? null : (uint)attributes, Context())));

    [Fact]
    public void Unsafe_NamesTheOperationAndPath()
    {
        var d = IoGuardPolicy.Check(IoOp.ReadData, LibraryFile, 0x20, Context());
        var reason = d switch { GuardUnsafe u => u.Reason, GuardAllow => "", GuardCloudOnly => "", GuardHydration => "" };
        Assert.Contains("ReadData", reason, StringComparison.Ordinal);
        Assert.Contains(LibraryFile, reason, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutACardRoot_CardPathsAreOutsideEveryRoot()
        => Assert.Equal("Unsafe", Kind(IoGuardPolicy.Check(IoOp.ReadData, @"E:\DCIM\DJI_001\a.MP4", 0x20, Context(cardRoot: null))));
}
