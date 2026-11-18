using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.SoftwareDisabler;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;
using NvAPIWrapper.GPU;

namespace LenovoLegionToolkit.Lib.Controllers.GodMode;

/// <summary>
/// What <see cref="GodModeController"/> reads from and sends to the laptop on the data-driven path (current
/// Legion/LOQ and non-gaming models), the fan steps and AMD overclocking: one place, so tests can supply a scripted
/// laptop and record every write. The product uses <see cref="WmiGodModeFirmware"/>, which forwards each call
/// unchanged. The older "legacy" models' calls are not routed through here.
/// </summary>
internal interface IGodModeFirmware
{
    Task<MachineInformation> GetMachineInformationAsync();

    Task<SoftwareStatus> GetVantageStatusAsync();
    Task<SoftwareStatus> GetLegionZoneStatusAsync();
    Task<SoftwareStatus> GetLegionSpaceStatusAsync();

    Task<IEnumerable<RangeCapability>> ReadCapabilityDataAsync();
    Task<IEnumerable<DiscreteCapability>> ReadDiscreteDataAsync();
    Task<IEnumerable<(int mode, byte fanId, byte sensorId, ushort[] fanTableData, ushort[] sensorTableData)>> ReadFanTableDataAsync();

    /// <summary>A Lenovo "feature" value; <paramref name="idRaw"/> is already masked by the caller.</summary>
    Task<int> GetFeatureValueAsync(uint idRaw);
    Task SetFeatureValueAsync(uint idRaw, int value);

    /// <summary>The GPU's total processing power target offset over its default, in watts (NVIDIA PCF).</summary>
    Task<int> GetGpuPowerTargetOffsetAsync();
    Task SetGpuPowerTargetOffsetAsync(int offsetInWatts);
    Task ResetGpuPowerTargetOffsetAsync();

    Task<bool> GetFanFullSpeedAsync();
    Task SetFanFullSpeedAsync(int status);
    Task SetFanTableAsync(byte[] fanTable);
    Task<uint[]> GetFanTableAsync(int fanId, int sensorId);

    Task<int> GetBiosOcModeAsync();
    Task SetCpuOcDataAsync(byte mode, uint tuneId, int value);

    /// <summary>The Windows power plan / power mode that goes with the preset (best effort).</summary>
    Task EnsureWindowsPowerSettingsAsync(GodModeSettings.GodModeSettingsStore.Preset preset);

    Task DelayAsync(TimeSpan delay);
}

/// <summary>The real laptop: each call goes straight to the Lenovo WMI interface, NVIDIA's API or Windows, as before.</summary>
internal sealed class WmiGodModeFirmware(
    VantageDisabler vantageDisabler,
    LegionZoneDisabler legionZoneDisabler,
    LegionSpaceDisabler legionSpaceDisabler) : IGodModeFirmware
{
    public Task<MachineInformation> GetMachineInformationAsync() => Compatibility.GetMachineInformationAsync();

    public Task<SoftwareStatus> GetVantageStatusAsync() => vantageDisabler.GetStatusAsync();
    public Task<SoftwareStatus> GetLegionZoneStatusAsync() => legionZoneDisabler.GetStatusAsync();
    public Task<SoftwareStatus> GetLegionSpaceStatusAsync() => legionSpaceDisabler.GetStatusAsync();

    public Task<IEnumerable<RangeCapability>> ReadCapabilityDataAsync() => WMI.LenovoCapabilityData01.ReadAsync();
    public Task<IEnumerable<DiscreteCapability>> ReadDiscreteDataAsync() => WMI.LenovoDiscreteData.ReadAsync();
    public Task<IEnumerable<(int mode, byte fanId, byte sensorId, ushort[] fanTableData, ushort[] sensorTableData)>> ReadFanTableDataAsync() => WMI.LenovoFanTableData.ReadAsync();

    public Task<int> GetFeatureValueAsync(uint idRaw) => WMI.LenovoOtherMethod.GetFeatureValueAsync(idRaw);
    public Task SetFeatureValueAsync(uint idRaw, int value) => WMI.LenovoOtherMethod.SetFeatureValueAsync(idRaw, value);

    public Task<int> GetGpuPowerTargetOffsetAsync() => Task.Run(() =>
    {
        using var controller = new PcfPowerController();
        var offsetInWatts = GodModeController.CalculatePcfTotalProcessingPowerTargetOffsetInWatts(controller.GetPowerValues());
        Log.Instance.Trace($"Read GPU TPP target offset through NVAPI PCF. [offset={offsetInWatts}W]");
        return offsetInWatts;
    });

    public Task SetGpuPowerTargetOffsetAsync(int offsetInWatts) => Task.Run(() =>
    {
        using var controller = new PcfPowerController();
        Log.Instance.Trace($"Writing GPU TPP target offset through NVAPI PCF. [offset={offsetInWatts}W]");
        var values = controller.GetPowerValues();
        var targetInMilliwatts = checked((uint)(values.ACDefaultGPULimitInMilliwatts + offsetInWatts * 1000));
        controller.SetPowerField(PcfPowerFields.ACTargetTPPLimit, targetInMilliwatts);
    });

    public Task ResetGpuPowerTargetOffsetAsync() => Task.Run(() =>
    {
        using var controller = new PcfPowerController();
        Log.Instance.Trace($"Resetting GPU TPP target offset through NVAPI PCF.");
        controller.ResetPowerField(PcfPowerFields.ACTargetTPPLimit);
    });

    public Task<bool> GetFanFullSpeedAsync() => WMI.LenovoFanMethod.FanGetFullSpeedAsync();
    public Task SetFanFullSpeedAsync(int status) => WMI.LenovoFanMethod.FanSetFullSpeedAsync(status);
    public Task SetFanTableAsync(byte[] fanTable) => WMI.LenovoFanMethod.FanSetTableAsync(fanTable);
    public Task<uint[]> GetFanTableAsync(int fanId, int sensorId) => WMI.LenovoFanMethod.FanGetTableAsync(fanId, sensorId);

    public Task<int> GetBiosOcModeAsync() => WMI.LenovoGameZoneData.GetBIOSOCMode();
    public Task SetCpuOcDataAsync(byte mode, uint tuneId, int value) => WMI.LenovoCpuMethod.CPUSetOCDataAsync(mode, tuneId, value);

    public async Task EnsureWindowsPowerSettingsAsync(GodModeSettings.GodModeSettingsStore.Preset preset)
    {
        try
        {
            var feature = IoCContainer.Resolve<PowerModeFeature>();
            await feature.EnsureCorrectWindowsPowerSettingsAreSetAsync(preset).ConfigureAwait(false);
        }
        catch { /* feature may not be available in all contexts */ }
    }

    public Task DelayAsync(TimeSpan delay) => Task.Delay(delay);
}
