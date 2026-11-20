using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.System.Management;

namespace LenovoLegionToolkit.Lib.System;

/// <summary>What Windows does when the battery reaches its critical level, and whether it can.</summary>
public sealed record BatteryActionInfo(bool HibernateAvailable, int CriticalActionOnBattery, int CriticalLevelPercent);

/// <summary>Whether Windows installs updates by itself, and when the last Windows update was installed.</summary>
public sealed record UpdatesInfo(bool AutomaticUpdatesOff, DateTime? LastInstalledDate);

/// <summary>Defender's real-time protection, and other antivirus programs that are switched on.</summary>
public sealed record VirusProtectionInfo(bool DefenderRealTimeOn, IReadOnlyList<string> OtherAntivirusOn);

/// <summary>Free and total space of the drive Windows is installed on.</summary>
public sealed record SystemDriveInfo(string Name, long FreeBytes, long TotalBytes);

/// <summary>
/// Read-only questions to Windows for the Health page. Each method returns what Windows says or throws; nothing
/// here changes a setting.
/// </summary>
public static class WindowsHealth
{
    /// <summary>The power setting values of the critical battery action.</summary>
    public const int ActionNothing = 0, ActionSleep = 1, ActionHibernate = 2, ActionShutDown = 3;

    private static readonly Guid BatterySubgroup = new("e73a048d-bf27-4f12-9731-8b2076e8891f");
    private static readonly Guid CriticalBatteryAction = new("637ea02f-bbcb-4015-8e2c-a1c7b9c0b546");
    private static readonly Guid CriticalBatteryLevel = new("9a66d8d7-4ff7-4ef9-b5a2-5a326ca2a469");

    /// <summary>The critical battery action and level of the active power plan, and whether hibernation can happen.</summary>
    public static BatteryActionInfo GetBatteryAction()
    {
        var scheme = GetActiveScheme();
        return new BatteryActionInfo(IsHibernateAvailable(), (int)ReadDcValue(scheme, CriticalBatteryAction), (int)ReadDcValue(scheme, CriticalBatteryLevel));
    }

