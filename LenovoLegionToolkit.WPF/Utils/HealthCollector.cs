using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.WPF.Utils;

/// <summary>
/// Reads the Health page's inputs from Windows. Each read runs on its own background thread with a time limit, so a
/// stuck WMI or service query can't hold anything up; a read that fails or takes too long gives null.
/// </summary>
public static class HealthCollector
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    public static async Task<HealthInputs> CollectAsync()
    {
        var batteryAction = ReadAsync("battery action", () => Task.Run(WindowsHealth.GetBatteryAction));
        var updates = ReadAsync("Windows Update", WindowsHealth.GetUpdatesAsync);
        var virus = ReadAsync("virus protection", WindowsHealth.GetVirusProtectionAsync);
        var notifications = ReadValueAsync("security notifications", () => Task.Run(WindowsHealth.AreSecurityNotificationsOff));
        var memoryIntegrity = ReadValueAsync("memory integrity", WindowsHealth.IsMemoryIntegrityRunningAsync);
        var services = ReadAsync("services", () => Task.Run(() =>
            (IReadOnlyList<string>)HealthReport.WatchedServices
                .Where(name => WindowsHealth.GetStartMode(name) == ServiceStartMode.Disabled)
                .ToList()));
        var gpuErrors = ReadValueAsync("GPU driver errors", () => Task.Run(() =>
            IoCContainer.Resolve<GpuDriverErrorMonitor>().ReadSince(DateTime.UtcNow - TimeSpan.FromDays(1))?.Count
            ?? throw new InvalidOperationException("The System log couldn't be read.")));
        var drive = ReadAsync("system drive", () => Task.Run(WindowsHealth.GetSystemDrive));
        var battery = ReadAsync("battery wear", () => Task.Run(() =>
        {
            var info = Battery.GetBatteryInformation();
            return new BatteryWearInfo(info.DesignCapacity, info.FullChargeCapacity);
        }));

        return new HealthInputs(
            await batteryAction.ConfigureAwait(false),
            await updates.ConfigureAwait(false),
            await virus.ConfigureAwait(false),
            await notifications.ConfigureAwait(false),
            await memoryIntegrity.ConfigureAwait(false),
            await services.ConfigureAwait(false),
            await gpuErrors.ConfigureAwait(false),
            await drive.ConfigureAwait(false),
            await battery.ConfigureAwait(false));
    }

    private static async Task<T?> ReadAsync<T>(string what, Func<Task<T>> read) where T : class
    {
        try
        {
            return await read().WaitAsync(Limit).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Health check couldn't read {what}.", ex);
            return null;
        }
    }

    private static async Task<T?> ReadValueAsync<T>(string what, Func<Task<T>> read) where T : struct
    {
        try
        {
            return await read().WaitAsync(Limit).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Health check couldn't read {what}.", ex);
            return null;
        }
    }
}
