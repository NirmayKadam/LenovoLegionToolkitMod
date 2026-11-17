using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.GameProfiles;

/// <summary>What the Game profiles window offers to pick from: running apps with a window, and running services.</summary>
public static class GameProfileCatalog
{
    /// <summary>
    /// File names (no ".exe") of running apps that have a window in the user's session, from the process list
    /// and the window list: no process is opened. LLT itself isn't offered.
    /// </summary>
    public static IReadOnlyList<string> RunningApps()
    {
        var processes = Process.GetProcesses();
        try
        {
            return processes
                .Where(p => SafeHasWindow(p))
                .Select(p => p.ProcessName)
                .Where(n => !string.Equals(n, "Lenovo Legion Toolkit", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
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
