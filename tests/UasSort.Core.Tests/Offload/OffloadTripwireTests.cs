// tests/UasSort.Core.Tests/Offload/OffloadTripwireTests.cs
using System.Text;
using UasSort.Core;
using UasSort.Core.Offload;
using UasSort.Testing;
using UasSort.Testing.Offload;
using static UasSort.Testing.Offload.OffloadPlanBuilder;

namespace UasSort.Core.Tests.Offload;

public class OffloadTripwireTests
{
    private const uint Placeholder = FakeFileSystem.CloudOnlyPlaceholder;
    private const string ZRel = CommitSessionTests.ZRel;

    private sealed record Fixture(CommitSessionTests.Harness H, string Stale, string OldVideo, string OldPhoto, string LedgerFolder);

    private static Fixture Library(bool cloudOnlyLedgerB = false)
    {
        const string oldName = "DJI_20260927150000_0150_D.MP4", photoName = "DJI_20260801100000_0100_D.DNG";
        var (h, stale, folder) = CommitSessionTests.AppendWithStaleTemp(ledgerOnFileSystem: true, arrange: b =>
        {
            b.LibraryVideo(ZRel + "\\" + oldName, 90_000, Placeholder);
            b.LibraryPhoto(photoName, 27_000_000, Placeholder);
        });
        var fs = h.Rig.Fs;
        var oldVideo = folder + "\\" + oldName;
        var oldPhoto = h.Rig.B.PhotoRoot + "\\" + photoName;
        fs.AddFile(oldVideo, 90_000, T0.AddDays(-2), Placeholder);
        fs.AddFile(oldPhoto, 27_000_000, T0.AddDays(-50), Placeholder);
        var ledgerFolder = LedgerPaths.For(h.Rig.B.VideoRoot);
        fs.AddFile(ledgerFolder + @"\ledger-B.jsonl", Encoding.UTF8.GetBytes("{\"t\":\"run\"}\n"), T0.AddDays(-1),
                   cloudOnlyLedgerB ? Placeholder : FakeFileSystem.ArchiveAttribute);
        return new Fixture(h, stale, oldVideo, oldPhoto, ledgerFolder);
    }

    private static bool Permitted(FakeGuardCall call, Fixture f)
        => PathRules.IsSameOrUnder(call.Path, CardRoot)
           || call.Path.EndsWith(OffloadPaths.TempSuffix, StringComparison.OrdinalIgnoreCase)
           || PathRules.Equal(call.Path, f.LedgerFolder)
           || (PathRules.Equal(PathRules.Parent(call.Path) ?? "", f.LedgerFolder) && LedgerPaths.IsLedgerFileName(PathRules.FileName(call.Path)))
           || PathRules.IsSameOrUnder(call.Path, FakeLayout.AppDataDir);

    [Fact]
    public async Task PreflightAndOffload_OpenOnlyCardFiles_OwnTemps_AndTheLedgerExemption()
    {
        var f = Library();
        using var session = f.H.Begin();

        var result = await session.StartAsync(session.RequiredAcks.ToHashSet(), new ListProgress<OffloadProgress>(), CancellationToken.None);

        Assert.IsType<Verified>(result.Offload.Outcomes[0]);
        var log = f.H.Rig.Fs.GuardLog;
        Assert.All(log, c => Assert.True(c.Decision == "Allow" && Permitted(c, f), $"{c.Op} {c.Path} → {c.Decision}"));
        Assert.DoesNotContain(log, c => PathRules.Equal(c.Path, f.OldVideo) || PathRules.Equal(c.Path, f.OldPhoto));
        Assert.Contains(log, c => c.Op == IoOp.ReadData && c.Path.EndsWith(@"\ledger-B.jsonl", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(log, c => c.Op == IoOp.AppendOwnLedger
                                  && PathRules.Equal(c.Path, LedgerPaths.OwnFile(f.H.Rig.B.VideoRoot, FakeLayout.Machine)));
        Assert.Empty(f.H.Rig.Fs.CardDeleteViolations);
        f.H.Rig.Fs.AssertNoViolations();
    }

    [Fact]
    public async Task StrayFilesInTheLedgerFolder_AreNeverTouched()
    {
        var f = Library();
        f.H.Rig.Fs.AddFile(f.LedgerFolder + @"\notes.txt", 10, T0);
        f.H.Rig.Fs.AddFile(f.LedgerFolder + @"\sub\ledger-C.jsonl", 10, T0);
        using var session = f.H.Begin();

        await session.StartAsync(session.RequiredAcks.ToHashSet(), new ListProgress<OffloadProgress>(), CancellationToken.None);

        Assert.DoesNotContain(f.H.Rig.Fs.GuardLog, c => c.Path.EndsWith(@"\notes.txt", StringComparison.OrdinalIgnoreCase)
                                                        || c.Path.Contains(@"\.uas-sort\sub", StringComparison.OrdinalIgnoreCase));
        f.H.Rig.Fs.AssertNoViolations();
    }

    [Fact]
    public void ACloudOnlyLedgerFile_IsBlocking_AndNothingIsOpened()
    {
        var f = Library(cloudOnlyLedgerB: true);
        using var session = f.H.Begin();

        Assert.Contains(session.Preflight.Issues, i => i.Code == IssueCode.LedgerCloudOnly && i.Severity == IssueSeverity.Blocking);
        Assert.False(session.Preflight.CanStart);
        Assert.Empty(f.H.Rig.Fs.GuardLog);
        f.H.Rig.Fs.AssertNoViolations();
    }

    [Fact]
    public void PreflightThenBack_DeletesNothing_NotEvenTheStaleTempItListed()
    {
        var f = Library();

        using (var session = f.H.Begin()) Assert.Contains(f.Stale, session.Preflight.StaleTemps);

        Assert.True(f.H.Rig.Fs.Exists(f.Stale));
        Assert.Empty(f.H.Rig.Fs.GuardLog);
        Assert.Empty(f.H.Rig.Fs.CardDeleteViolations);
    }
}
