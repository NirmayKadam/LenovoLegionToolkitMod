using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.GameProfiles;

/// <summary>What the Game profiles window offers to pick from: running apps with a window, and running services.</summary>
public static class GameProfileCatalog
{
    /// <summary>
    /// File names (no ".exe") of running apps with a window in the user's session that a game profile may touch
    /// (<see cref="AppRules.FilterApps"/>). Windows' own apps, LLT and the names in <paramref name="excludeNames"/>
    /// (profiled games, apps already listed) are dropped by name first; only the remaining apps are opened, for
    /// "query limited information", to drop those in the Windows folder.
    /// </summary>
    public static IReadOnlyList<string> RunningApps(IEnumerable<string> excludeNames)
    {
        var processes = Process.GetProcesses();
        try
        {
            var windowed = processes.Where(SafeHasWindow).ToList();
            return AppRules.FilterApps(
                windowed.Select(p => p.ProcessName),
                excludeNames,
                name => windowed.Where(p => string.Equals(p.ProcessName, name, StringComparison.OrdinalIgnoreCase)).Select(p => p.GetFileName()).FirstOrDefault(path => path is not null));
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    private static bool SafeHasWindow(Process process)
    {
        try
        {
            return process.SessionId != 0 && process.MainWindowHandle != IntPtr.Zero;
        }
        catch
        {
            return false;   // ended meanwhile
        }
    }

    /// <summary>Running Windows services (not drivers), with what LLT knows about each.</summary>
    public static IReadOnlyList<ServiceInfo> RunningServices()
    {
        var services = ServiceController.GetServices();
        var result = new List<ServiceInfo>();
        try
        {
            foreach (var service in services)
            {
                try
                {
                    if (service.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending)
                        result.Add(WindowsGameProfileSystem.ToInfo(service));
                }
                catch (Exception ex)
                {
                    Log.Instance.Trace($"Couldn't read service {service.ServiceName}.", ex);
                }
            }
        }
        finally
        {
            foreach (var service in services)
                service.Dispose();
        }

        return result.OrderBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
