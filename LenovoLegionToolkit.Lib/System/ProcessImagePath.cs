using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
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

        return Match(devicePath, _drivesByDevice) ?? Match(devicePath, _drivesByDevice = ReadDrives());
    }

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
