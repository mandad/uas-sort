// tests/UasSort.Testing/Offload/OffloadRig.cs
using UasSort.Core;
using UasSort.Core.Offload;
using UasSort.Testing;

namespace UasSort.Testing.Offload;

/// <summary>One offload fixture: a Plan from OffloadPlanBuilder, its batch, the card files on Part 02's FakeFileSystem (FakeLayout),
/// Part 02's FakeCardReader and FakeFileOps (with the run's NewFolderDirs), and the shared FakeLedgerStore.</summary>
public sealed class OffloadRig
{
    public OffloadRig(OffloadPlanBuilder builder)
    {
        B = builder;
        Fs = FakeLayout.NewFileSystem();
        if (!PathRules.Equal(builder.PhotoRoot, FakeLayout.PhotoRoot))
        {
            Fs.AddDirectory(builder.PhotoRoot);
            Fs.Context = Fs.Context with { PhotoRoot = builder.PhotoRoot };
        }
    }

    public OffloadPlanBuilder B { get; }
    public FakeFileSystem Fs { get; }
    public Plan Plan { get; private set; } = null!;
    public OffloadBatch Batch { get; private set; } = null!;
    public FakeCardReader Reader { get; private set; } = null!;
    public FakeFileOps Files { get; private set; } = null!;
    public FakeLedgerStore Ledger { get; private set; } = null!;
    public FakeLedgerWriter Writer => Ledger.Writer;
    public FakeOffloadLock Lock { get; } = new();
    public FakePowerRequest Power { get; } = new();
    public FakeThumbnails Thumbnails { get; } = new();
    public MemReportStore Reports { get; } = new();
    public List<VolumeInfo> Volumes { get; } = [OffloadVolumes.NtfsC];
    public Dictionary<string, byte[]> CardData { get; } = new(StringComparer.OrdinalIgnoreCase);

    public OffloadRig Build(bool cardFiles = true, bool ledgerOnFileSystem = false)
    {
        Plan = B.Build();
        if (cardFiles && CardData.Count == 0)
        {
            int seed = 1;
            foreach (var e in B.Entries)
            {
                var data = Bytes(e.Size, seed++);
                CardData[e.RelPath] = data;
                Fs.AddFile(CardPath(e.RelPath), data, e.MtimeUtc, e.RawAttributes);
            }
        }
        Reader = new FakeCardReader(Fs, OffloadPlanBuilder.CardRoot, OffloadPlanBuilder.Card);
        Ledger = new FakeLedgerStore(ledgerOnFileSystem ? Fs : null, B.VideoRoot, FakeLayout.Machine, Plan.Base.Scan.Ledger);
        Rebatch(OffloadCompiler.Compile(Plan, "run-1", Plan.Base.Scan.Inventory.Source.Identity ?? OffloadPlanBuilder.Card));
        return this;
    }

    public void Rebatch(OffloadBatch batch)
    {
        Batch = batch;
        Files = new FakeFileOps(Fs, OffloadCompiler.NewFolderDirs(batch, B.VideoRoot));
    }

    public static string CardPath(string cardRelPath) => PathRules.Join(OffloadPlanBuilder.CardRoot, cardRelPath);

    public static byte[] Bytes(long size, int seed)
    {
        var b = new byte[size];
        for (long i = 0; i < size; i++) b[i] = unchecked((byte)(((ulong)i + (ulong)seed * 7919UL) * 0x9E3779B97F4A7C15UL >> 56));
        return b;
    }
}
