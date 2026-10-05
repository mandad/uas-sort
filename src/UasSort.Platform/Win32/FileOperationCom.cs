using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace UasSort.Platform.Win32;

/// <summary>The shell's IFileOperation through source-generated COM (AOT-safe), used only by WindowsPhotoRootRecycler to move one item to
/// the Recycle Bin. Never a permanent delete: RecycleSink cancels every delete the shell would not recycle — the shell clears
/// TSF_DELETE_RECYCLE_IF_POSSIBLE for those (an online-only OneDrive file, a volume without a Recycle Bin, an item too large for it).</summary>
internal static unsafe partial class FileOperationCom
{
    public const int MaxShellPath = 260;
    internal const uint TsfDeleteRecycleIfPossible = 0x80;
    internal const int HResultCancelled = unchecked((int)0x800704C7);            // HRESULT_FROM_WIN32(ERROR_CANCELLED)
    private const uint FofSilent = 0x0004, FofNoConfirmation = 0x0010, FofAllowUndo = 0x0040, FofNoErrorUi = 0x0400;
    private const uint FofxRecycleOnDelete = 0x00080000, FofxEarlyFailure = 0x00100000;
    internal const uint RecycleFlags = FofAllowUndo | FofNoConfirmation | FofNoErrorUi | FofSilent | FofxRecycleOnDelete | FofxEarlyFailure;
    private const uint ClsctxInprocServer = 0x1, ClsctxLocalServer = 0x4;
    private static readonly Guid ClsidFileOperation = new("3ad05575-8857-4850-9277-11b85bdb8e09");
    private static readonly Guid IidFileOperation = new("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8");
    private static readonly Guid IidShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

