using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Utils;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;

namespace LenovoLegionToolkit.Lib.GameProfiles;

/// <summary>
/// The real Windows calls behind game profiles. Only apps and services the user listed are opened or changed;
/// finding the game uses the process list only and never opens the game's process.
/// </summary>
internal sealed class WindowsGameProfileSystem : IGameProfileSystem
{
    public IReadOnlyList<int> FindByName(string exeName)
    {
        var processes = Process.GetProcessesByName(exeName);
        try
        {
            return processes.Select(p => p.Id).ToArray();
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    public bool IsRunning(int processId, string reportedName)
    {
        // A snapshot of the process list: unlike GetProcessById, it never opens the process (a game's must not be).
        var processes = Process.GetProcesses();
        try
        {
            return processes.Any(p => p.Id == processId && GameProfileController.IsGame(reportedName, p.ProcessName));
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    public AppProcess? ReadApp(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return new AppProcess(processId, process.StartTime.ToUniversalTime(), process.GetFileName(), process.SessionId, process.PriorityClass, ReadThrottle(processId));
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't read app process. [pid={processId}]", ex);
            return null;
        }
    }

    public bool SetApp(int processId, DateTime startTimeUtc, ProcessPriorityClass priority, ThrottleState? throttle)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.StartTime.ToUniversalTime() != startTimeUtc)
                return false;   // the id now belongs to another process

            var ok = true;
            if (throttle is { } state)
                ok = WriteThrottle(processId, state);

            process.PriorityClass = priority;
            return ok;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't change app process. [pid={processId}]", ex);
            return false;
        }
    }

    public async Task<bool> CloseAppAsync(int processId, DateTime startTimeUtc, TimeSpan timeout)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.StartTime.ToUniversalTime() != startTimeUtc)
                return false;

            if (!process.CloseMainWindow())
                return false;   // no window to close: LLT doesn't end processes

            using var cts = new CancellationTokenSource(timeout);
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;   // still open (for example, asking whether to save)
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't close app. [pid={processId}]", ex);
            return false;
        }
    }

    public ServiceInfo? ReadService(string name)
    {
        try
        {
            using var service = new ServiceController(name);
            return ToInfo(service);
        }
        catch (InvalidOperationException)
        {
            return null;   // not installed
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't read service. [name={name}]", ex);
            return null;
        }
    }

    /// <summary>Reads a service's state; throws <see cref="InvalidOperationException"/> when it isn't installed.</summary>
    internal static ServiceInfo ToInfo(ServiceController service)
    {
        var status = service.Status;
        var dependents = service.DependentServices;
        try
        {
            return new ServiceInfo(
                service.ServiceName,
                service.DisplayName,
                (service.ServiceType & (ServiceType.KernelDriver | ServiceType.FileSystemDriver)) != 0,
                status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending,
                service.CanStop,
                dependents.Any(d => d.Status != ServiceControllerStatus.Stopped));
        }
        finally
        {
            foreach (var dependent in dependents)
                dependent.Dispose();
        }
    }

    public Task<bool> StopServiceAsync(string name, TimeSpan timeout) => Task.Run(() =>
    {
        try
        {
            using var service = new ServiceController(name);
            service.Stop(stopDependentServices: false);
            service.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
            return true;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't stop service. [name={name}]", ex);
            return false;
        }
    });

    public Task<bool> StartServiceAsync(string name, TimeSpan timeout) => Task.Run(() =>
    {
        try
        {
            using var service = new ServiceController(name);
            if (service.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending)
                return true;

            service.Start();
            service.WaitForStatus(ServiceControllerStatus.Running, timeout);
            return true;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't start service. [name={name}]", ex);
            return false;
        }
    });

    public async Task<PowerModeState?> GetPowerModeAsync()
    {
        try
        {
            var feature = IoCContainer.Resolve<PowerModeFeature>();
            if (!await feature.IsSupportedAsync().ConfigureAwait(false))
                return null;

            return await feature.GetStateAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't read the power mode.", ex);
            return null;
        }
    }

    public async Task<bool> SetPowerModeAsync(PowerModeState mode)
    {
        try
        {
            // The same path as the dashboard and Fn+Q: the laptop's own modes only, and not the high-power ones on
            // battery unless the user allowed that.
            await IoCContainer.Resolve<PowerModeFeature>().SetStateAsync(mode).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't switch the power mode to {mode}.", ex);
            return false;
        }
    }

    private static unsafe ThrottleState? ReadThrottle(int processId)
    {
        try
        {
            using var handle = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)processId);
            if (handle.IsInvalid)
                return null;

            var state = new PROCESS_POWER_THROTTLING_STATE { Version = PInvoke.PROCESS_POWER_THROTTLING_CURRENT_VERSION };
            if (!PInvoke.GetProcessInformation((HANDLE)handle.DangerousGetHandle(), PROCESS_INFORMATION_CLASS.ProcessPowerThrottling, &state, (uint)sizeof(PROCESS_POWER_THROTTLING_STATE)))
                return null;   // older Windows can't report it: LLT then leaves efficiency mode alone

            return new ThrottleState(state.ControlMask, state.StateMask);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't read efficiency mode. [pid={processId}]", ex);
            return null;
        }
    }

    private static unsafe bool WriteThrottle(int processId, ThrottleState throttle)
    {
        using var handle = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_SET_INFORMATION, false, (uint)processId);
        if (handle.IsInvalid)
            return false;

        var state = new PROCESS_POWER_THROTTLING_STATE
        {
            Version = PInvoke.PROCESS_POWER_THROTTLING_CURRENT_VERSION,
            ControlMask = throttle.ControlMask,
            StateMask = throttle.StateMask,
        };
        return PInvoke.SetProcessInformation((HANDLE)handle.DangerousGetHandle(), PROCESS_INFORMATION_CLASS.ProcessPowerThrottling, &state, (uint)sizeof(PROCESS_POWER_THROTTLING_STATE));
    }
}
