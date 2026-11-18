using System;
using System.Diagnostics;

namespace LenovoLegionToolkit.Lib.Utils;

/// <summary>
/// Whether MSI Afterburner is running. While it runs, it manages the GPU's clocks and voltage, so LLT doesn't send
/// its own GPU overclock or P-state lock. Read from the process list only: no process is opened.
/// </summary>
public static class AfterburnerDetector
{
    public const string ProcessName = "MSIAfterburner";

    public static bool IsRunning()
    {
        try
        {
            var processes = Process.GetProcessesByName(ProcessName);
            foreach (var process in processes)
                process.Dispose();
            return processes.Length > 0;
        }
        catch (Exception ex)
        {
            // The safer answer: LLT then leaves the GPU clocks alone.
            Log.Instance.Trace($"Couldn't check for MSI Afterburner; treating it as running.", ex);
            return true;
        }
    }
}