    /// <summary>Moves one file or folder to the Recycle Bin. Must run on an STA thread (StaWorker). Never throws for a shell or COM
    /// failure: every one becomes a RecycleError, so the executor always ends with a result and a report.</summary>
    public static RecycleResult Recycle(string path)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("IFileOperation needs an STA thread");               // a wiring bug, not a shell failure
#pragma warning disable CA1031 // a COM wrapper may throw (COMException, InvalidCastException, …): it becomes this item's RecycleError, never a faulted run
        try
        {
            return RecycleCore(path);
        }
        catch (Exception e)
        {
            return new RecycleError(e.HResult, "the shell's file operation failed: " + e.Message, false);
        }
#pragma warning restore CA1031
    }

    private static RecycleResult RecycleCore(string path)
    {
        var hr = CoCreateInstance(ClsidFileOperation, 0, ClsctxInprocServer | ClsctxLocalServer, IidFileOperation, out var opPtr);
        if (hr < 0) return Failed(hr, "the shell's file operation couldn't be created");
        IFileOperation op;
        try
        {
            op = ComInterfaceMarshaller<IFileOperation>.ConvertToManaged((void*)opPtr)
                 ?? throw new InvalidOperationException("IFileOperation: no object");
        }
        finally
        {
            ComInterfaceMarshaller<IFileOperation>.Free((void*)opPtr);
        }

        nint item = 0;
        uint cookie = 0;
        var sink = new RecycleSink();
        try
        {
            if ((hr = op.SetOperationFlags(RecycleFlags)) < 0) return Failed(hr, "the Recycle Bin flags were refused");
            if ((hr = SHCreateItemFromParsingName(path, 0, IidShellItem, out item)) < 0) return Failed(hr, "the shell couldn't find it");
            if ((hr = op.Advise(sink, out cookie)) < 0) return Failed(hr, "the shell refused the progress callback");
            if ((hr = op.DeleteItem(item, 0)) < 0) return Failed(hr, "the shell refused to queue the move");
            hr = op.PerformOperations();
            _ = op.GetAnyOperationsAborted(out var aborted);
            return Outcome(sink.RefusedPermanentDelete, hr, sink.DeleteResult, sink.Deleted, aborted != 0, sink.RemovedWithoutBinItem);
        }
        finally
        {
            if (cookie != 0) _ = op.Unadvise(cookie);
            if (item != 0) Marshal.Release(item);
            if ((object)op is ComObject com) com.FinalRelease();                                   // via object: ComObject is sealed (CS8121)
        }
    }

    /// <summary>The result of one IFileOperation run (pure, unit-tested). The sink's refusal is checked first: whatever PerformOperations
    /// returned (success, ERROR_CANCELLED, another failure), a refused permanent delete is NotRecyclable and the item was kept. A delete
    /// that succeeded without a new Recycle Bin item (PostDeleteItem's psiNewlyCreated NULL = fully deleted, deferred minor P.11) is
    /// RecycleNotInBin, never RecycleOk: the executor checks whether the item is gone and says so.</summary>
    internal static RecycleResult Outcome(bool refusedPermanentDelete, int performResult, int deleteResult, bool deleted, bool aborted,
                                          bool removedWithoutBinItem)
    {
        if (refusedPermanentDelete)
            return new RecycleError(HResultCancelled, "Windows would delete it permanently instead of moving it to the Recycle Bin; kept", true);
        if (performResult < 0) return Failed(performResult, "moving it to the Recycle Bin failed");
        if (deleteResult < 0) return Failed(deleteResult, "moving it to the Recycle Bin failed");
        if (aborted || !deleted) return new RecycleError(HResultCancelled, "the move to the Recycle Bin was cancelled; kept", false);
        if (removedWithoutBinItem) return new RecycleNotInBin("Windows removed it without putting it in the Recycle Bin");
        return new RecycleOk();
    }

    private static RecycleError Failed(int hr, string what)
        => new(hr, string.Create(CultureInfo.InvariantCulture, $"{what} (HRESULT 0x{hr:X8})"), false);

    [LibraryImport("ole32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int CoCreateInstance(in Guid rclsid, nint pUnkOuter, uint dwClsContext, in Guid riid, out nint ppv);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int SHCreateItemFromParsingName(string pszPath, nint pbc, in Guid riid, out nint ppv);
}

/// <summary>IFileOperation (shobjidl_core.h), every member in vtable order; members the recycler never calls take raw pointers.</summary>
[GeneratedComInterface]
[Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8")]
internal partial interface IFileOperation
{
    [PreserveSig] int Advise(IFileOperationProgressSink pfops, out uint pdwCookie);
    [PreserveSig] int Unadvise(uint dwCookie);
    [PreserveSig] int SetOperationFlags(uint dwOperationFlags);
    [PreserveSig] int SetProgressMessage(nint pszMessage);
    [PreserveSig] int SetProgressDialog(nint popd);
    [PreserveSig] int SetProperties(nint pproparray);
    [PreserveSig] int SetOwnerWindow(nint hwndOwner);
    [PreserveSig] int ApplyPropertiesToItem(nint psiItem);
    [PreserveSig] int ApplyPropertiesToItems(nint punkItems);
    [PreserveSig] int RenameItem(nint psiItem, nint pszNewName, nint pfopsItem);
    [PreserveSig] int RenameItems(nint pUnkItems, nint pszNewName);
    [PreserveSig] int MoveItem(nint psiItem, nint psiDestinationFolder, nint pszNewName, nint pfopsItem);
    [PreserveSig] int MoveItems(nint punkItems, nint psiDestinationFolder);
    [PreserveSig] int CopyItem(nint psiItem, nint psiDestinationFolder, nint pszCopyName, nint pfopsItem);
    [PreserveSig] int CopyItems(nint punkItems, nint psiDestinationFolder);
    [PreserveSig] int DeleteItem(nint psiItem, nint pfopsItem);
    [PreserveSig] int DeleteItems(nint punkItems);
    [PreserveSig] int NewItem(nint psiDestinationFolder, uint dwFileAttributes, nint pszName, nint pszTemplateName, nint pfopsItem);
    [PreserveSig] int PerformOperations();
    [PreserveSig] int GetAnyOperationsAborted(out int pfAnyOperationsAborted);
}

/// <summary>IFileOperationProgressSink (shobjidl_core.h), every member in vtable order.</summary>
[GeneratedComInterface]
[Guid("04b0f1a7-9490-44bc-96e1-4296a31252e2")]
internal partial interface IFileOperationProgressSink
{
    [PreserveSig] int StartOperations();
    [PreserveSig] int FinishOperations(int hrResult);
    [PreserveSig] int PreRenameItem(uint dwFlags, nint psiItem, nint pszNewName);
    [PreserveSig] int PostRenameItem(uint dwFlags, nint psiItem, nint pszNewName, int hrRename, nint psiNewlyCreated);
    [PreserveSig] int PreMoveItem(uint dwFlags, nint psiItem, nint psiDestinationFolder, nint pszNewName);
    [PreserveSig] int PostMoveItem(uint dwFlags, nint psiItem, nint psiDestinationFolder, nint pszNewName, int hrMove, nint psiNewlyCreated);
    [PreserveSig] int PreCopyItem(uint dwFlags, nint psiItem, nint psiDestinationFolder, nint pszNewName);
    [PreserveSig] int PostCopyItem(uint dwFlags, nint psiItem, nint psiDestinationFolder, nint pszNewName, int hrCopy, nint psiNewlyCreated);
    [PreserveSig] int PreDeleteItem(uint dwFlags, nint psiItem);
    [PreserveSig] int PostDeleteItem(uint dwFlags, nint psiItem, int hrDelete, nint psiNewlyCreated);
    [PreserveSig] int PreNewItem(uint dwFlags, nint psiDestinationFolder, nint pszNewName);
    [PreserveSig] int PostNewItem(uint dwFlags, nint psiDestinationFolder, nint pszNewName, nint pszTemplateName, uint dwFileAttributes,
                                  int hrNew, nint psiNewItem);
    [PreserveSig] int UpdateProgress(uint iWorkTotal, uint iWorkSoFar);
    [PreserveSig] int ResetTimer();
    [PreserveSig] int PauseTimer();
    [PreserveSig] int ResumeTimer();
}

/// <summary>Refuses (ERROR_CANCELLED) any delete the shell announces without TSF_DELETE_RECYCLE_IF_POSSIBLE, i.e. a permanent delete,
/// and records how the delete ended, including a successful delete whose psiNewlyCreated is NULL (fully deleted: nothing arrived in the
/// Recycle Bin).</summary>
[GeneratedComClass]
internal sealed partial class RecycleSink : IFileOperationProgressSink
{
    public bool RefusedPermanentDelete { get; private set; }
    public bool Deleted { get; private set; }
    public int DeleteResult { get; private set; }
    public bool RemovedWithoutBinItem { get; private set; }

    public int PreDeleteItem(uint dwFlags, nint psiItem)
    {
        if ((dwFlags & FileOperationCom.TsfDeleteRecycleIfPossible) != 0) return 0;
        RefusedPermanentDelete = true;
        return FileOperationCom.HResultCancelled;
    }

    public int PostDeleteItem(uint dwFlags, nint psiItem, int hrDelete, nint psiNewlyCreated)
    {
        DeleteResult = hrDelete;
        Deleted = hrDelete >= 0;
        if (hrDelete >= 0 && psiNewlyCreated == 0) RemovedWithoutBinItem = true;
        return 0;
    }

    public int StartOperations() => 0;
    public int FinishOperations(int hrResult) => 0;
    public int PreRenameItem(uint dwFlags, nint psiItem, nint pszNewName) => 0;
    public int PostRenameItem(uint dwFlags, nint psiItem, nint pszNewName, int hrRename, nint psiNewlyCreated) => 0;
    public int PreMoveItem(uint dwFlags, nint psiItem, nint psiDestinationFolder, nint pszNewName) => 0;
    public int PostMoveItem(uint dwFlags, nint psiItem, nint psiDestinationFolder, nint pszNewName, int hrMove, nint psiNewlyCreated) => 0;
    public int PreCopyItem(uint dwFlags, nint psiItem, nint psiDestinationFolder, nint pszNewName) => 0;
    public int PostCopyItem(uint dwFlags, nint psiItem, nint psiDestinationFolder, nint pszNewName, int hrCopy, nint psiNewlyCreated) => 0;
    public int PreNewItem(uint dwFlags, nint psiDestinationFolder, nint pszNewName) => 0;
    public int PostNewItem(uint dwFlags, nint psiDestinationFolder, nint pszNewName, nint pszTemplateName, uint dwFileAttributes, int hrNew,
                           nint psiNewItem) => 0;
    public int UpdateProgress(uint iWorkTotal, uint iWorkSoFar) => 0;
    public int ResetTimer() => 0;
    public int PauseTimer() => 0;
    public int ResumeTimer() => 0;
}
