using System;
using LenovoLegionToolkit.Lib.Resources;
using LenovoLegionToolkit.Lib.System;
using NvAPIWrapper.Native.GPU;

namespace LenovoLegionToolkit.Lib.Utils;

/// <summary>Why the NVIDIA GPU isn't running faster right now, as its driver reports it.</summary>
public enum GpuLimit
{
    None,
    Thermal,
    Power,
    Battery,
    App,
    Unknown
}

/// <summary>
/// Reads the GPU's "performance decrease" reasons (NVAPI). The driver reports thermal and power limits, running on
/// battery, and a limit set by an app; it has no voltage reason. When several apply, the one that matters most for
/// tuning is shown: thermal, then power, then battery, then an app's limit.
/// </summary>
public static class GpuLimitReading
{
    /// <summary>The current limit, or null when there is no NVIDIA GPU or it can't be read (for example while it sleeps).</summary>
    public static GpuLimit? Read()
    {
        var gpu = NVAPI.GetGPU();
        return gpu is null ? null : NVAPI.GetPerformanceDecreaseReason(gpu) is { } reason ? From(reason) : null;
    }

    public static GpuLimit From(PerformanceDecreaseReason reason)
    {
        if (reason.HasFlag(PerformanceDecreaseReason.ThermalProtection))
            return GpuLimit.Thermal;
        if (reason.HasFlag(PerformanceDecreaseReason.PowerControl) || reason.HasFlag(PerformanceDecreaseReason.InsufficientPower))
            return GpuLimit.Power;
        if (reason.HasFlag(PerformanceDecreaseReason.AC_BATT))
            return GpuLimit.Battery;
        if (reason.HasFlag(PerformanceDecreaseReason.ApiTriggered))
            return GpuLimit.App;
        if (reason.HasFlag(PerformanceDecreaseReason.Unknown))
            return GpuLimit.Unknown;
        return GpuLimit.None;
    }

    public static string Label(GpuLimit limit) => limit switch
    {
        GpuLimit.None => Resource.GpuLimit_None,
        GpuLimit.Thermal => Resource.GpuLimit_Thermal,
        GpuLimit.Power => Resource.GpuLimit_Power,
        GpuLimit.Battery => Resource.GpuLimit_Battery,
        GpuLimit.App => Resource.GpuLimit_App,
        GpuLimit.Unknown => Resource.GpuLimit_Unknown,
        _ => throw new ArgumentOutOfRangeException(nameof(limit), limit, null)
    };
}
