// src/UasSort.App/SelfTest/SelfTestChecks.PhotoCleanup.cs
using System.Text;

namespace UasSort.App.SelfTest;

internal static partial class SelfTestChecks
{
    private const string SelfTestDngDto = "2026:09:27 14:05:00";

    /// <summary>Spec 2026-10-04 §8 selftest: a synthetic Picture Offload and Lightroom folder inside the sandbox; the title-bar command opens
    /// the page; verify mode puts only the photo found in Lightroom on Delete; the run really moves that sandbox file to the Recycle Bin and
    /// records it as photoDelete; Done returns to the stage it was opened from; the check purges exactly its own Recycle Bin item.</summary>
    private static async Task<SelfTestCheck> PhotoCleanupFlow(SelfTestContext ctx)
    {
        var shell = ctx.Services.Shell;
        var s = ctx.Sandbox;
        var dng = SelfTestFixture.ReadAll("selftest.dng");
        var inLightroom = WithDto(dng, "2026:09:27 14:04:10");
        var photo = Path.GetRelativePath(s.Root, s.PhotoRoot);
        s.WriteFile(Path.Join(photo, "SELFTEST_0001.DNG"), inLightroom);
        s.WriteFile(Path.Join(photo, "SELFTEST_0002.DNG"), WithDto(dng, "2026:09:27 14:06:00"));
        s.WriteFile(Path.Join(photo, "001_0042", "PANO_0001.DNG"), WithDto(dng, "2026:09:27 14:07:00"));
        s.WriteFile(Path.Join(photo, "001_0042", "PANO_0002.DNG"), WithDto(dng, "2026:09:27 14:07:02"));
        s.WriteFile(Path.Join(photo, "notes.txt"), "selftest"u8);
        s.WriteFile(Path.Join("lightroom", "2026", "2026-09-27", "Selftest_20260927_001.dng"), inLightroom);

        var before = shell.Stage;
        if (!shell.PhotoCleanupEnabled)
            return SelfTestCheck.Fail("photoCleanup.flow", $"[Clean up Picture Offload…] disabled on {before}: {shell.PhotoCleanupUnavailableText}");
        shell.PhotoCleanupCommand.Execute(null);
        int purged;
        string outcome;
        bool pass;
        try
        {
            (pass, outcome) = await PhotoCleanupFlowSteps(ctx, before);
        }
        finally
        {
            purged = s.PurgeOwnRecycleBinItems();     // on every path once the page opened: a sandbox file may already be in the Recycle Bin
        }
        return pass && purged >= 1
            ? SelfTestCheck.Pass("photoCleanup.flow", $"{outcome}; purged {purged} own Recycle Bin item(s)")
            : SelfTestCheck.Fail("photoCleanup.flow", $"{outcome}; purged {purged} own Recycle Bin item(s)");
    }

    private static async Task<(bool Pass, string Outcome)> PhotoCleanupFlowSteps(SelfTestContext ctx, Stage before)
    {
        var shell = ctx.Services.Shell;
        var s = ctx.Sandbox;
        if (!await WaitUntilAsync(() => CurrentPage<PhotoCleanupPage>(ctx) is { IsLoaded: true } && shell.PhotoCleanup is { IsBusy: false },
                                  TimeSpan.FromSeconds(10)))
            return (false, "PhotoCleanupPage did not open");
        var vm = shell.PhotoCleanup!;
        if (vm.BlockingText is { } blocked) return (false, "blocked: " + blocked);

        vm.Verify = true;
        vm.PickDate(new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero));
        await vm.NextCommand.ExecuteAsync(null);
        if (!await WaitUntilAsync(() => vm.Step == PhotoCleanupStep.Review && !vm.IsBusy, TimeSpan.FromSeconds(10)))
            return (false, $"review not reached (step {vm.Step}, {vm.BlockingText})");
        PhotoCleanupRowVm? Row(string key) => vm.Rows.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));
        var decisions = string.Join(", ", vm.Rows.Select(r => $"{r.Key}={r.Decision} ({r.StatusText})"));
        var rowsOk = Row("SELFTEST_0001.DNG") is { Decision: RowDecision.Delete, IsVerified: true }
                     && Row("SELFTEST_0002.DNG") is { Decision: RowDecision.Keep }
                     && Row("001_0042") is { Decision: RowDecision.Keep }
                     && vm.NotTouchedText is not null;

        await vm.NextCommand.ExecuteAsync(null);
        vm.AckMove = true;
        await vm.RunCommand.ExecuteAsync(null);
        if (!await WaitUntilAsync(() => vm.Step == PhotoCleanupStep.Result, TimeSpan.FromSeconds(10)))
            return (false, $"result not reached (step {vm.Step}, {vm.BlockingText}); rows {decisions}");

        var left = ctx.Services.Platform.Lister.Enumerate(s.PhotoRoot, recurse: false, new HashSet<string>(StringComparer.OrdinalIgnoreCase))
                      .Entries.Select(e => Path.GetFileName(e.FullPath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var moved = !left.Contains("SELFTEST_0001.DNG") && left.Contains("SELFTEST_0002.DNG") && left.Contains("001_0042") && left.Contains("notes.txt");
        var recorded = ctx.Services.Platform.LedgerFor(s.VideoRoot).Load().PhotoDeletes.Values
                          .Any(d => d.Evidence == PhotoDeleteRecords.EvidenceLightroom
                                    && d.Dest.EndsWith(@"\SELFTEST_0001.DNG", StringComparison.OrdinalIgnoreCase));
        var headline = vm.Result?.HeadlineText;
        vm.DoneCommand.Execute(null);
        var back = await WaitUntilAsync(() => shell.Stage == before, TimeSpan.FromSeconds(5));
        return rowsOk && moved && recorded && back
            ? (true, $"verify mode kept the unconfirmed rows; {headline}; photoDelete recorded")
            : (false, $"rows {decisions} (ok {rowsOk}), moved {moved}, recorded {recorded}, back {back}");
    }

    /// <summary>selftest.dng with every occurrence of its DateTimeOriginal text replaced (same length), so each copy is its own shot.</summary>
    private static byte[] WithDto(byte[] dng, string dto)
    {
        var from = Encoding.ASCII.GetBytes(SelfTestDngDto);
        var to = Encoding.ASCII.GetBytes(dto);
        var copy = dng.ToArray();
        for (var i = 0; i + from.Length <= copy.Length; i++)
            if (copy.AsSpan(i, from.Length).SequenceEqual(from)) to.CopyTo(copy, i);
        return copy;
    }
}
