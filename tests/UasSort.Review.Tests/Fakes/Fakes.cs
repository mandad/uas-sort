// tests/UasSort.Review.Tests/Fakes/Fakes.cs
namespace UasSort.Review.Tests;

/// <summary>
/// Queues posted actions until the test runs them. The fake "UI thread" is whatever thread is inside RunAll, so
/// HasThreadAccess is true only there; PostedWithAccess records, per Post, whether it came from the fake UI thread.
/// </summary>
internal sealed class FakeUiDispatcher : IUiDispatcher
{
    [ThreadStatic] private static bool t_inUi;
    private readonly Lock _lock = new();
    private readonly Queue<Action> _queue = new();

    public bool HasThreadAccess => t_inUi;
    public List<bool> PostedWithAccess { get; } = [];

    public void Post(Action a)
    {
        lock (_lock) { _queue.Enqueue(a); PostedWithAccess.Add(t_inUi); }
    }

    public int Pending { get { lock (_lock) { return _queue.Count; } } }

    /// <summary>Runs every queued action (and anything they queue) on the calling thread, as the UI thread.</summary>
    public void RunAll()
    {
        var outer = t_inUi;
        t_inUi = true;
        try
        {
            while (true)
            {
                Action? next;
                lock (_lock) { next = _queue.Count > 0 ? _queue.Dequeue() : null; }
                if (next is null) return;
                next();
            }
        }
        finally
        {
            t_inUi = outer;
        }
    }
}

internal sealed class FakeDialogService : IDialogService
{
    public List<DialogRequest> Shown { get; } = [];
    public Queue<DialogResult> Answers { get; } = new();
    public Task<DialogResult> ShowAsync(DialogRequest r)
    {
        Shown.Add(r);
        return Task.FromResult(Answers.Count > 0 ? Answers.Dequeue() : DialogResult.Primary);
    }
}

internal sealed class FakeShellLauncher : IShellLauncher
{
    public List<string> Opened { get; } = [];
    public void OpenFolder(string path) => Opened.Add("folder:" + path);
    public void OpenFile(string path) => Opened.Add("file:" + path);
    public void OpenHttps(Uri uri) => Opened.Add("https:" + uri);
}

internal sealed class FakeDraftStore : IDraftStore
{
    public Dictionary<string, Draft> Drafts { get; } = new(StringComparer.Ordinal);
    public int Saves { get; private set; }
    public List<string> Deleted { get; } = [];
    public Draft? Load(string cardKey) => Drafts.GetValueOrDefault(cardKey);
    public void Save(string cardKey, Draft d) { Drafts[cardKey] = d; Saves++; }
    public void Delete(string cardKey) { Drafts.Remove(cardKey); Deleted.Add(cardKey); }
}

internal sealed class FakeFreeSpace : IFreeSpace
{
    public long? FreeBytes(string anyPathOnVolume) => 317_000_000_000;
}

internal sealed class ListLog : IReviewLog
{
    public List<string> Warnings { get; } = [];
    public void Warn(string message) => Warnings.Add(message);
}

internal static class Fake
{
    public static ReviewServices Services(FakeUiDispatcher ui, TimeProvider? time = null, FakeDraftStore? drafts = null,
                                          FakeDialogService? dialogs = null, ListLog? log = null)
        => new(ui, dialogs ?? new FakeDialogService(), new FakeShellLauncher(), new FakeThumbnails(),
               drafts ?? new FakeDraftStore(), new FakeFreeSpace(), time ?? new FakeTimeProvider(new DateTimeOffset(2026, 9, 28, 2, 0, 0, TimeSpan.Zero)),
               log ?? new ListLog());

    /// <summary>The shared in-memory <see cref="FakeLedgerStore"/> (Parts 06/07) for the fixture library; <paramref name="status"/>
    /// pins what <c>Check()</c> reports.</summary>
    public static FakeLedgerStore Ledger(LedgerFolderStatus? status = null, LedgerSnapshot? snapshot = null, string videoRoot = TestPlans.VideoRoot)
        => new(null, videoRoot, "PC1", snapshot ?? TestPlans.Ledger()) { StatusOverride = status ?? (snapshot ?? TestPlans.Ledger()).Status };
}
