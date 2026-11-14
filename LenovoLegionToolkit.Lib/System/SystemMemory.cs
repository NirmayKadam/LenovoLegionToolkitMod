using System.Runtime.InteropServices;

namespace LenovoLegionToolkit.Lib.System;

/// <summary>
/// Physical memory use from Windows itself (<c>GlobalMemoryStatusEx</c>): no driver, no sensor library.
/// Used by the OSD when detailed sensors are off or paused for game-safe mode.
/// </summary>
public static class SystemMemory
{
    private const double BytesPerGb = 1024d * 1024 * 1024;

    /// <summary>Load in percent and used/total in GB, or null if Windows doesn't answer.</summary>
    public static (double LoadPercent, double UsedGb, double TotalGb)? GetStatus()
    {
        var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status) || status.ullTotalPhys == 0)
            return null;

        return FromBytes(status.dwMemoryLoad, status.ullTotalPhys, status.ullAvailPhys);
    }

    /// <summary>Turns Windows' byte counts into the OSD's units (exposed for tests).</summary>
    public static (double LoadPercent, double UsedGb, double TotalGb) FromBytes(uint loadPercent, ulong totalBytes, ulong availableBytes)
    {
        var used = totalBytes > availableBytes ? totalBytes - availableBytes : 0;
        return (loadPercent, used / BytesPerGb, totalBytes / BytesPerGb);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
