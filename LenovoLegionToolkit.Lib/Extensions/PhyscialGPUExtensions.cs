using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NvAPIWrapper.GPU;
using NvAPIWrapper.Native;

namespace LenovoLegionToolkit.Lib.Extensions;

public static class NVAPIExtensions
{
    private static readonly string[] Exclusions =
    [
        "dwm.exe",
        "explorer.exe",
    ];

    /// <summary>
    /// The processes using the GPU, from the driver's own list (id and name). No process is opened: this runs on
    /// every GPU check, and the list includes running games.
    /// </summary>
    public static (List<GpuProcess> All, List<GpuProcess> Filtered) GetActiveProcesses(PhysicalGPU gpu)
    {
        var allProcesses = new List<GpuProcess>();
        var filteredProcesses = new List<GpuProcess>();

        foreach (var app in GPUApi.QueryActiveApps(gpu.Handle))
        {
            var fileName = Path.GetFileName(app.ProcessName ?? string.Empty);
            if (app.ProcessId <= 0 || string.IsNullOrEmpty(fileName))
                continue;

            var process = new GpuProcess(app.ProcessId, Path.GetFileNameWithoutExtension(fileName));
            allProcesses.Add(process);

            if (!Exclusions.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                filteredProcesses.Add(process);
        }

        return (allProcesses, filteredProcesses);
    }
}
