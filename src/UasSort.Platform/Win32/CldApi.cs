using System.Runtime.InteropServices;

namespace UasSort.Platform.Win32;

internal static unsafe partial class CldApi
{
    public const int CF_SYNC_ROOT_INFO_BASIC = 0;
    public const int HRESULT_ERROR_MORE_DATA = unchecked((int)0x800700EA);

    [LibraryImport("cldapi.dll", EntryPoint = "CfGetSyncRootInfoByPath", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int CfGetSyncRootInfoByPath(string filePath, int infoClass, void* infoBuffer, uint infoBufferLength, uint* returnedLength);
}
