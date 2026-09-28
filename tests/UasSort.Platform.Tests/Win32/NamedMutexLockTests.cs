using UasSort.Platform.Win32;

namespace UasSort.Platform.Tests.Win32;

/// <summary>Ref §13 Windows integration: "the named-mutex fallback" (Ref §4.4 step 1).</summary>
public sealed class NamedMutexLockTests
{
    private static string UniqueName() => @"Local\uas-sort-test-" + Guid.NewGuid().ToString("N");

    [Fact]
    public void TryAcquire_FirstCaller_GetsTheLock()
    {
        var name = UniqueName();
        using var first = NamedMutexLock.TryAcquire(name);
        Assert.NotNull(first);
        Assert.Equal(name, first.Name);
    }

    [Fact]
    public void TryAcquire_WhileHeld_ReturnsNull()
    {
        var name = UniqueName();
        using var first = NamedMutexLock.TryAcquire(name);
        using var second = NamedMutexLock.TryAcquire(name);
        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public void TryAcquire_AfterTheHolderDisposes_Succeeds()
    {
        var name = UniqueName();
        NamedMutexLock.TryAcquire(name)!.Dispose();
        using var again = NamedMutexLock.TryAcquire(name);
        Assert.NotNull(again);
    }

    [Fact]
    public async Task Dispose_FromAnotherThread_ReleasesTheName()
    {
        var name = UniqueName();
        var held = NamedMutexLock.TryAcquire(name)!;
        await Task.Run(held.Dispose, TestContext.Current.CancellationToken);
        using var again = NamedMutexLock.TryAcquire(name);
        Assert.NotNull(again);
    }

    [Fact]
    public void SingleInstanceNames_MatchTheSpec()
    {
        Assert.Equal("uas-sort", SingleInstance.AppInstanceKey);
        Assert.Equal(@"Local\uas-sort", SingleInstance.MutexName);
    }
}
