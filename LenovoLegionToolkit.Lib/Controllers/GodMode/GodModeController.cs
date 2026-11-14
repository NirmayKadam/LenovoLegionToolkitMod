using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Resources;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.SoftwareDisabler;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;
using NvAPIWrapper.GPU;

namespace LenovoLegionToolkit.Lib.Controllers.GodMode;

/// <summary>
/// The firmware took the fan table write without an error, but the table it reports afterwards is a different
/// one. Distinct from a failed call, which says nothing about whether the firmware accepts the table.
/// </summary>
public class FanTableNotKeptException(string message) : Exception(message);

public class GodModeController(
    GodModeSettings settings,
    VantageDisabler vantageDisabler,
    LegionZoneDisabler legionZoneDisabler,
    LegionSpaceDisabler legionSpaceDisabler)
    : IGodModeController
{
    private const uint CAPABILITY_ID_MASK = 0xFFFF00FF;
    private const int BIOS_OC_MODE_ENABLED = 3;

    // The fixed ranges of the AMD overclocking values (the same ones the God Mode window offers).
    private const int AMD_PBO_SCALER_MAX = 7;
    private const int AMD_PBO_BOOST_MAX = 200;
    private const int AMD_CURVE_OPTIMIZER_MAX = 20;

    public event EventHandler<Guid>? PresetChanged;

    private GodModePlatformConfiguration? _config;
    private MachineInformation? _mi;
    private bool _hasCapabilityErrors;

    public bool HasCapabilityErrors() => _hasCapabilityErrors;

    #region Software Disablers

    public Task<bool> NeedsVantageDisabledAsync() => Task.FromResult(true);

    public async Task<bool> NeedsLegionZoneDisabledAsync()
    {
        var config = await GetConfigAsync().ConfigureAwait(false);
        if (config.Platform == GodModePlatform.NonGaming)
            return false;
        return true;
    }

    public async Task<bool> NeedsLegionSpaceDisabledAsync()
    {
        var config = await GetConfigAsync().ConfigureAwait(false);
        if (config.Platform is GodModePlatform.LegacyLegion or GodModePlatform.NonGaming)
            return false;
        var mi = await GetMachineInformationAsync().ConfigureAwait(false);
        return mi.SmartFanVersion >= 8;
    }

    #endregion

    #region Active Preset

    public Task<Guid> GetActivePresetIdAsync() => Task.FromResult(settings.Store.ActivePresetId);

    public Task<string?> GetActivePresetNameAsync()
    {
        var store = settings.Store;
        var name = store.Presets
            .Where(p => p.Key == store.ActivePresetId)
            .Select(p => p.Value.Name)
            .FirstOrDefault();
        return Task.FromResult(name);
    }

    public async Task<(Guid, GodModeSettings.GodModeSettingsStore.Preset)> GetActivePresetAsync()
    {
        if (!IsValidStore(settings.Store))
        {
            Log.Instance.Trace($"Invalid store, generating default one.");
            var state = await GetStateAsync().ConfigureAwait(false);
            await SetStateAsync(state).ConfigureAwait(false);
        }

        var activePresetId = settings.Store.ActivePresetId;
        var presets = settings.Store.Presets;

        if (presets.TryGetValue(activePresetId, out var activePreset))
        {
            return (activePresetId, activePreset);
        }

        throw new InvalidOperationException($"Preset with ID {activePresetId} not found");
    }

    public virtual async Task<Dictionary<Guid, GodModeSettings.GodModeSettingsStore.Preset>> GetGodModePresetsAsync()
    {
        return await Task.FromResult(settings.Store.Presets).ConfigureAwait(false);
    }

    #endregion

    #region State Get/Set

    public async Task<GodModeState> GetStateAsync()
    {
        Log.Instance.Trace($"Getting state...");

        var store = settings.Store;
        var config = await GetConfigAsync().ConfigureAwait(false);
        var defaultState = await GetDefaultStateAsync(config).ConfigureAwait(false);

        if (!IsValidStore(store))
        {
            Log.Instance.Trace($"Loading default state...");

            var id = Guid.NewGuid();
            return new GodModeState
            {
                ActivePresetId = id,
                Presets = new Dictionary<Guid, GodModePreset> { { id, defaultState } }.AsReadOnlyDictionary()
            };
        }

        Log.Instance.Trace($"Loading state from store...");
        return await LoadStateFromStoreAsync(store, defaultState).ConfigureAwait(false);
    }

    public Task SetStateAsync(GodModeState state)
    {
        Log.Instance.Trace($"Setting state...");

        var activePresetId = state.ActivePresetId;
        var presets = new Dictionary<Guid, GodModeSettings.GodModeSettingsStore.Preset>();

        foreach (var (id, preset) in state.Presets)
        {
            presets.Add(id, new()
            {
                Name = preset.Name,
                CPULongTermPowerLimit = preset.CPULongTermPowerLimit,
                CPUShortTermPowerLimit = preset.CPUShortTermPowerLimit,
                CPUPeakPowerLimit = preset.CPUPeakPowerLimit,
                CPUCrossLoadingPowerLimit = preset.CPUCrossLoadingPowerLimit,
                CPUPL2Tau = preset.CPUPL2Tau,
                APUsPPTPowerLimit = preset.APUsPPTPowerLimit,
                CPUTemperatureLimit = preset.CPUTemperatureLimit,
                GPUPowerBoost = preset.GPUPowerBoost,
                GPUConfigurableTGP = preset.GPUConfigurableTGP,
                GPUTemperatureLimit = preset.GPUTemperatureLimit,
                GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline = preset.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline,
                GPUTotalProcessingPowerTargetOnAcOffsetFromBaselineNVAPI = preset.GPUTotalProcessingPowerTargetOnAcOffsetFromBaselineNVAPI,
                GPUToCPUDynamicBoost = preset.GPUToCPUDynamicBoost,
                FanTable = preset.FanTableInfo?.Table,
                FanFullSpeed = preset.FanFullSpeed,
                MinValueOffset = preset.MinValueOffset,
                MaxValueOffset = preset.MaxValueOffset,
                PrecisionBoostOverdriveScaler = preset.PrecisionBoostOverdriveScaler,
                PrecisionBoostOverdriveBoostFrequency = preset.PrecisionBoostOverdriveBoostFrequency,
                AllCoreCurveOptimizer = preset.AllCoreCurveOptimizer,
                EnableAllCoreCurveOptimizer = preset.EnableAllCoreCurveOptimizer,
                EnableOverclocking = preset.EnableOverclocking,
                Overrides = new Dictionary<PowerOverrideKey, string>(preset.Overrides ?? []),
            });
        }

        settings.Store.ActivePresetId = activePresetId;
        settings.Store.Presets = presets;
        settings.SynchronizeStore();

        Log.Instance.Trace($"State saved.");
        return Task.CompletedTask;
    }

    #endregion

    #region Apply State

    public async Task ApplyStateAsync()
    {
        var config = await GetConfigAsync().ConfigureAwait(false);

        if (config.Platform == GodModePlatform.LegacyLegion)
        {
            await ApplyStateLegacyAsync().ConfigureAwait(false);
            return;
        }

        await ApplyStateDataDrivenAsync(config).ConfigureAwait(false);
    }

    private async Task ApplyStateLegacyAsync()
    {
        if (await legionZoneDisabler.GetStatusAsync().ConfigureAwait(false) == SoftwareStatus.Enabled)
        {
            Log.Instance.Trace($"Can't correctly apply state when Legion Zone is running.");
            return;
        }

        Log.Instance.Trace($"Applying state...");

        var (presetId, preset) = await GetActivePresetAsync().ConfigureAwait(false);
        var fanTable = preset.FanTable ?? await GetDefaultFanTableAsync().ConfigureAwait(false);
        var fanFullSpeed = preset.FanFullSpeed ?? false;

        // The values that are sent come from here, not straight from the settings file: each one has been
        // put through the firmware's own range (see GetRangeCheckedPresetAsync).
        var checkedPreset = await GetRangeCheckedPresetAsync(presetId).ConfigureAwait(false);

        var cpuSettings = new (string Name, StepperValue? Value, Func<int, Task> Set)[]
        {
            ("cpuLongTermPowerLimit", OnlyIfSaved(preset.CPULongTermPowerLimit, checkedPreset?.CPULongTermPowerLimit), WMI.LenovoCpuMethod.CPUSetLongTermPowerLimitAsync),
            ("cpuShortTermPowerLimit", OnlyIfSaved(preset.CPUShortTermPowerLimit, checkedPreset?.CPUShortTermPowerLimit), WMI.LenovoCpuMethod.CPUSetShortTermPowerLimitAsync),
            ("cpuPeakPowerLimit", OnlyIfSaved(preset.CPUPeakPowerLimit, checkedPreset?.CPUPeakPowerLimit), v => WMI.LenovoCpuMethod.CPUSetPeakPowerLimitAsync(v)),
            ("cpuCrossLoadingPowerLimit", OnlyIfSaved(preset.CPUCrossLoadingPowerLimit, checkedPreset?.CPUCrossLoadingPowerLimit), v => WMI.LenovoCpuMethod.CPUSetCrossLoadingPowerLimitAsync(v)),
            ("apuSPPTPowerLimit", OnlyIfSaved(preset.APUsPPTPowerLimit, checkedPreset?.APUsPPTPowerLimit), v => WMI.LenovoCpuMethod.SetAPUSPPTPowerLimitAsync(v)),
            ("cpuTemperatureLimit", OnlyIfSaved(preset.CPUTemperatureLimit, checkedPreset?.CPUTemperatureLimit), v => WMI.LenovoCpuMethod.CPUSetTemperatureControlAsync(v)),
        };

        var gpuSettings = new (string Name, StepperValue? Value, Func<int, Task> Set)[]
        {
            ("gpuPowerBoost", OnlyIfSaved(preset.GPUPowerBoost, checkedPreset?.GPUPowerBoost), v => WMI.LenovoGpuMethod.GPUSetPPABPowerLimitAsync(v)),
            ("gpuConfigurableTgp", OnlyIfSaved(preset.GPUConfigurableTGP, checkedPreset?.GPUConfigurableTGP), v => WMI.LenovoGpuMethod.GPUSetCTGPPowerLimitAsync(v)),
            ("gpuTemperatureLimit", OnlyIfSaved(preset.GPUTemperatureLimit, checkedPreset?.GPUTemperatureLimit), v => WMI.LenovoGpuMethod.GPUSetTemperatureLimitAsync(v)),
        };

        // If a limit can't be set, no further limits are written (as before), but the fan step below still
        // runs, so raised limits are never left without the preset's fan settings. The error is raised after it.
        ExceptionDispatchInfo? limitFailure = null;
        try
        {
            foreach (var (name, value, set) in cpuSettings)
            {
                await TryApplyAsync(name, value, set, rethrow: true).ConfigureAwait(false);
            }

            foreach (var (name, value, set) in gpuSettings)
            {
                await TryApplyAsync(name, value, set, rethrow: false).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            limitFailure = ExceptionDispatchInfo.Capture(ex);
        }

        await ApplyFanLegacyAsync(fanTable, fanFullSpeed).ConfigureAwait(false);

        limitFailure?.Throw();

        await RaisePresetChanged(presetId).ConfigureAwait(false);
        Log.Instance.Trace($"State applied. [name={preset.Name}, id={presetId}]");
    }

    private static async Task TryApplyAsync(string name, StepperValue? value, Func<int, Task> set, bool rethrow)
    {
        if (value == null)
            return;
        try
        {
            Log.Instance.Trace($"Applying {name}: {value}");
            await set(value.Value.Value).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Apply failed. [setting={name}]", ex);
            if (rethrow)
            {
                throw;
            }
        }
    }

    private async Task ApplyFanLegacyAsync(FanTable fanTable, bool fanFullSpeed)
    {
        if (fanFullSpeed)
        {
            try
            {
                Log.Instance.Trace($"Applying Fan Full Speed...");
                await WMI.LenovoFanMethod.FanSetFullSpeedAsync(1).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Apply failed. [setting=fanFullSpeed]", ex);
                throw;
            }
        }
        else
        {
            try
            {
                Log.Instance.Trace($"Making sure Fan Full Speed is false...");
                await WMI.LenovoFanMethod.FanSetFullSpeedAsync(0).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Apply failed. [setting=fanFullSpeed]", ex);
                throw;
            }

            try
            {
                Log.Instance.Trace($"Applying Fan Table {fanTable}...");
                if (!await IsValidFanTableAsync(fanTable).ConfigureAwait(false))
                {
                    Log.Instance.Trace($"Fan table invalid, replacing with default...");
                    fanTable = await GetDefaultFanTableAsync().ConfigureAwait(false);
                }
                await WMI.LenovoFanMethod.FanSetTableAsync(fanTable.GetBytes()).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Apply failed. [setting=fanTable]", ex);
                throw;
            }
        }
    }

    private async Task ApplyStateDataDrivenAsync(GodModePlatformConfiguration config)
    {
        var mi = await GetMachineInformationAsync().ConfigureAwait(false);

        if (await vantageDisabler.GetStatusAsync().ConfigureAwait(false) == SoftwareStatus.Enabled)
        {
            Log.Instance.Trace($"Can't correctly apply state when Vantage is running.");
            return;
        }

        if (config.Platform == GodModePlatform.Legion)
        {
            if (mi.SmartFanVersion >= 8)
            {
                if (await legionSpaceDisabler.GetStatusAsync().ConfigureAwait(false) == SoftwareStatus.Enabled)
                {
                    Log.Instance.Trace($"Can't correctly apply state when Legion Space is running.");
                    return;
                }
            }

            if (await legionZoneDisabler.GetStatusAsync().ConfigureAwait(false) == SoftwareStatus.Enabled)
            {
                Log.Instance.Trace($"Can't correctly apply state when Legion Zone is running.");
                return;
            }
        }

        Log.Instance.Trace($"Applying state...");

        var (presetId, preset) = await GetActivePresetAsync().ConfigureAwait(false);

        var defaultPresets = await GetDefaultsInOtherPowerModesAsync().ConfigureAwait(false);
        var defaultPerformancePreset = mi.Properties.SupportsExtremeMode
            ? defaultPresets.GetValueOrNull(PowerModeState.Extreme)
            : defaultPresets.GetValueOrNull(PowerModeState.Performance);

        var failAllowedIds = config.Capabilities
            .Where(c => c.FailAllowed)
            .Select(c => c.RawId)
            .ToHashSet();

        var fanTable = preset.FanTable ?? await GetDefaultFanTableAsync().ConfigureAwait(false);
        var fanFullSpeed = preset.FanFullSpeed ?? false;

        // The values that are sent come from here, not straight from the settings file: each one has been
        // put through the firmware's own range (see GetRangeCheckedPresetAsync).
        var checkedPreset = await GetRangeCheckedPresetAsync(presetId).ConfigureAwait(false);

        // If a limit can't be set, no further limits are written (as before), but the fan step below still
        // runs, so raised limits are never left without the preset's fan settings. The error is raised after it.
        ExceptionDispatchInfo? limitFailure = null;

        foreach (var cap in config.Capabilities)
        {
            // Fan full speed is an on/off switch with no range; everything else is range-checked. A setting the
            // preset doesn't save goes the "default" way below, as before, instead of resending a read-back value.
            var stepperValue = cap.PropertyName == nameof(GodModePreset.FanFullSpeed)
                ? GetStepperValueForStorePreset(preset, cap.PropertyName)
                : OnlyIfSaved(GetStepperValueForStorePreset(preset, cap.PropertyName), GetStepperValueForPreset(checkedPreset, cap.PropertyName));

            if (stepperValue is null && GetStepperValueForStorePreset(preset, cap.PropertyName) is { } saved)
                Log.Instance.Trace($"Not applying saved {cap.PropertyName}={saved.Value}: its allowed range couldn't be read from the firmware.");

            var value = stepperValue?.Value;
            var defaultValue = GetDefaultValueFromDefaults(defaultPerformancePreset, cap.PropertyName);

            if (value.HasValue)
            {
                try
                {
                    Log.Instance.Trace($"Applying {cap.PropertyName} (0x{cap.RawId:X}): {value}...");

                    if (cap.CapabilityId.Equals(NonGamingCapabilityID.GPUConfigurableTGP) && config.Platform == GodModePlatform.NonGaming)
                    {
                        // NonGaming cTGP is written as an offset from the lowest step (55 W, see
                        // GodModePlatformConfiguration). Same value as the previous literal 55.
                        // Note: reads are not converted back the same way; unconfirmed on hardware.
                        value -= cap.Steps[0];
                    }

                    await SetCapabilityValueAsync(cap, value.Value, config.CapabilityIdMask).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Instance.Trace($"Failed to apply {cap.PropertyName} ({cap.RawId:X}). [value={value}]", ex);
                    if (!failAllowedIds.Contains(cap.RawId))
                    {
                        limitFailure = ExceptionDispatchInfo.Capture(ex);
                        break;
                    }
                }
            }
            else if (defaultValue.HasValue)
            {
                try
                {
                    Log.Instance.Trace($"Applying default {cap.PropertyName} ({cap.RawId:X}): {defaultValue}...");
                    await SetCapabilityValueAsync(cap, defaultValue.Value, config.CapabilityIdMask).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Instance.Trace($"Failed to apply default {cap.PropertyName} ({cap.RawId:X}). [value={defaultValue}]", ex);
                    if (!failAllowedIds.Contains(cap.RawId))
                    {
                        limitFailure = ExceptionDispatchInfo.Capture(ex);
                        break;
                    }
                }
            }
        }

        if (limitFailure is not null)
            Log.Instance.Trace($"A limit couldn't be set; applying the fan settings before reporting it.");

        if (preset.FanTable != null && config.Platform != GodModePlatform.NonGaming)
        {
            await ApplyFanSettingsAsync(config, fanTable, fanFullSpeed).ConfigureAwait(false);
        }

        limitFailure?.Throw();

        if (config.Platform == GodModePlatform.Legion)
        {
            await ApplyOcIfNeededAsync(preset).ConfigureAwait(false);
        }

        await RaisePresetChanged(presetId).ConfigureAwait(false);
        Log.Instance.Trace($"State applied. [name={preset.Name}, id={presetId}]");
    }

    private async Task ApplyFanSettingsAsync(GodModePlatformConfiguration config, FanTable fanTable, bool fanFullSpeed)
    {
        if (fanFullSpeed)
        {
            try
            {
                Log.Instance.Trace($"Applying Fan Full Speed...");
                await SetFanFullSpeedAsync(config, true).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Apply failed. [setting=fanFullSpeed]", ex);
                throw;
            }
        }
        else
        {
            try
            {
                Log.Instance.Trace($"Making sure Fan Full Speed is false...");
                await SetFanFullSpeedAsync(config, false).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Apply failed. [setting=fanFullSpeed]", ex);
                throw;
            }

            try
            {
                Log.Instance.Trace($"Waiting for power mode set successfully...");
                await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);

                Log.Instance.Trace($"Applying Fan Table {fanTable}...");
                if (!await IsValidFanTableAsync(fanTable).ConfigureAwait(false))
                {
                    Log.Instance.Trace($"Fan table invalid, replacing with default...");
                    fanTable = await GetDefaultFanTableAsync().ConfigureAwait(false);
                }

                await SetFanTableWithVerifyAsync(fanTable).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Apply failed. [setting=fanTable]", ex);
                throw;
            }
        }
    }

    private async Task ApplyOcIfNeededAsync(GodModeSettings.GodModeSettingsStore.Preset preset)
    {
        var isOcEnabled = await IsBiosOcEnabledAsync().ConfigureAwait(false);

        if (isOcEnabled && preset.EnableOverclocking == true)
        {
            await WMI.LenovoOtherMethod.SetFeatureValueAsync((uint)CapabilityID.CPUOverclockingEnable, 1).ConfigureAwait(false);

            // Each value is held to its fixed range here too, not only in the window, so a hand-edited settings
            // file can't send something outside it.
            if (preset.PrecisionBoostOverdriveScaler is { } pboScaler)
            {
                Log.Instance.Trace($"Applying PrecisionBoostOverdriveScaler: {pboScaler}...");
                await WMI.LenovoCpuMethod.CPUSetOCDataAsync(17, (uint)CPUOverclockingID.PrecisionBoostOverdriveScaler, Math.Clamp(pboScaler.Value, 0, AMD_PBO_SCALER_MAX)).ConfigureAwait(false);
            }
            if (preset.PrecisionBoostOverdriveBoostFrequency is { } pboFreq)
            {
                Log.Instance.Trace($"Applying PrecisionBoostOverdriveBoostFrequency: {pboFreq}...");
                await WMI.LenovoCpuMethod.CPUSetOCDataAsync(17, (uint)CPUOverclockingID.PrecisionBoostOverdriveBoostFrequency, Math.Clamp(pboFreq.Value, 0, AMD_PBO_BOOST_MAX)).ConfigureAwait(false);
            }
            if (preset.AllCoreCurveOptimizer is { } coreCurve && preset.EnableAllCoreCurveOptimizer == true)
            {
                Log.Instance.Trace($"Applying AllCoreCurveOptimizer: {coreCurve}...");
                await WMI.LenovoCpuMethod.CPUSetOCDataAsync(17, (uint)CPUOverclockingID.AllCoreCurveOptimizer, Math.Clamp(coreCurve.Value, 0, AMD_CURVE_OPTIMIZER_MAX)).ConfigureAwait(false);
            }
        }
        else if (isOcEnabled && preset.EnableOverclocking == false)
        {
            await WMI.LenovoOtherMethod.SetFeatureValueAsync((uint)CapabilityID.CPUOverclockingEnable, 0).ConfigureAwait(false);
            Log.Instance.Trace($"Overclocking is disabled.");
        }
    }

    #endregion

    #region Get Default State

    private async Task<GodModePreset> GetDefaultStateAsync(GodModePlatformConfiguration config)
    {
        return config.Platform switch
        {
            GodModePlatform.LegacyLegion => await GetDefaultStateLegacyAsync().ConfigureAwait(false),
            _ => await GetDefaultStateDataDrivenAsync(config).ConfigureAwait(false),
        };
    }

    private async Task<GodModePreset> GetDefaultStateLegacyAsync()
    {
        var fanTableData = await GetFanTableDataLegacyAsync().ConfigureAwait(false);

        var preset = new GodModePreset
        {
            Name = "Default",
            CPULongTermPowerLimit = await GetCPULongTermPowerLimitLegacyAsync().OrNullIfException().ConfigureAwait(false),
            CPUShortTermPowerLimit = await GetCPUShortTermPowerLimitLegacyAsync().OrNullIfException().ConfigureAwait(false),
            CPUPeakPowerLimit = await GetCPUPeakPowerLimitLegacyAsync().OrNullIfException().ConfigureAwait(false),
            CPUCrossLoadingPowerLimit = await GetCPUCrossLoadingPowerLimitLegacyAsync().OrNullIfException().ConfigureAwait(false),
            APUsPPTPowerLimit = await GetAPUSPPTPowerLimitLegacyAsync().OrNullIfException().ConfigureAwait(false),
            CPUTemperatureLimit = await GetCPUTemperatureLimitLegacyAsync().OrNullIfException().ConfigureAwait(false),
            GPUPowerBoost = await GetGPUPowerBoostLegacyAsync().OrNullIfException().ConfigureAwait(false),
            GPUConfigurableTGP = await GetGPUConfigurableTGPLegacyAsync().OrNullIfException().ConfigureAwait(false),
            GPUTemperatureLimit = await GetGPUTemperatureLimitLegacyAsync().OrNullIfException().ConfigureAwait(false),
            FanTableInfo = fanTableData == null ? null : new FanTableInfo(fanTableData, await GetDefaultFanTableAsync().ConfigureAwait(false)),
            FanFullSpeed = await WMI.LenovoFanMethod.FanGetFullSpeedAsync().ConfigureAwait(false),
            MinValueOffset = 0,
            MaxValueOffset = 0
        };

        Log.Instance.Trace($"Default state retrieved (legacy): {preset}");
        return preset;
    }

    private async Task<GodModePreset> GetDefaultStateDataDrivenAsync(GodModePlatformConfiguration config)
    {
        var mi = await GetMachineInformationAsync().ConfigureAwait(false);
        var isAmdDevice = mi.Properties.IsAmdDevice;
        var stepperValues = new Dictionary<uint, StepperValue>();

        // Reflects this read only, so a transient failure doesn't keep the warning bar open all session.
        _hasCapabilityErrors = false;

        if (config.UseCapabilityDataForDefaults)
        {
            var allCapabilityData = await WMI.LenovoCapabilityData01.ReadAsync().ConfigureAwait(false);
            allCapabilityData = allCapabilityData.ToArray();

            var knownIds = config.Capabilities
                .Where(c => !IsNvApiCapability(c))
                .Select(c => c.RawId)
                .ToHashSet();
            var capabilityData = allCapabilityData
                .Where(d => knownIds.Contains((uint)d.Id))
                .ToArray();

            var allDiscreteData = await WMI.LenovoDiscreteData.ReadAsync().ConfigureAwait(false);
            allDiscreteData = allDiscreteData.ToArray();

            var discreteData = allDiscreteData
                .Where(d => knownIds.Contains((uint)d.Id))
                .GroupBy(d => d.Id, d => d.Value, (id, values) => (id, values))
                .ToDictionary(d => (uint)d.id, d => d.values.ToArray());

            foreach (var c in capabilityData)
            {
                var rawId = (uint)c.Id;
                var value = await GetValueAsync(rawId, config.CapabilityIdMask).OrNullIfException().ConfigureAwait(false) ?? c.DefaultValue;
                var steps = discreteData.GetValueOrDefault(rawId) ?? [];

                if (c.Step == 0 && steps.Length < 1)
                {
                    Log.Instance.Trace($"Skipping {rawId:X}... [defaultValue={c.DefaultValue}, min={c.Min}, max={c.Max}, step={c.Step}]");
                    continue;
                }

                Log.Instance.Trace($"Creating StepperValue {rawId:X}... [defaultValue={c.DefaultValue}, min={c.Min}, max={c.Max}, step={c.Step}, steps={string.Join(", ", steps)}]");
                stepperValues[rawId] = new StepperValue(value, c.Min, c.Max, c.Step, steps, c.DefaultValue);
            }

            foreach (var cap in config.Capabilities.Where(IsNvApiCapability))
            {
                try
                {
                    var value = await GetCapabilityValueAsync(cap, config.CapabilityIdMask).ConfigureAwait(false);
                    stepperValues[cap.RawId] = CreateCapabilityStepperValue(cap, value);
                }
                catch (Exception ex)
                {
                    Log.Instance.Trace($"Failed to read {cap.PropertyName} via NVAPI, skipping.", ex);
                }
            }
        }
        else
        {
            foreach (var cap in config.Capabilities)
            {
                if (config.Platform == GodModePlatform.NonGaming && IsNvApiCapability(cap))
                {
                    var nativeCapability = config.Capabilities.FirstOrDefault(c =>
                        !IsNvApiCapability(c) &&
                        c.PropertyName == nameof(GodModePreset.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline));

                    if (nativeCapability != null && stepperValues.ContainsKey(nativeCapability.RawId))
                    {
                        Log.Instance.Trace($"Skipping {cap.PropertyName}; native capability is available.");
                        continue;
                    }
                }

                try
                {
                    var rawValue = await GetCapabilityValueAsync(cap, config.CapabilityIdMask).ConfigureAwait(false);

                    stepperValues[cap.RawId] = CreateCapabilityStepperValue(cap, rawValue);
                }
                catch (Exception ex)
                {
                    Log.Instance.Trace($"Failed to read {cap.PropertyName} ({cap.RawId:X}), skipping.", ex);
                    if (!cap.FailAllowed)
                    {
                        _hasCapabilityErrors = true;
                    }
                }
            }
        }

        var fanTableData = await TryGetFanTableDataAsync(config).ConfigureAwait(false);

        var (pboScaler, pboFreq, coreCurve) = CreateAmdOcDefaults(isAmdDevice);
        FanTableInfo? fanTableInfo = null;
        if (fanTableData != null)
        {
            fanTableInfo = new FanTableInfo(fanTableData, await GetDefaultFanTableAsync().ConfigureAwait(false));
        }
        var fanFullSpeed = await GetFanFullSpeedAsync(config).ConfigureAwait(false);

        var enableOverclocking = false;
        var enableAllCoreCurve = false;

        if (config.Platform == GodModePlatform.Legion && isAmdDevice)
        {
            try
            {
                var ocMode = await WMI.LenovoOtherMethod.GetFeatureValueAsync((uint)CapabilityID.CPUOverclockingEnable).ConfigureAwait(false);
                enableOverclocking = ocMode == 1;
            }
            catch { /* Ignore */ }
        }

        var preset = PopulatePreset(config, stepperValues, fanTableInfo, fanFullSpeed, 0, 0, pboScaler, pboFreq, coreCurve, enableAllCoreCurve, enableOverclocking);

        Log.Instance.Trace($"Default state retrieved: {preset}");
        return preset;
    }

    private static GodModePreset PopulatePreset(
        GodModePlatformConfiguration config,
        Dictionary<uint, StepperValue> stepperValues,
        FanTableInfo? fanTableInfo,
        bool? fanFullSpeed,
        int minValueOffset,
        int maxValueOffset,
        StepperValue? pboScaler,
        StepperValue? pboFreq,
        StepperValue? coreCurve,
        bool enableAllCoreCurve,
        bool enableOverclocking)
    {
        StepperValue? sv(uint id) => stepperValues.GetValueOrNull(id);

        return new GodModePreset
        {
            Name = "Default",
            CPULongTermPowerLimit = Sv(config, sv, nameof(GodModePreset.CPULongTermPowerLimit)),
            CPUShortTermPowerLimit = Sv(config, sv, nameof(GodModePreset.CPUShortTermPowerLimit)),
            CPUPeakPowerLimit = Sv(config, sv, nameof(GodModePreset.CPUPeakPowerLimit)),
            CPUCrossLoadingPowerLimit = Sv(config, sv, nameof(GodModePreset.CPUCrossLoadingPowerLimit)),
            CPUPL2Tau = Sv(config, sv, nameof(GodModePreset.CPUPL2Tau)),
            APUsPPTPowerLimit = Sv(config, sv, nameof(GodModePreset.APUsPPTPowerLimit)),
            CPUTemperatureLimit = Sv(config, sv, nameof(GodModePreset.CPUTemperatureLimit)),
            GPUPowerBoost = Sv(config, sv, nameof(GodModePreset.GPUPowerBoost)),
            GPUConfigurableTGP = Sv(config, sv, nameof(GodModePreset.GPUConfigurableTGP)),
            GPUTemperatureLimit = Sv(config, sv, nameof(GodModePreset.GPUTemperatureLimit)),
            GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline = Sv(config, sv, nameof(GodModePreset.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline)),
            GPUTotalProcessingPowerTargetOnAcOffsetFromBaselineNVAPI = Sv(config, sv, nameof(GodModePreset.GPUTotalProcessingPowerTargetOnAcOffsetFromBaselineNVAPI)),
            GPUToCPUDynamicBoost = Sv(config, sv, nameof(GodModePreset.GPUToCPUDynamicBoost)),
            FanTableInfo = fanTableInfo,
            FanFullSpeed = fanFullSpeed,
            MinValueOffset = minValueOffset,
            MaxValueOffset = maxValueOffset,
            PrecisionBoostOverdriveScaler = pboScaler,
            PrecisionBoostOverdriveBoostFrequency = pboFreq,
            AllCoreCurveOptimizer = coreCurve,
            EnableAllCoreCurveOptimizer = enableAllCoreCurve,
            EnableOverclocking = enableOverclocking,
        };
    }

    private static StepperValue? Sv(GodModePlatformConfiguration config, Func<uint, StepperValue?> sv, string propertyName)
    {
        var cap = config.Capabilities.FirstOrDefault(c => c.PropertyName == propertyName);
        if (cap == null)
            return null;
        return sv(cap.RawId);
    }

    private static (StepperValue?, StepperValue?, StepperValue?) CreateAmdOcDefaults(bool isAmdDevice)
    {
        if (!isAmdDevice)
            return (null, null, null);
        return (
            new StepperValue(0, 0, 7, 1, [], 0),
            new StepperValue(0, 0, 200, 1, [], 0),
            new StepperValue(0, 0, 20, 1, [], 0)
        );
    }

    #endregion

    #region Property Access Helpers

    private static StepperValue? GetStepperValueForStorePreset(GodModeSettings.GodModeSettingsStore.Preset preset, string propertyName)
    {
        return propertyName switch
        {
            nameof(GodModePreset.CPULongTermPowerLimit) => preset.CPULongTermPowerLimit,
            nameof(GodModePreset.CPUShortTermPowerLimit) => preset.CPUShortTermPowerLimit,
            nameof(GodModePreset.CPUPeakPowerLimit) => preset.CPUPeakPowerLimit,
            nameof(GodModePreset.CPUCrossLoadingPowerLimit) => preset.CPUCrossLoadingPowerLimit,
            nameof(GodModePreset.CPUPL2Tau) => preset.CPUPL2Tau,
            nameof(GodModePreset.APUsPPTPowerLimit) => preset.APUsPPTPowerLimit,
            nameof(GodModePreset.CPUTemperatureLimit) => preset.CPUTemperatureLimit,
            nameof(GodModePreset.GPUPowerBoost) => preset.GPUPowerBoost,
            nameof(GodModePreset.GPUConfigurableTGP) => preset.GPUConfigurableTGP,
            nameof(GodModePreset.GPUTemperatureLimit) => preset.GPUTemperatureLimit,
            nameof(GodModePreset.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline) => preset.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline,
            nameof(GodModePreset.GPUTotalProcessingPowerTargetOnAcOffsetFromBaselineNVAPI) => preset.GPUTotalProcessingPowerTargetOnAcOffsetFromBaselineNVAPI,
            nameof(GodModePreset.GPUToCPUDynamicBoost) => preset.GPUToCPUDynamicBoost,
            nameof(GodModePreset.FanFullSpeed) when preset.FanFullSpeed != null => new StepperValue(preset.FanFullSpeed.Value ? 1 : 0, 0, 1, 1, [], 0),
            _ => null,
        };
    }

    /// <summary>
    /// The active preset with every value put through the firmware's own range, steps and default: the same rule
    /// the God Mode window shows (<see cref="CreateStepperValue"/>). This is what gets sent to the firmware, so a
    /// hand-edited settings file, a backup from another model or a range changed by a BIOS update can't push a
    /// value past what the firmware allows. A value whose range can't be read comes back empty and isn't sent.
    /// </summary>
    private async Task<GodModePreset?> GetRangeCheckedPresetAsync(Guid presetId)
    {
        try
        {
            var store = settings.Store;
            if (IsValidStore(store))
            {
                var ranges = await _firmwareRanges.GetAsync(ReadFirmwareRangesAsync).ConfigureAwait(false);
                var state = await LoadStateFromStoreAsync(store, ranges).ConfigureAwait(false);
                if (state.Presets.TryGetValue(presetId, out var preset))
                    return preset;
            }
        }
        catch (Exception ex)
        {
            // Nothing is sent rather than unchecked values; the fan step still runs.
            Log.Instance.Trace($"Couldn't read the firmware's ranges; no limits will be sent.", ex);
            return null;
        }

        Log.Instance.Trace($"Preset {presetId} couldn't be range-checked; no limits will be sent.");
        return null;
    }

    /// <summary>
    /// The firmware's ranges, steps and defaults for each setting, read once and reused for every apply (they don't
    /// change while running). Forgotten on resume from sleep (<see cref="ForgetFirmwareRanges"/>), so a change made
    /// in BIOS setup in between is picked up. The God Mode window still reads live, since it shows current values.
    /// </summary>
    private readonly ReadOnceCache<GodModePreset> _firmwareRanges = new();

    private async Task<GodModePreset> ReadFirmwareRangesAsync()
    {
        var config = await GetConfigAsync().ConfigureAwait(false);
        return await GetDefaultStateAsync(config).ConfigureAwait(false);
    }

    public Task<GodModePreset?> GetRangeCheckedActivePresetAsync() => GetRangeCheckedPresetAsync(settings.Store.ActivePresetId);

    /// <summary>Makes the next apply read the firmware's ranges again.</summary>
    public void ForgetFirmwareRanges() => _firmwareRanges.Clear();

    /// <summary>
    /// The range-checked value, but only for a setting the preset actually saves. A setting it leaves empty is not
    /// sent as a value (the checked preset would otherwise carry the firmware's read-back value for it).
    /// </summary>
    internal static StepperValue? OnlyIfSaved(StepperValue? saved, StepperValue? rangeChecked) => saved is null ? null : rangeChecked;

    private static StepperValue? GetStepperValueForPreset(GodModePreset? preset, string propertyName)
    {
        if (preset is not { } p)
            return null;

        return propertyName switch
        {
            nameof(GodModePreset.CPULongTermPowerLimit) => p.CPULongTermPowerLimit,
            nameof(GodModePreset.CPUShortTermPowerLimit) => p.CPUShortTermPowerLimit,
            nameof(GodModePreset.CPUPeakPowerLimit) => p.CPUPeakPowerLimit,
            nameof(GodModePreset.CPUCrossLoadingPowerLimit) => p.CPUCrossLoadingPowerLimit,
            nameof(GodModePreset.CPUPL2Tau) => p.CPUPL2Tau,
            nameof(GodModePreset.APUsPPTPowerLimit) => p.APUsPPTPowerLimit,
            nameof(GodModePreset.CPUTemperatureLimit) => p.CPUTemperatureLimit,
            nameof(GodModePreset.GPUPowerBoost) => p.GPUPowerBoost,
            nameof(GodModePreset.GPUConfigurableTGP) => p.GPUConfigurableTGP,
            nameof(GodModePreset.GPUTemperatureLimit) => p.GPUTemperatureLimit,
            nameof(GodModePreset.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline) => p.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline,
            nameof(GodModePreset.GPUTotalProcessingPowerTargetOnAcOffsetFromBaselineNVAPI) => p.GPUTotalProcessingPowerTargetOnAcOffsetFromBaselineNVAPI,
            nameof(GodModePreset.GPUToCPUDynamicBoost) => p.GPUToCPUDynamicBoost,
            _ => null,
        };
    }

    private static int? GetDefaultValueFromDefaults(GodModeDefaults? defaults, string propertyName)
    {
        if (defaults is not { } d)
            return null;
        return propertyName switch
        {
            nameof(GodModeDefaults.CPULongTermPowerLimit) => d.CPULongTermPowerLimit,
            nameof(GodModeDefaults.CPUShortTermPowerLimit) => d.CPUShortTermPowerLimit,
            nameof(GodModeDefaults.CPUPeakPowerLimit) => d.CPUPeakPowerLimit,
            nameof(GodModeDefaults.CPUCrossLoadingPowerLimit) => d.CPUCrossLoadingPowerLimit,
            nameof(GodModeDefaults.CPUPL2Tau) => d.CPUPL2Tau,
            nameof(GodModeDefaults.APUsPPTPowerLimit) => d.APUsPPTPowerLimit,
            nameof(GodModeDefaults.CPUTemperatureLimit) => d.CPUTemperatureLimit,
            nameof(GodModeDefaults.GPUPowerBoost) => d.GPUPowerBoost,
            nameof(GodModeDefaults.GPUConfigurableTGP) => d.GPUConfigurableTGP,
            nameof(GodModeDefaults.GPUTemperatureLimit) => d.GPUTemperatureLimit,
            nameof(GodModeDefaults.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline) => d.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline,
            nameof(GodModeDefaults.GPUToCPUDynamicBoost) => d.GPUToCPUDynamicBoost,
            _ => null,
        };
    }

    #endregion

    #region Get Defaults in Other Power Modes

    public async Task<Dictionary<PowerModeState, GodModeDefaults>> GetDefaultsInOtherPowerModesAsync()
    {
        var config = await GetConfigAsync().ConfigureAwait(false);
        var mi = await GetMachineInformationAsync().ConfigureAwait(false);

        return config.Platform switch
        {
            GodModePlatform.LegacyLegion => await GetDefaultsLegacyAsync().ConfigureAwait(false),
            GodModePlatform.Legion => await GetDefaultsLegionAsync(mi).ConfigureAwait(false),
            GodModePlatform.NonGaming => [],
            _ => []
        };
    }

    public async Task RestoreDefaultsInOtherPowerModeAsync(PowerModeState state)
    {
        var config = await GetConfigAsync().ConfigureAwait(false);

        if (config.Platform == GodModePlatform.LegacyLegion)
        {
            await RestoreDefaultsLegacyAsync(state).ConfigureAwait(false);
            return;
        }

        if (config.Capabilities.Any(IsNvApiCapability))
        {
            try
            {
                Log.Instance.Trace($"Resetting PCF power target offset for power mode {state}...");
                await ResetTotalProcessingPowerTargetOffsetAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to reset PCF power target offset for {state}.", ex);
            }
        }
    }

    private async Task RestoreDefaultsLegacyAsync(PowerModeState state)
    {
        try
        {
            Log.Instance.Trace($"Restoring defaults for {state}...");
            var result = await GetDefaultsLegacyAsync().ConfigureAwait(false);

            if (!result.TryGetValue(state, out var defaults))
            {
                Log.Instance.Trace($"Defaults for {state} not found. Skipping...");
                return;
            }

            if (defaults.CPULongTermPowerLimit is { } longTermPowerLimit)
            {
                await WMI.LenovoCpuMethod.CPUSetLongTermPowerLimitAsync(longTermPowerLimit).ConfigureAwait(false);
            }
            if (defaults.CPUShortTermPowerLimit is { } shortTermPowerLimit)
            {
                await WMI.LenovoCpuMethod.CPUSetShortTermPowerLimitAsync(shortTermPowerLimit).ConfigureAwait(false);
            }
            if (defaults.CPUPeakPowerLimit is { } peakPowerLimit)
            {
                await WMI.LenovoCpuMethod.CPUSetPeakPowerLimitAsync(peakPowerLimit).ConfigureAwait(false);
            }
            if (defaults.CPUCrossLoadingPowerLimit is { } crossLoadingPowerLimit)
            {
                await WMI.LenovoCpuMethod.CPUSetCrossLoadingPowerLimitAsync(crossLoadingPowerLimit).ConfigureAwait(false);
            }
            if (defaults.APUsPPTPowerLimit is { } spptPowerLimit)
            {
                await WMI.LenovoCpuMethod.SetAPUSPPTPowerLimitAsync(spptPowerLimit).ConfigureAwait(false);
            }
            if (defaults.CPUTemperatureLimit is { } cpuTemperatureLimit)
            {
                await WMI.LenovoCpuMethod.CPUSetTemperatureControlAsync(cpuTemperatureLimit).ConfigureAwait(false);
            }
            if (defaults.GPUPowerBoost is { } gpuPowerBoost)
            {
                await WMI.LenovoGpuMethod.GPUSetPPABPowerLimitAsync(gpuPowerBoost).ConfigureAwait(false);
            }
            if (defaults.GPUConfigurableTGP is { } configurableTgp)
            {
                await WMI.LenovoGpuMethod.GPUSetCTGPPowerLimitAsync(configurableTgp).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to restore defaults for {state}.", ex);
        }
    }

    private async Task<Dictionary<PowerModeState, GodModeDefaults>> GetDefaultsLegacyAsync()
    {
        try
        {
            Log.Instance.Trace($"Getting defaults in other power modes...");
            var defaultFanTable = await GetDefaultFanTableAsync().ConfigureAwait(false);
            var result = await WMI.LenovoDefaultValueInDifferentModeData.ReadAsync().ConfigureAwait(false);

            return result.Select(d =>
                {
                    var powerMode = (PowerModeState)(d.Mode - 1);
                    var defaults = new GodModeDefaults
                    {
                        CPULongTermPowerLimit = d.CPULongTermPowerLimit,
                        CPUShortTermPowerLimit = d.CPUShortTermPowerLimit,
                        CPUPeakPowerLimit = d.CPUPeakPowerLimit,
                        CPUCrossLoadingPowerLimit = d.CPUCrossLoadingPowerLimit,
                        APUsPPTPowerLimit = d.APUsPPTPowerLimit,
                        CPUTemperatureLimit = d.CPUTemperatureLimit,
                        GPUPowerBoost = d.GPUPowerBoost,
                        GPUConfigurableTGP = d.GPUConfigurableTGP,
                        GPUTemperatureLimit = d.GPUTemperatureLimit,
                        FanTable = defaultFanTable,
                        FanFullSpeed = false
                    };
                    return (powerMode, defaults);
                })
                .Where(d => d.powerMode is PowerModeState.Quiet or PowerModeState.Balance or PowerModeState.Performance)
                .DistinctBy(d => d.powerMode)
                .ToDictionary(d => d.powerMode, d => d.defaults);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to get defaults.", ex);
            return [];
        }
    }

    private async Task<Dictionary<PowerModeState, GodModeDefaults>> GetDefaultsLegionAsync(MachineInformation mi)
    {
        try
        {
            Log.Instance.Trace($"Getting defaults in other power modes...");
            var result = new Dictionary<PowerModeState, GodModeDefaults>();
            var allCapabilityData = await WMI.LenovoCapabilityData01.ReadAsync().ConfigureAwait(false);
            allCapabilityData = allCapabilityData.ToArray();

            var powerModes = new List<PowerModeState> { PowerModeState.Quiet, PowerModeState.Balance, PowerModeState.Performance };
            if (mi.Properties.SupportsExtremeMode)
            {
                powerModes.Add(PowerModeState.Extreme);
            }

            foreach (var powerMode in powerModes)
            {
                var defaults = new GodModeDefaults
                {
                    CPULongTermPowerLimit = GetDefVal(allCapabilityData, CapabilityID.CPULongTermPowerLimit, powerMode),
                    CPUShortTermPowerLimit = GetDefVal(allCapabilityData, CapabilityID.CPUShortTermPowerLimit, powerMode),
                    CPUPeakPowerLimit = GetDefVal(allCapabilityData, CapabilityID.CPUPeakPowerLimit, powerMode),
                    CPUCrossLoadingPowerLimit = GetDefVal(allCapabilityData, CapabilityID.CPUCrossLoadingPowerLimit, powerMode),
                    CPUPL2Tau = GetDefVal(allCapabilityData, CapabilityID.CPUPL2Tau, powerMode),
                    APUsPPTPowerLimit = GetDefVal(allCapabilityData, CapabilityID.APUsPPTPowerLimit, powerMode),
                    CPUTemperatureLimit = GetDefVal(allCapabilityData, CapabilityID.CPUTemperatureLimit, powerMode),
                    GPUPowerBoost = GetDefVal(allCapabilityData, CapabilityID.GPUPowerBoost, powerMode),
                    GPUConfigurableTGP = GetDefVal(allCapabilityData, CapabilityID.GPUConfigurableTGP, powerMode),
                    GPUTemperatureLimit = GetDefVal(allCapabilityData, CapabilityID.GPUTemperatureLimit, powerMode),
                    GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline = GetDefVal(allCapabilityData, CapabilityID.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline, powerMode),
                    GPUToCPUDynamicBoost = GetDefVal(allCapabilityData, CapabilityID.GPUToCPUDynamicBoost, powerMode),
                    FanTable = await GetDefaultFanTableAsync().ConfigureAwait(false),
                    FanFullSpeed = false,
                    PrecisionBoostOverdriveScaler = 0,
                    PrecisionBoostOverdriveBoostFrequency = 0,
                    AllCoreCurveOptimizer = 0,
                    EnableAllCoreCurveOptimizer = false,
                    EnableOverclocking = false,
                };
                result[powerMode] = defaults;
            }

            Log.Instance.Trace($"Defaults in other power modes retrieved.");
            return result;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to get defaults.", ex);
            return [];
        }
    }

    #endregion

    #region Fan Tables

    public async Task<bool> SetFanTableWithVerifyAsync(FanTable fanTable)
    {
        var expected = fanTable.GetTable();
        int maxAttempts = 3;
        int delayMs = 100;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var fanTableBytes = fanTable.GetBytes();
                await WMI.LenovoFanMethod.FanSetTableAsync(fanTableBytes).ConfigureAwait(false);
                await Task.Delay(delayMs).ConfigureAwait(false);

                var readBack1 = await WMI.LenovoFanMethod.FanGetTableAsync(1, 1).ConfigureAwait(false);
                bool firstMatch = readBack1.Length == 10 && readBack1.Select(b => (ushort)b).SequenceEqual(expected);
                Log.Instance.Trace($"Fan table verify attempt {attempt}, read 1: {(firstMatch ? "OK" : "Mismatch")}");

                await Task.Delay(delayMs).ConfigureAwait(false);
                var readBack2 = await WMI.LenovoFanMethod.FanGetTableAsync(1, 1).ConfigureAwait(false);
                bool secondMatch = readBack2.Length == 10 && readBack2.Select(b => (ushort)b).SequenceEqual(expected);
                Log.Instance.Trace($"Fan table verify attempt {attempt}, read 2: {(secondMatch ? "OK" : "Mismatch")}");

                if (firstMatch && secondMatch)
                {
                    Log.Instance.Trace($"Fan table verification succeeded on attempt {attempt}.");
                    return true;
                }

                Log.Instance.Trace($"Fan table verification attempt {attempt} failed (mismatch), retrying write...");
            }
            catch (Exception ex) when (ex is TimeoutException || ex is COMException)
            {
                Log.Instance.Trace($"Fan table verify attempt {attempt} transient error: {ex.Message}. Retrying...");
                if (attempt == maxAttempts)
                {
                    throw new Exception(Resource.GodModeController_FanSetTableError_Message, ex);
                }
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Fan table verify attempt {attempt} fatal error: {ex.Message}.");
                throw new Exception(Resource.GodModeController_FanSetTableError_Message, ex);
            }
        }

        Log.Instance.Trace($"Fan table verification failed after {maxAttempts} attempts.");
        throw new FanTableNotKeptException(Resource.GodModeController_FanSetTableError_Message);
    }

    public Task<FanTable> GetDefaultFanTableAsync()
    {
        var fanTable = new FanTable([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
        return Task.FromResult(fanTable);
    }

    public async Task<FanTable> GetMinimumFanTableAsync()
    {
        var config = await GetConfigAsync().ConfigureAwait(false);
        return config.Platform switch
        {
            GodModePlatform.LegacyLegion => new FanTable([0, 0, 0, 0, 0, 0, 0, 1, 3, 5]),
            _ => config.MinimumFanTable,
        };
    }

    private static async Task<FanTableData[]?> TryGetFanTableDataAsync(GodModePlatformConfiguration config)
    {
        if (config.Platform is not (GodModePlatform.Legion or GodModePlatform.NonGaming))
        {
            return null;
        }

        try
        {
            return await GetFanTableDataLegionAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to read fan table data.", ex);
            return null;
        }
    }

    private static async Task<FanTableData[]?> GetFanTableDataLegacyAsync()
    {
        Log.Instance.Trace($"Reading fan table data...");
        var data = await WMI.LenovoFanTableData.ReadAsync().ConfigureAwait(false);

        var fanTableData = data
            .Select(d =>
            {
                var type = (d.fanId, d.sensorId) switch
                {
                    (0, 3) => FanTableType.CPU,
                    (1, 4) => FanTableType.GPU,
                    (0, 0) => FanTableType.CPUSensor,
                    _ => FanTableType.Unknown,
                };
                return new FanTableData(type, d.fanId, d.sensorId, d.fanTableData, d.sensorTableData);
            })
            .ToArray();

        if (fanTableData.Length != 3) { Log.Instance.Trace($"Bad fan table length"); return null; }
        if (fanTableData.Count(ftd => ftd.FanSpeeds.Length == 10) != 3) { Log.Instance.Trace($"Bad fan table speeds length"); return null; }
        if (fanTableData.Count(ftd => ftd.Temps.Length == 10) != 3) { Log.Instance.Trace($"Bad fan table temps length"); return null; }

        Log.Instance.Trace($"Fan table data retrieved.");
        return fanTableData;
    }

    private static async Task<FanTableData[]?> GetFanTableDataLegionAsync(PowerModeState powerModeState = PowerModeState.GodMode)
    {
        Log.Instance.Trace($"Reading fan table data...");
        var data = await WMI.LenovoFanTableData.ReadAsync().ConfigureAwait(false);
        var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);

        var fanTableData = data
            .Where(d => d.mode == (int)powerModeState + 1)
            .Select(d =>
            {
                var type = ResolveFanTableType(d.fanId, d.sensorId, mi.SmartFanVersion, mi);
                return new FanTableData(type, d.fanId, d.sensorId, d.fanTableData, d.sensorTableData);
            })
            .ToArray();

        if (!IsValidFanTableData(fanTableData))
        {
            Log.Instance.Trace($"Bad fan table: {string.Join(", ", fanTableData)}");
            return null;
        }

        Log.Instance.Trace($"Fan table data retrieved.");
        return fanTableData;
    }

    private static FanTableType ResolveFanTableType(byte fanId, byte sensorId, int smartFanVersion, MachineInformation mi)
    {
        if (smartFanVersion <= 7)
            return ResolveFanTableTypeUpToV7(fanId, sensorId);

        var isV3Model = GetIsV3StyleModel(mi);
        if (isV3Model)
        {
            return (fanId, sensorId) switch
            {
                (1, 1) => FanTableType.CPU,
                (2, 5) => FanTableType.GPU,
                (1, 4) => FanTableType.PCH,
                _ => FanTableType.Unknown,
            };
        }

        return (fanId, sensorId) switch
        {
            (1, 1) or (1, 4) => FanTableType.CPU,
            (2, 5) => FanTableType.GPU,
            (4, 4) or (5, 5) or (4, 1) => FanTableType.PCH,
            _ => FanTableType.Unknown,
        };
    }

    // Smart Fan version 7 and below. (The old switch also listed patterns for version 8 here,
    // which could never match inside the "<= 7" branch; they were removed with no change in behavior.)
    internal static FanTableType ResolveFanTableTypeUpToV7(byte fanId, byte sensorId) => (fanId, sensorId) switch
    {
        (1, 4) => FanTableType.CPU,
        (1, 1) => FanTableType.CPUSensor,
        (2, 5) => FanTableType.GPU,
        (3, 5) => FanTableType.GPU2,
        _ => FanTableType.Unknown,
    };

    private static bool GetIsV3StyleModel(MachineInformation mi)
    {
        var affectedSeries = new[] { LegionSeries.Legion_5, LegionSeries.Legion_7 };
        var affectedModels = new[] { "Legion 5", "Legion 7", "Legion Pro 5 16IAX10H", "LOQ", "Y7000", "R7000" };
        var isAffectedSeries = affectedSeries.Any(s => Compatibility.GetLegionSeries(mi.Model, mi.MachineType) == s);
        var isAffectedModel = affectedModels.Any(m => mi.Model.Contains(m));
        return (isAffectedSeries || isAffectedModel) && mi.Generation >= 10;
    }

    private static bool IsValidFanTableData(FanTableData[]? fanTableData)
    {
        return fanTableData?.All(ftd =>
            ftd.Type != FanTableType.Unknown &&
            ftd.FanSpeeds?.Length == 10 &&
            ftd.Temps?.Length == 10) ?? false;
    }

    #endregion

    #region Fan Full Speed

    private static async Task<bool?> GetFanFullSpeedAsync(GodModePlatformConfiguration config)
    {
        try
        {
            var fanFullSpeedCap = config.Capabilities.FirstOrDefault(c => c.PropertyName == nameof(GodModePreset.FanFullSpeed));
            if (fanFullSpeedCap == null)
                return await WMI.LenovoFanMethod.FanGetFullSpeedAsync().ConfigureAwait(false);
            var value = await GetValueAsync(fanFullSpeedCap.RawId, config.CapabilityIdMask).ConfigureAwait(false);
            return value != 0;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to read FanFullSpeed.", ex);
            return null;
        }
    }

    private static async Task SetFanFullSpeedAsync(GodModePlatformConfiguration config, bool enabled)
    {
        try
        {
            var fanFullSpeedCap = config.Capabilities.FirstOrDefault(c => c.PropertyName == nameof(GodModePreset.FanFullSpeed));
            if (fanFullSpeedCap == null)
            {
                await WMI.LenovoFanMethod.FanSetFullSpeedAsync(enabled ? 1 : 0).ConfigureAwait(false);
            }
            else
            {
                await SetValueAsync(fanFullSpeedCap.RawId, enabled ? 1 : 0, config.CapabilityIdMask).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to set FanFullSpeed.", ex);
        }
    }

    #endregion

    #region Capability R/W Helpers

    private static Task<int> GetValueAsync(uint rawId, uint mask)
    {
        var idRaw = rawId & mask;
        return WMI.LenovoOtherMethod.GetFeatureValueAsync(idRaw);
    }

    private static Task SetValueAsync(uint rawId, int value, uint mask)
    {
        var idRaw = rawId & mask;
        return WMI.LenovoOtherMethod.SetFeatureValueAsync(idRaw, value);
    }

    private static bool IsNvApiCapability(GodModeCapabilityEntry capability)
    {
        return capability.CapabilityId.GetType() == typeof(NvApiCapabilityID);
    }

    private static Task<int> GetCapabilityValueAsync(GodModeCapabilityEntry capability, uint mask) =>
        IsNvApiCapability(capability)
            ? GetPcfTotalProcessingPowerTargetOffsetAsync()
            : GetValueAsync(capability.RawId, mask);

    private static Task SetCapabilityValueAsync(GodModeCapabilityEntry capability, int value, uint mask) =>
        IsNvApiCapability(capability)
            ? SetPcfTotalProcessingPowerTargetOffsetAsync(value)
            : SetValueAsync(capability.RawId, value, mask);

    private static StepperValue CreateCapabilityStepperValue(GodModeCapabilityEntry capability, int value)
    {
        if (capability.Steps.Length == 0)
            return new StepperValue(value, capability.Min, capability.Max, capability.Step, [], capability.DefaultValue);

        var closestValue = capability.Steps.Contains(value)
            ? value
            : capability.Steps.MinBy(step => Math.Abs((long)step - value));
        return new StepperValue(closestValue, 0, 0, 0, capability.Steps, capability.DefaultValue);
    }

    private static Task<int> GetPcfTotalProcessingPowerTargetOffsetAsync() => Task.Run(() =>
    {
        using var controller = new PcfPowerController();
        var offsetInWatts = CalculatePcfTotalProcessingPowerTargetOffsetInWatts(controller.GetPowerValues());
        Log.Instance.Trace($"Read GPU TPP target offset through NVAPI PCF. [offset={offsetInWatts}W]");
        return offsetInWatts;
    });

    private static int CalculatePcfTotalProcessingPowerTargetOffsetInWatts(PcfPowerValues values)
    {
        var targetInMilliwatts = values.ACTargetTPPLimitInMilliwatts;
        var baselineInMilliwatts = values.ACDefaultGPULimitInMilliwatts;

        if (targetInMilliwatts == uint.MaxValue || targetInMilliwatts <= baselineInMilliwatts)
        {
            return 0;
        }

        return checked((int)(((long)targetInMilliwatts - baselineInMilliwatts) / 1000));
    }

    private static Task SetPcfTotalProcessingPowerTargetOffsetAsync(int offsetInWatts) => Task.Run(() =>
    {
        using var controller = new PcfPowerController();
        Log.Instance.Trace($"Writing GPU TPP target offset through NVAPI PCF. [offset={offsetInWatts}W]");
        var values = controller.GetPowerValues();
        var targetInMilliwatts = checked((uint)(values.ACDefaultGPULimitInMilliwatts + offsetInWatts * 1000));
        controller.SetPowerField(PcfPowerFields.ACTargetTPPLimit, targetInMilliwatts);
    });

    private static Task ResetTotalProcessingPowerTargetOffsetAsync() => Task.Run(() =>
    {
        using var controller = new PcfPowerController();
        Log.Instance.Trace($"Resetting GPU TPP target offset through NVAPI PCF.");
        controller.ResetPowerField(PcfPowerFields.ACTargetTPPLimit);
    });

    private static CapabilityID AdjustCapabilityIdForPowerMode(CapabilityID id, PowerModeState powerMode)
    {
        var idRaw = (uint)id & CAPABILITY_ID_MASK;
        var powerModeRaw = ((uint)powerMode + 1) << 8;
        return (CapabilityID)(idRaw + powerModeRaw);
    }

    private static int? GetDefVal(IEnumerable<RangeCapability> capabilities, CapabilityID id, PowerModeState powerMode)
    {
        var adjustedId = AdjustCapabilityIdForPowerMode(id, powerMode);
        var value = capabilities
            .Where(c => c.Id == adjustedId)
            .Select(c => c.DefaultValue)
            .DefaultIfEmpty(-1)
            .First();
        return value < 0 ? null : value;
    }

    #endregion

    #region State Store Helpers

    private static bool IsValidStore(GodModeSettings.GodModeSettingsStore store) => store.Presets.Count != 0 && store.Presets.ContainsKey(store.ActivePresetId);

    private async Task<GodModeState> LoadStateFromStoreAsync(GodModeSettings.GodModeSettingsStore store, GodModePreset defaultState)
    {
        var states = new Dictionary<Guid, GodModePreset>();
        var mi = await GetMachineInformationAsync().ConfigureAwait(false);
        var isAmdDevice = mi.Properties.IsAmdDevice;

        foreach (var (id, preset) in store.Presets)
        {
            StepperValue? pboScaler = null, pboFreq = null, coreCurve = null;

            var pboSettings = new StepperValue?[]
            {
                preset.PrecisionBoostOverdriveScaler,
                preset.PrecisionBoostOverdriveBoostFrequency,
                preset.AllCoreCurveOptimizer
            };

            if (pboSettings.Any(s => s == null) && isAmdDevice)
            {
                pboScaler = new StepperValue(0, 0, 7, 1, [], 0);
                pboFreq = new StepperValue(0, 0, 200, 1, [], 0);
                coreCurve = new StepperValue(0, 0, 20, 1, [], 0);
            }

            states.Add(id, new GodModePreset
            {
                Name = preset.Name,
                CPULongTermPowerLimit = CreateStepperValue(defaultState.CPULongTermPowerLimit, preset.CPULongTermPowerLimit, preset.MinValueOffset, preset.MaxValueOffset),
                CPUShortTermPowerLimit = CreateStepperValue(defaultState.CPUShortTermPowerLimit, preset.CPUShortTermPowerLimit, preset.MinValueOffset, preset.MaxValueOffset),
                CPUPeakPowerLimit = CreateStepperValue(defaultState.CPUPeakPowerLimit, preset.CPUPeakPowerLimit, preset.MinValueOffset, preset.MaxValueOffset),
                CPUCrossLoadingPowerLimit = CreateStepperValue(defaultState.CPUCrossLoadingPowerLimit, preset.CPUCrossLoadingPowerLimit, preset.MinValueOffset, preset.MaxValueOffset),
                CPUPL2Tau = CreateStepperValue(defaultState.CPUPL2Tau, preset.CPUPL2Tau, preset.MinValueOffset, preset.MaxValueOffset),
                APUsPPTPowerLimit = CreateStepperValue(defaultState.APUsPPTPowerLimit, preset.APUsPPTPowerLimit, preset.MinValueOffset, preset.MaxValueOffset),
                CPUTemperatureLimit = CreateStepperValue(defaultState.CPUTemperatureLimit, preset.CPUTemperatureLimit, preset.MinValueOffset, preset.MaxValueOffset),
                GPUPowerBoost = CreateStepperValue(defaultState.GPUPowerBoost, preset.GPUPowerBoost, preset.MinValueOffset, preset.MaxValueOffset),
                GPUConfigurableTGP = CreateStepperValue(defaultState.GPUConfigurableTGP, preset.GPUConfigurableTGP, preset.MinValueOffset, preset.MaxValueOffset),
                GPUTemperatureLimit = CreateStepperValue(defaultState.GPUTemperatureLimit, preset.GPUTemperatureLimit, preset.MinValueOffset, preset.MaxValueOffset),
                GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline = CreateStepperValue(defaultState.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline, preset.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline, preset.MinValueOffset, preset.MaxValueOffset),
                GPUTotalProcessingPowerTargetOnAcOffsetFromBaselineNVAPI = CreateStepperValue(defaultState.GPUTotalProcessingPowerTargetOnAcOffsetFromBaselineNVAPI, preset.GPUTotalProcessingPowerTargetOnAcOffsetFromBaselineNVAPI, preset.MinValueOffset, preset.MaxValueOffset),
                GPUToCPUDynamicBoost = CreateStepperValue(defaultState.GPUToCPUDynamicBoost, preset.GPUToCPUDynamicBoost),
                FanTableInfo = await GetFanTableInfoAsync(preset, defaultState.FanTableInfo?.Data).ConfigureAwait(false),
                FanFullSpeed = preset.FanFullSpeed,
                MinValueOffset = preset.MinValueOffset ?? defaultState.MinValueOffset,
                MaxValueOffset = preset.MaxValueOffset ?? defaultState.MaxValueOffset,
                PrecisionBoostOverdriveScaler = (isAmdDevice && pboScaler != null) ? pboScaler : preset.PrecisionBoostOverdriveScaler,
                PrecisionBoostOverdriveBoostFrequency = (isAmdDevice && pboFreq != null) ? pboFreq : preset.PrecisionBoostOverdriveBoostFrequency,
                AllCoreCurveOptimizer = (isAmdDevice && coreCurve != null) ? coreCurve : preset.AllCoreCurveOptimizer,
                EnableAllCoreCurveOptimizer = (isAmdDevice && preset.EnableAllCoreCurveOptimizer == null) ? false : preset.EnableAllCoreCurveOptimizer,
                EnableOverclocking = (isAmdDevice && preset.EnableOverclocking == null) ? false : preset.EnableOverclocking,
                Overrides = new Dictionary<PowerOverrideKey, string>(preset.Overrides ?? []),
            });
        }

        return new GodModeState
        {
            ActivePresetId = store.ActivePresetId,
            Presets = states.AsReadOnlyDictionary()
        };
    }

    /// <summary>
    /// A saved value fitted to what the firmware allows: <paramref name="state"/> carries the firmware's range (or
    /// its list of steps) and default, <paramref name="store"/> the saved value. A value off the step grid moves
    /// to the nearest step; a value outside the range becomes the firmware's default (or the nearest end).
    /// <para>
    /// The two offsets can only narrow the range (raise the minimum, lower the maximum). They can never widen it:
    /// nothing above the firmware's maximum or below its minimum comes out of here.
    /// </para>
    /// Returns nothing when the firmware reported no range and no steps for the setting.
    /// </summary>
    internal static StepperValue? CreateStepperValue(StepperValue? state, StepperValue? store = null, int? minValueOffset = 0, int? maxValueOffset = 0)
    {
        if (state is not { } stateValue)
            return null;

        if (stateValue.Steps.Length > 0)
        {
            var value = store?.Value ?? stateValue.Value;
            var steps = stateValue.Steps;
            var defaultValue = stateValue.DefaultValue;

            if (!steps.Contains(value))
            {
                var valueTemp = value;
                value = steps.MinBy(v => Math.Abs((long)v - valueTemp));
            }

            return new(value, 0, 0, 0, steps, defaultValue);
        }

        if (stateValue.Step > 0)
        {
            var value = store?.Value ?? stateValue.Value;
            var min = Math.Max(0, stateValue.Min + Math.Max(0, minValueOffset ?? 0));
            var max = stateValue.Max + Math.Min(0, maxValueOffset ?? 0);
            if (max < min)
            {
                // Offsets that leave no room are ignored rather than obeyed.
                min = Math.Max(0, stateValue.Min);
                max = stateValue.Max;
            }
            var step = stateValue.Step;
            var defaultValue = stateValue.DefaultValue;

            value = MathExtensions.RoundNearest(value, step);

            if (value < min || value > max)
                value = defaultValue ?? Math.Clamp(value, min, max);

            return new(value, min, max, step, [], defaultValue);
        }

        return null;
    }

    /// <summary>Whether a fan table stays within this laptop's minimum and the firmware's maximum level (10).</summary>
    public async Task<bool> IsValidFanTableAsync(FanTable fanTable)
    {
        var minimumFanTable = await GetMinimumFanTableAsync().ConfigureAwait(false);
        var minimum = minimumFanTable.GetTable();
        return fanTable.GetTable().Where((t, i) => t < minimum[i] || t > 10u).IsEmpty();
    }

    private async Task<FanTableInfo?> GetFanTableInfoAsync(GodModeSettings.GodModeSettingsStore.Preset preset, FanTableData[]? fanTableData)
    {
        Log.Instance.Trace($"Getting fan table info...");
        if (fanTableData == null) { Log.Instance.Trace($"Fan table data == null"); return null; }
        Log.Instance.Trace($"Fan table data retrieved.");

        var fanTable = preset.FanTable ?? await GetDefaultFanTableAsync().ConfigureAwait(false);
        if (!await IsValidFanTableAsync(fanTable).ConfigureAwait(false))
        {
            Log.Instance.Trace($"Fan table invalid, replacing with default...");
            fanTable = await GetDefaultFanTableAsync().ConfigureAwait(false);
        }
        return new FanTableInfo(fanTableData, fanTable);
    }

    #endregion

    #region Event

    protected async Task RaisePresetChanged(Guid presetId)
    {
        var (_, preset) = await GetActivePresetAsync().ConfigureAwait(false);
        try
        {
            var feature = IoCContainer.Resolve<PowerModeFeature>();
            await feature.EnsureCorrectWindowsPowerSettingsAreSetAsync(preset).ConfigureAwait(false);
        }
        catch { /* feature may not be available in all contexts */ }
        PresetChanged?.Invoke(this, presetId);
    }

    #endregion

    #region Init

    private async Task<GodModePlatformConfiguration> GetConfigAsync()
    {
        if (_config != null)
        {
            return _config;
        }

        var mi = await GetMachineInformationAsync().ConfigureAwait(false);
        _config = mi.Properties.GodModePlatform switch
        {
            GodModePlatform.LegacyLegion => GodModePlatformConfiguration.LegacyLegion,
            GodModePlatform.Legion => GodModePlatformConfiguration.Legion,
            GodModePlatform.NonGaming => GodModePlatformConfiguration.NonGaming,
            _ => throw new InvalidOperationException("Unsupported GodMode platform"),
        };
        return _config;
    }

    private async Task<MachineInformation> GetMachineInformationAsync()
    {
        if (_mi.HasValue)
            return _mi.Value;
        _mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);
        return _mi.Value;
    }

    #endregion

    #region Legacy V1 WMI Helpers

    private static async Task<StepperValue> GetCPULongTermPowerLimitLegacyAsync()
    {
        var defaultValue = await WMI.LenovoCpuMethod.CPUGetDefaultPowerLimitAsync().OrNullIfException().ConfigureAwait(false);
        var (value, min, max, step) = await WMI.LenovoCpuMethod.CPUGetLongTermPowerLimitAsync().ConfigureAwait(false);
        return new(value, min, max, step, [], defaultValue?.longTerm);
    }

    private static async Task<StepperValue> GetCPUShortTermPowerLimitLegacyAsync()
    {
        var defaultValue = await WMI.LenovoCpuMethod.CPUGetDefaultPowerLimitAsync().OrNullIfException().ConfigureAwait(false);
        var (value, min, max, step) = await WMI.LenovoCpuMethod.CPUGetShortTermPowerLimitAsync().ConfigureAwait(false);
        return new(value, min, max, step, [], defaultValue?.shortTerm);
    }

    private static async Task<StepperValue> GetCPUPeakPowerLimitLegacyAsync()
    {
        var (value, min, max, step, defaultValue) = await WMI.LenovoCpuMethod.CPUGetPeakPowerLimitAsync().ConfigureAwait(false);
        return new(value, min, max, step, [], defaultValue);
    }

    private static async Task<StepperValue> GetCPUCrossLoadingPowerLimitLegacyAsync()
    {
        var (value, min, max, step, defaultValue) = await WMI.LenovoCpuMethod.CPUGetCrossLoadingPowerLimitAsync().ConfigureAwait(false);
        return new(value, min, max, step, [], defaultValue);
    }

    private static async Task<StepperValue> GetAPUSPPTPowerLimitLegacyAsync()
    {
        var (value, min, max, step, defaultValue) = await WMI.LenovoCpuMethod.GetAPUSPPTPowerLimitAsync().ConfigureAwait(false);
        return new(value, min, max, step, [], defaultValue);
    }

    private static async Task<StepperValue> GetCPUTemperatureLimitLegacyAsync()
    {
        var (value, min, max, step, defaultValue) = await WMI.LenovoCpuMethod.CPUGetTemperatureControlAsync().ConfigureAwait(false);
        return new(value, min, max, step, [], defaultValue);
    }

    private static async Task<StepperValue> GetGPUConfigurableTGPLegacyAsync()
    {
        var defaultValue = await WMI.LenovoGpuMethod.GPUGetDefaultPPABcTGPPowerLimit().OrNullIfException().ConfigureAwait(false);
        var (value, min, max, step) = await WMI.LenovoGpuMethod.GPUGetCTGPPowerLimitAsync().ConfigureAwait(false);
        return new(value, min, max, step, [], defaultValue?.ctgp);
    }

    private static async Task<StepperValue> GetGPUPowerBoostLegacyAsync()
    {
        var defaultValue = await WMI.LenovoGpuMethod.GPUGetDefaultPPABcTGPPowerLimit().OrNullIfException().ConfigureAwait(false);
        var (value, min, max, step) = await WMI.LenovoGpuMethod.GPUGetPPABPowerLimitAsync().ConfigureAwait(false);
        return new(value, min, max, step, [], defaultValue?.ppab);
    }

    private static async Task<StepperValue> GetGPUTemperatureLimitLegacyAsync()
    {
        var (value, min, max, step, defaultValue) = await WMI.LenovoGpuMethod.GPUGetTemperatureLimitAsync().ConfigureAwait(false);
        return new(value, min, max, step, [], defaultValue);
    }

    #endregion

    #region OC Helpers

    private static async Task<bool> IsBiosOcEnabledAsync()
    {
        try
        {
            var result = await WMI.LenovoGameZoneData.GetBIOSOCMode().ConfigureAwait(false);
            return result == BIOS_OC_MODE_ENABLED;
        }
        catch (ManagementException)
        {
            return false;
        }
    }

    #endregion
}
