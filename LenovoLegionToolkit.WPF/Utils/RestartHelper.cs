using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.WPF.Utils;

/// <summary>
/// Restarting Indra after a settings restore or reset. The new copy is told which process it replaces and waits for
/// it to end before its single-instance check; it used to start while the old one was still shutting down, find it
/// "already running", hand over and exit, so nothing came back.
/// </summary>
internal static class RestartHelper
{
    public const string AfterRestartArgument = "--after-restart=";

    /// <summary>How long the new copy waits for the old one (its shutdown puts game profiles and services back).</summary>
    public static readonly TimeSpan WaitLimit = TimeSpan.FromMinutes(2);

    /// <summary>The process id passed with <see cref="AfterRestartArgument"/>, or null when there is none.</summary>
    public static int? ParseAfterRestartPid(IEnumerable<string> args) =>
        args.Where(a => a.StartsWith(AfterRestartArgument, StringComparison.OrdinalIgnoreCase))
            .Select(a => int.TryParse(a.AsSpan(AfterRestartArgument.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var pid) && pid > 0 ? pid : (int?)null)
            .FirstOrDefault(pid => pid is not null);

    /// <summary>How the new copy is started: the same program, told which process it replaces.</summary>
    public static ProcessStartInfo StartInfo(string exePath, int processId) => new(exePath)
    {
        UseShellExecute = true,
        Arguments = $"{AfterRestartArgument}{processId.ToString(CultureInfo.InvariantCulture)}",
    };

    /// <summary>
    /// Starts the new copy. False (logged) when Windows couldn't start it; the caller then keeps this copy running.
    /// </summary>
    public static bool TryStartNewCopy(string exePath, int processId)
    {
        try
        {
            using var started = Process.Start(StartInfo(exePath, processId));
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            Log.Instance.Trace($"Couldn't start the new copy of Indra. [path={exePath}]", ex);
            return false;
        }
    }

    /// <summary>
    /// Waits until the process with <paramref name="processId"/> and this program's name has ended. True when it has
    /// (or was already gone, or the id now belongs to another program); false when it is still running after
    /// <paramref name="limit"/>.
    /// </summary>
    public static bool WaitForExit(int processId, string processName, TimeSpan limit)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!string.Equals(process.ProcessName, processName, StringComparison.OrdinalIgnoreCase))
                return true;

            return process.WaitForExit(limit);
        }
        catch (ArgumentException)
        {
            return true;   // not running
        }
        catch (InvalidOperationException)
        {
            return true;   // ended meanwhile
        }
    }
}
