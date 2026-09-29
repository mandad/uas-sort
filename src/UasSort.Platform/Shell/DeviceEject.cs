using UasSort.Platform.Io;

namespace UasSort.Platform.Shell;

/// <summary>[Eject E:] (Ref §10.5, §10.6): the PnP eject flushes the volume; no write or flush handle is ever opened on it.</summary>
public sealed unsafe class DeviceEject : IDeviceEject
{
    public EjectResult Eject(string volumeRoot)
    {
        if (volumeRoot.Length is not (2 or 3) || !char.IsAsciiLetter(volumeRoot[0]) || volumeRoot[1] != ':')
            return new EjectRefused(volumeRoot, "not a drive letter");
        var root = volumeRoot[..2] + "\\";
        if (VolumeQuery.Information(root) is null) return new EjectRefused(root, "no volume is mounted there");
        if (VolumeQuery.IsSystemBootOrPaging(root)) return new EjectRefused(root, "the system volume can't be ejected");
        if (DeviceNumber(@"\\.\" + root[..2]) is not { } number) return new EjectRefused(root, "the disk number can't be read");
        if (FindDisk(number) is not { } disk) return new EjectRefused(root, "the disk device wasn't found");

        var reason = "";
        var name = stackalloc char[260];                         // outside the loop (CA2014)
        foreach (var candidate in Candidates(disk))
        {
            name[0] = '\0';
            var result = CfgMgr32.CM_Request_Device_Eject(candidate, out var veto, name, 260, 0);
            if (result == 0 && veto == 0) return new Ejected(root);
            reason = $"Windows refused (result {result}, veto {veto}: {new string(name)})";
        }
        return new EjectRefused(root, reason);
    }

    private static IEnumerable<uint> Candidates(uint disk)
    {
        yield return disk;
        if (CfgMgr32.CM_Get_Parent(out var parent, disk, 0) == 0) yield return parent;
    }

    private static StorageDeviceNumber? DeviceNumber(string devicePath)
    {
        using var handle = Kernel32.CreateFile(devicePath, 0, Kernel32.FILE_SHARE_READ | Kernel32.FILE_SHARE_WRITE, 0, Kernel32.OPEN_EXISTING, 0, 0);
        if (handle.IsInvalid) return null;
        StorageDeviceNumber number;
        return Kernel32.DeviceIoControl(handle, Kernel32.IOCTL_STORAGE_GET_DEVICE_NUMBER, null, 0, &number,
                                        (uint)sizeof(StorageDeviceNumber), out _, 0) ? number : null;
    }

    private static uint? FindDisk(StorageDeviceNumber volume)
    {
        var set = SetupApi.SetupDiGetClassDevs(in SetupApi.GuidDevInterfaceDisk, 0, 0, SetupApi.DIGCF_PRESENT | SetupApi.DIGCF_DEVICEINTERFACE);
        if (set == -1) return null;
        var detail = stackalloc byte[2048];                        // outside the loop (CA2014)
        try
        {
            for (uint i = 0; ; i++)
            {
                var iface = new SpDeviceInterfaceData { CbSize = (uint)sizeof(SpDeviceInterfaceData) };
                if (!SetupApi.SetupDiEnumDeviceInterfaces(set, 0, in SetupApi.GuidDevInterfaceDisk, i, ref iface)) return null;
                var info = new SpDevinfoData { CbSize = (uint)sizeof(SpDevinfoData) };
                new Span<byte>(detail, 2048).Clear();
                *(uint*)detail = (uint)(IntPtr.Size == 8 ? 8 : 6);      // SP_DEVICE_INTERFACE_DETAIL_DATA_W.cbSize
                if (!SetupApi.SetupDiGetDeviceInterfaceDetail(set, ref iface, detail, 2048, out _, ref info)) continue;
                var path = new string((char*)(detail + 4));
                if (DeviceNumber(path) is { } disk && disk.DeviceNumber == volume.DeviceNumber && disk.DeviceType == volume.DeviceType)
                    return info.DevInst;
            }
        }
        finally { SetupApi.SetupDiDestroyDeviceInfoList(set); }
    }
}
