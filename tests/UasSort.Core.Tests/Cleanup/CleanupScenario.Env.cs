// tests/UasSort.Core.Tests/Cleanup/CleanupScenario.Env.cs
namespace UasSort.Core.Tests.Cleanup;

internal sealed partial class CleanupScenario
{
    /// <summary>The fake PC for a cleanup run: FakeLayout.NewFileSystem() with card volume E:\ (this scenario's Space) holding
    /// every card entry, and the library files the scenario lists. The same FakeFileSystem is the library lister.</summary>
    public FakeFileSystem MakeCard()
    {
        var fs = FakeLayout.NewFileSystem();
        fs.AddCardVolume(CardRoot, Identity, Space);
        foreach (var e in Entries)
        {
            var full = PathRules.Join(CardRoot, e.RelPath);
            if ((e.RawAttributes & FakeFileSystem.DirectoryAttribute) != 0) fs.AddDirectory(full);
            else fs.AddFile(full, e.Size, e.MtimeUtc, e.RawAttributes);
        }
        foreach (var e in VideoListing.Concat(PhotoListing)) fs.AddFile(e.FullPath, e.Size, e.MtimeUtc, e.RawAttributes);
        return fs;
    }

    /// <summary>In-memory FakeLedgerStore over this scenario's ledger; Check() returns its (Ok) status until a test overrides it.</summary>
    public FakeLedgerStore MakeLedgerStore()
        => new(null, VideoRoot, FakeLayout.Machine, Ledger()) { StatusOverride = Ledger().Status };

    /// <summary>Build + Confirm with every acknowledgement given (the rows left as Build decided them).</summary>
    public ConfirmedCleanupPlan Confirmed(CleanupRequest request, CleanupRows? rows = null)
    {
        var plan = Build(request, rows);
        var nil = plan.Delete.Where(c => c.Eligibility == CleanupEligibility.NotInLibrary).Select(c => c.Unit).ToImmutableHashSet();
        return plan.Confirm(new CleanupAck(plan.Fingerprint, true, nil.Count > 0, nil), new FakeTimeProvider(new DateTimeOffset(ScanUtc)));
    }
}
