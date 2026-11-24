using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using LenovoLegionToolkit.Lib.GameProfiles;

namespace LenovoLegionToolkit.Lib.Automation.Pipeline.Triggers;

/// <summary>What the "process started" and "process stopped" triggers have in common.</summary>
internal static class ProcessTriggerMatching
{
    /// <summary>
    /// Whether the started or stopped process is one the trigger lists: the same program, or the same name ignoring
    /// case. Windows' start event may cut a long name to 15 characters, so a cut name matches too.
    /// </summary>
    public static bool IsListed(IEnumerable<ProcessInfo> listed, ProcessInfo process) =>
        listed.Any(p => p.Equals(process) || GameProfileController.IsGame(process.Name, p.Name));

    /// <summary>Whether any listed program runs now (one look at the process list; no process is opened).</summary>
    public static bool IsAnyRunning(IEnumerable<ProcessInfo> listed)
    {
        foreach (var name in listed.Select(p => p.Name).Distinct())
        {
            var processes = Process.GetProcessesByName(name);
            foreach (var process in processes)
                process.Dispose();
            if (processes.Length > 0)
                return true;
        }

        return false;
    }
}
