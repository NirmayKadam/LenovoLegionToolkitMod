using System.Diagnostics;
using LenovoLegionToolkit.Lib.System;

namespace LenovoLegionToolkit.Lib.Extensions;

public static class ProcessExtensions
{
    /// <summary>
    /// The file the process was started from, or null. Read from Windows' process list without opening the process
    /// (<see cref="ProcessImagePath"/>). It used to fall back to an all-access open and to reading the process's module
    /// list when a limited open was refused, which is exactly what an anti-cheat refuses and watches for.
    /// </summary>
    public static string? GetFileName(this Process process) => ProcessImagePath.Get(process.Id);
}
