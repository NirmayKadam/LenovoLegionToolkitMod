using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers.Sensors;

namespace LenovoLegionToolkit.WPF.Windows.Osd;

/// <summary>
/// Combines the OSD's data sources into one <see cref="SensorSnapshot"/>.
/// <list type="bullet">
/// <item><b>Detailed</b> (hardware sensors on, not in game-safe mode): readings from LibreHardwareMonitor,
/// fans and chipset from Lenovo's WMI sensors, exactly as before.</item>
/// <item><b>Driver-free</b> (sensors off, or game-safe mode): CPU/GPU temperature, usage, clock and fans from
/// Lenovo's WMI sensors, memory from Windows. Readings only the driver provides (power, voltage, per-core-type
/// clocks, VRAM, memory and disk temperatures) are reported as missing (-1), which the OSD shows as "-".</item>
/// </list>
/// </summary>
public static class OsdSnapshotBuilder
{
    /// <summary>A clock of 0 isn't a real reading (no running chip reports it), so it counts as missing.</summary>
    private static double Positive(int? value) => value is > 0 ? value.Value : -1;

    public static SensorSnapshot Build(HardwareSensorSnapshot? detailed, SensorsData? lenovo, (double LoadPercent, double UsedGb, double TotalGb)? memory, bool showAverageFrequency)
    {
        var main = lenovo;

        if (detailed is null)
        {
            return new SensorSnapshot
            {
                CpuUsage = main?.CPU.Utilization ?? -1,
                CpuFrequency = Positive(main?.CPU.CoreClock),
                CpuPClock = -1,
                CpuEClock = -1,
                CpuTemp = main?.CPU.Temperature ?? -1,
                CpuPower = -1,
                CpuVoltage = -1,
                CpuFanSpeed = main?.CPU.FanSpeed ?? -1,

                GpuUsage = main?.GPU.Utilization ?? -1,
                GpuFrequency = Positive(main?.GPU.CoreClock),
                GpuTemp = main?.GPU.Temperature ?? -1,
                GpuVramUsage = -1,
                GpuVramUsed = -1,
                GpuVramTotal = -1,
                GpuVramTemp = -1,
                GpuPower = -1,
                GpuFanSpeed = main?.GPU.FanSpeed ?? -1,

                MemUsage = memory?.LoadPercent ?? -1,
                MemUsed = memory?.UsedGb ?? -1,
                MemTotal = memory?.TotalGb ?? -1,
                MemTemp = -1,

                PchTemp = main?.PCH.Temperature ?? -1,
                PchFanSpeed = main?.PCH.FanSpeed ?? -1,

                Disk1Temp = -1,
                Disk2Temp = -1,
            };
        }

        var g = detailed;
        return new SensorSnapshot
        {
            CpuUsage = g[SensorItem.CpuUtilization],
            CpuFrequency = showAverageFrequency ? g[SensorItem.CpuAverageFrequency] : g[SensorItem.CpuMaxFrequency],
            CpuPClock = showAverageFrequency ? g[SensorItem.CpuPAverageFrequency] : g[SensorItem.CpuPMaxFrequency],
            CpuEClock = showAverageFrequency ? g[SensorItem.CpuEAverageFrequency] : g[SensorItem.CpuEMaxFrequency],
            CpuTemp = g[SensorItem.CpuTemperature],
            CpuPower = g[SensorItem.CpuPower],
            CpuVoltage = g[SensorItem.CpuVoltage],
            CpuFanSpeed = main?.CPU.FanSpeed ?? -1,

            GpuUsage = g[SensorItem.GpuUtilization],
            GpuFrequency = g[SensorItem.GpuFrequency],
            GpuTemp = g[SensorItem.GpuCoreTemperature],
            GpuVramUsage = g[SensorItem.GpuVramUtilization],
            GpuVramUsed = g[SensorItem.GpuVramUsed],
            GpuVramTotal = g[SensorItem.GpuVramTotal],
            GpuVramTemp = g[SensorItem.GpuVramTemperature],
            GpuPower = g[SensorItem.GpuPower],
            GpuFanSpeed = main?.GPU.FanSpeed ?? -1,

            MemUsage = g[SensorItem.MemoryUtilization],
            MemUsed = g[SensorItem.MemoryUsed],
            MemTotal = g[SensorItem.MemoryTotal],
            MemTemp = g[SensorItem.MemoryTemperature],

            PchTemp = main?.PCH.Temperature ?? -1,
            PchFanSpeed = main?.PCH.FanSpeed ?? -1,

            Disk1Temp = g[SensorItem.Disk1Temperature],
            Disk2Temp = g[SensorItem.Disk2Temperature],
        };
    }
}
