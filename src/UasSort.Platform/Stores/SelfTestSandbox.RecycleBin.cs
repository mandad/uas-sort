namespace UasSort.Platform.Stores;

public sealed partial class SelfTestSandbox
{
    /// <summary>Removes from the Recycle Bin exactly the items this selftest run moved there from its own sandbox (RecycleBinPurge refuses
    /// anything but this %TEMP%\uas-sort-selftest-* root); the selftest's only way to purge.</summary>
    public int PurgeOwnRecycleBinItems() => RecycleBinPurge.PurgeOwn(Root);
}
