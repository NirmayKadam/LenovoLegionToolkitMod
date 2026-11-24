using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LenovoLegionToolkit.Lib.System;
using NvAPIWrapper.GPU;
using NvAPIWrapper.Native;

namespace LenovoLegionToolkit.Lib.Extensions;

public static class NVAPIExtensions
{
    private static readonly string[] Exclusions =
    [
        "dwm",
        "explorer",
    ];

    /// <summary>
    /// The processes using the GPU, from the driver's own list (id), named as Windows names them. No process is opened:
    /// this runs on every GPU check, and the list includes running games.
    /// </summary>
    public static (List<GpuProcess> All, List<GpuProcess> Filtered) GetActiveProcesses(PhysicalGPU gpu)
    {
        var allProcesses = new List<GpuProcess>();
        var filteredProcesses = new List<GpuProcess>();

        foreach (var app in GPUApi.QueryActiveApps(gpu.Handle))
        {
            if (app.ProcessId <= 0)
                continue;

            var name = ProcessName(ProcessImagePath.Get(app.ProcessId), app.ProcessName);
            if (string.IsNullOrEmpty(name))
                continue;

            var process = new GpuProcess(app.ProcessId, name);
            allProcesses.Add(process);

            if (!Exclusions.Contains(name, StringComparer.OrdinalIgnoreCase))
                filteredProcesses.Add(process);
        }

        return (allProcesses, filteredProcesses);
    }

    /// <summary>
    /// A process's name as Windows gives it (<c>Process.ProcessName</c>: the file name without ".exe"), from its image
    /// path; the driver's name only when there is no path. The driver can report a packaged app (Indra itself) under its
    /// package name, which then matched neither the "never a game" list nor the process list.
    /// </summary>
    internal static string ProcessName(string? imagePath, string? driverName)
    {
        var source = !string.IsNullOrWhiteSpace(imagePath) ? imagePath : driverName;
        if (string.IsNullOrWhiteSpace(source))
            return string.Empty;

        var fileName = Path.GetFileName(source.Trim());
        return fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? fileName[..^4] : fileName;
    }
}