    /// <summary>
    /// True when hibernation can be used: the firmware supports it and the hibernation file exists (it doesn't
    /// after <c>powercfg /h off</c>).
    /// </summary>
    public static bool IsHibernateAvailable()
    {
        // SYSTEM_POWER_CAPABILITIES starts with BOOLEAN fields: SystemS4 at offset 6, HiberFilePresent at offset 8.
        // The buffer is larger than the whole structure.
        var buffer = Marshal.AllocHGlobal(512);
        try
        {
            for (var i = 0; i < 512; i++)
                Marshal.WriteByte(buffer, i, 0);

            if (!GetPwrCapabilities(buffer))
                throw new InvalidOperationException($"GetPwrCapabilities failed (error {Marshal.GetLastWin32Error()}).");

            return Marshal.ReadByte(buffer, 6) != 0 && Marshal.ReadByte(buffer, 8) != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Automatic updates are off by policy or because the Windows Update service is disabled.</summary>
    public static async Task<UpdatesInfo> GetUpdatesAsync()
    {
        var policyOff = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate", null) is int noAuto && noAuto == 1;
        var serviceOff = GetStartMode("wuauserv") == ServiceStartMode.Disabled;

        var dates = await WMI.QueryAsync("root\\CIMV2", "SELECT InstalledOn FROM Win32_QuickFixEngineering",
            o => ParseInstalledOn(o["InstalledOn"] as string)).ConfigureAwait(false);

        return new UpdatesInfo(policyOff || serviceOff, dates.Where(d => d is not null).Max());
    }

    /// <summary>
    /// Win32_QuickFixEngineering's InstalledOn is a date in US order ("11/11/2026"), sometimes "yyyyMMdd", and for some
    /// old updates a 64-bit FILETIME in hex. Anything else is unknown.
    /// </summary>
    public static DateTime? ParseInstalledOn(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        text = text.Trim();

        if (DateTime.TryParseExact(text, ["M/d/yyyy", "MM/dd/yyyy", "yyyyMMdd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date.Date;

        if (text.Length == 16 && long.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var fileTime) && fileTime > 0)
        {
            try
            {
                return DateTime.FromFileTimeUtc(fileTime).Date;
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>Defender's real-time protection, and the other antivirus programs Windows Security lists as on.</summary>
    public static async Task<VirusProtectionInfo> GetVirusProtectionAsync()
    {
        var defender = await WMI.QueryAsync("root\\Microsoft\\Windows\\Defender", "SELECT RealTimeProtectionEnabled FROM MSFT_MpComputerStatus",
            o => o["RealTimeProtectionEnabled"] is true).ConfigureAwait(false);

        var others = await WMI.QueryAsync("root\\SecurityCenter2", "SELECT displayName, productState FROM AntiVirusProduct",
            o => (Name: o["displayName"] as string ?? string.Empty, State: Convert.ToUInt32(o["productState"] ?? 0u, CultureInfo.InvariantCulture))).ConfigureAwait(false);

        return new VirusProtectionInfo(
            defender.Count > 0 && defender[0],
            others.Where(a => IsAntivirusOn(a.State) && !IsDefender(a.Name)).Select(a => a.Name).ToList());
    }

    /// <summary>Windows Security's productState: the second byte is 0x10 or 0x11 while the scanner is on.</summary>
    public static bool IsAntivirusOn(uint productState) => ((productState >> 8) & 0xF0) == 0x10;

    private static bool IsDefender(string name) =>
        name.Contains("Windows Defender", StringComparison.OrdinalIgnoreCase) || name.Contains("Microsoft Defender", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when Windows' Security and Maintenance notifications are switched off for this user.</summary>
    public static bool AreSecurityNotificationsOff() =>
        Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\Windows.SystemToast.SecurityAndMaintenance", "Enabled", null) is int enabled && enabled == 0;

    /// <summary>Memory integrity (HVCI) is running now, as Device Guard reports it.</summary>
    public static async Task<bool> IsMemoryIntegrityRunningAsync()
    {
        var running = await WMI.QueryAsync("root\\Microsoft\\Windows\\DeviceGuard", "SELECT SecurityServicesRunning FROM Win32_DeviceGuard",
            o => o["SecurityServicesRunning"] as uint[] ?? []).ConfigureAwait(false);

        // 2 = hypervisor enforced code integrity.
        return running.Any(services => services.Contains(2u));
    }

    /// <summary>The start mode of a service, or null when it isn't installed.</summary>
    public static ServiceStartMode? GetStartMode(string serviceName)
    {
        try
        {
            using var service = new ServiceController(serviceName);
            return service.StartType;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public static SystemDriveInfo GetSystemDrive()
    {
        var root = Path.GetPathRoot(Environment.SystemDirectory) ?? throw new InvalidOperationException("No system drive.");
        var drive = new DriveInfo(root);
        return new SystemDriveInfo(drive.Name, drive.AvailableFreeSpace, drive.TotalSize);
    }

    private static Guid GetActiveScheme()
    {
        var result = PowerGetActiveScheme(IntPtr.Zero, out var pointer);
        if (result != 0)
            throw new InvalidOperationException($"PowerGetActiveScheme failed ({result}).");

        try
        {
            return Marshal.PtrToStructure<Guid>(pointer);
        }
        finally
        {
            LocalFree(pointer);
        }
    }

    private static uint ReadDcValue(Guid scheme, Guid setting)
    {
        var subgroup = BatterySubgroup;
        var result = PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out var value);
        if (result != 0)
            throw new InvalidOperationException($"PowerReadDCValueIndex failed ({result}).");
        return value;
    }

    [DllImport("PowrProf.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool GetPwrCapabilities(IntPtr capabilities);

    [DllImport("PowrProf.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr rootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("PowrProf.dll")]
    private static extern uint PowerReadDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid, ref Guid powerSettingGuid, out uint valueIndex);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
