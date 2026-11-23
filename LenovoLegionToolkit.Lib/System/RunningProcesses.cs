using System.Diagnostics;

namespace LenovoLegionToolkit.Lib.System;

/// <summary>Whether a program is running, from the process list (nothing is opened; every Process object is disposed).</summary>
public static class RunningProcesses
{
    /// <param name="exeName">The file name without ".exe", as Windows names processes.</param>
    public static bool IsRunning(string exeName)
    {
        if (string.IsNullOrWhiteSpace(exeName))
            return false;

        var processes = Process.GetProcessesByName(exeName);
        foreach (var process in processes)
            process.Dispose();

        return processes.Length > 0;
    }
}
