using System;
using LenovoLegionToolkit.Lib.Utils;
using Windows.Win32;
using Windows.Win32.Storage.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace LenovoLegionToolkit.Lib.System;

public static class Drivers
{
    public const uint IOCTL_ENERGY_BATTERY_INFORMATION = 0x83102138;
    public const uint IOCTL_ENERGY_SETTINGS = 0x831020E8;
    public const uint IOCTL_ENERGY_BATTERY_CHARGE_MODE = 0x831020F8;
    public const uint IOCTL_ENERGY_BATTERY_NIGHT_CHARGE = 0x83102150;
    public const uint IOCTL_ENERGY_KEYBOARD = 0x83102144;
    public const uint IOCTL_ENERGY_SMART_POWER = 0x8310213C;
    public const uint IOCTL_KEY_WAIT_HANDLE = 0x831020D8;
    public const uint IOCTL_KEY_VALUE = 0x831020CC;
    public const uint IOCTL_DYTC = 0x831E003C;

    public const uint IOCTL_IBMPMDRV_CHARGE_THRESHOLD_MODE = 0x22261C;
    public const uint IOCTL_IBMPMDRV_CHARGE_THRESHOLD_START_STATUS = 0x22262C;
    public const uint IOCTL_IBMPMDRV_CHARGE_THRESHOLD_START = 0x222630;
    public const uint IOCTL_IBMPMDRV_CHARGE_THRESHOLD_STOP_STATUS = 0x222634;
    public const uint IOCTL_IBMPMDRV_CHARGE_THRESHOLD_STOP = 0x222638;

    private static readonly object Lock = new();

    private static SafeFileHandle? _energy;
    private static SafeFileHandle? _ibmPmDrv;

    public static SafeFileHandle GetEnergy()
    {
        if (_energy is not null)
            return _energy;

        lock (Lock)
        {
            if (_energy is not null)
                return _energy;

            var handle = PInvoke.CreateFile(@"\\.\EnergyDrv",
                (uint)FILE_ACCESS_RIGHTS.FILE_READ_DATA | (uint)FILE_ACCESS_RIGHTS.FILE_WRITE_DATA,
                FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE,
                null,
                FILE_CREATION_DISPOSITION.OPEN_EXISTING,
                FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_NORMAL,
                null);

            if (handle.IsInvalid)
                throw new InvalidOperationException("handle is invalid");

            _energy = handle;
        }

        return _energy;
    }

    /// <summary>
    /// Forgets the Lenovo driver handle after Windows said it is no longer valid (the driver was restarted or updated
    /// while Indra ran), so the next request opens a fresh one instead of failing until Indra restarts. The old handle
    /// is not closed: another thread may still be using it, and closing it under that thread is worse than keeping one
    /// stale handle until exit.
    /// </summary>
    public static void ForgetEnergy(SafeFileHandle stale)
    {
        lock (Lock)
        {
            if (!ReferenceEquals(_energy, stale))
                return;

            _energy = null;
        }

        Log.Instance.Trace($"Lenovo driver handle no longer valid; it will be opened again on the next request.");
    }

    /// <summary>Errors that mean the handle itself is gone (rather than the request being refused).</summary>
    public static bool IsStaleHandleError(int win32Error) => win32Error is 6 /* ERROR_INVALID_HANDLE */ or 1167 /* ERROR_DEVICE_NOT_CONNECTED */;
    public static SafeFileHandle GetIbmPmDrv()
    {
        if (_ibmPmDrv is not null)
            return _ibmPmDrv;

        lock (Lock)
        {
            if (_ibmPmDrv is not null)
                return _ibmPmDrv;

            var handle = PInvoke.CreateFile(@"\\.\IBMPmDrv",
                (uint)FILE_ACCESS_RIGHTS.FILE_READ_DATA,
                FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE,
                null,
                FILE_CREATION_DISPOSITION.OPEN_EXISTING,
                FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_NORMAL,
                null);

            if (handle.IsInvalid)
                throw new InvalidOperationException("handle is invalid");

            _ibmPmDrv = handle;
        }

        return _ibmPmDrv;
    }
}
