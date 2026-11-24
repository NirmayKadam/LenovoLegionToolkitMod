using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.System;

/// <summary>
/// The file a running process was started from, read by process id from Windows' process list
/// (<c>NtQuerySystemInformation</c>, <c>SystemProcessIdInformation</c>). No process is opened: Indra must never open a
/// game's process, which kernel anti-cheats watch for, and this also works for protected processes.
/// </summary>
public static class ProcessImagePath
{
    private const int SystemProcessIdInformationClass = 88;
    private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

    /// <summary>Room for the name on the first try (bytes); Windows says how much it needs if that isn't enough.</summary>
    private const int InitialBufferBytes = 1024;

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemProcessIdInformation
    {
        public IntPtr ProcessId;
        public UnicodeString ImageName;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int systemInformationClass, ref SystemProcessIdInformation systemInformation, int systemInformationLength, IntPtr returnLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint QueryDosDevice(string deviceName, StringBuilder targetPath, int maxLength);

    /// <summary>Device names (<c>\Device\HarddiskVolume3</c>) by drive (<c>C:</c>); replaced whole when rebuilt.</summary>
    private static volatile IReadOnlyDictionary<string, string> _drivesByDevice = new Dictionary<string, string>();

    /// <summary>The process's file path (<c>C:\...</c>), or null when the process is gone or the path can't be read.</summary>
    public static string? Get(int processId)
    {
        if (processId <= 0)
            return null;

        try
        {
            var devicePath = ReadDevicePath(processId);
            return devicePath is null ? null : ToDrivePath(devicePath);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or OutOfMemoryException)
        {
            Log.Instance.Trace($"Couldn't read a process's file path. [pid={processId}]", ex);
            return null;
        }
    }

    /// <summary>The image name as Windows keeps it: <c>\Device\HarddiskVolume3\Program Files\...</c>.</summary>
    private static string? ReadDevicePath(int processId)
    {
        var size = InitialBufferBytes;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                var info = new SystemProcessIdInformation
                {
                    ProcessId = processId,
                    ImageName = new UnicodeString { Length = 0, MaximumLength = (ushort)size, Buffer = buffer },
                };

                var status = NtQuerySystemInformation(SystemProcessIdInformationClass, ref info, Marshal.SizeOf<SystemProcessIdInformation>(), IntPtr.Zero);
                if (status == StatusInfoLengthMismatch && info.ImageName.MaximumLength > size)
                {
                    size = info.ImageName.MaximumLength;
                    continue;
                }

                // Any other failure: the process has ended (or the id was never used).
                if (status < 0 || info.ImageName.Length == 0)
                    return null;

                return Marshal.PtrToStringUni(buffer, info.ImageName.Length / sizeof(char));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        return null;
    }

    /// <summary>Turns a device path into a drive-letter (or network) path; null when no drive matches.</summary>
    internal static string? ToDrivePath(string devicePath)
    {
        const string network = @"\Device\Mup\";
        if (devicePath.StartsWith(network, StringComparison.OrdinalIgnoreCase))
            return @"\\" + devicePath[network.Length..];

        if (Match(devicePath, _drivesByDevice) is { } path)
            return path;

        // A miss may mean a drive was just added, so the drive letters are read again, but at most every 30 s. Some
        // names never map to a letter (the Registry and Memory Compression processes, volumes without a letter), and
        // each of them used to ask Windows about all 26 letters again, at every process start and every scan.
        var now = Environment.TickCount64;
        if (!ShouldRereadDrives(Interlocked.Read(ref _drivesReadAtMs), now))
            return null;

        Interlocked.Exchange(ref _drivesReadAtMs, now);
        return Match(devicePath, _drivesByDevice = ReadDrives());
    }

    /// <summary>How often a miss may read the drive letters again.</summary>
    internal static readonly TimeSpan RereadDrivesAfter = TimeSpan.FromSeconds(30);

    private static long _drivesReadAtMs = long.MinValue / 2;

    /// <summary>Whether a miss at <paramref name="nowMs"/> reads the drive letters again, last read at <paramref name="lastReadMs"/>.</summary>
    internal static bool ShouldRereadDrives(long lastReadMs, long nowMs) => nowMs - lastReadMs >= RereadDrivesAfter.TotalMilliseconds;

    private static string? Match(string devicePath, IReadOnlyDictionary<string, string> drivesByDevice)
    {
        foreach (var (device, drive) in drivesByDevice)
        {
            if (devicePath.Length > device.Length
                && devicePath[device.Length] == '\\'
                && devicePath.StartsWith(device, StringComparison.OrdinalIgnoreCase))
                return drive + devicePath[device.Length..];
        }

        return null;
    }

    /// <summary>Which device each drive letter is now (drives come and go, so this is read again on a miss).</summary>
    private static Dictionary<string, string> ReadDrives()
    {
        var drives = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var target = new StringBuilder(1024);
        for (var letter = 'A'; letter <= 'Z'; letter++)
        {
            var drive = $"{letter}:";
            target.Clear();
            if (QueryDosDevice(drive, target, target.Capacity) != 0 && target.Length > 0)
                drives.TryAdd(target.ToString(), drive);
        }

        return drives;
    }
}
