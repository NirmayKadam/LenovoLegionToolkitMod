using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Settings;

namespace LenovoLegionToolkit.Lib.Controllers.GodMode;

public interface IGodModeController
{
    event EventHandler<Guid> PresetChanged;
    Task<bool> NeedsVantageDisabledAsync();
    Task<bool> NeedsLegionZoneDisabledAsync();
    Task<bool> NeedsLegionSpaceDisabledAsync();
    Task<(Guid, GodModeSettings.GodModeSettingsStore.Preset)> GetActivePresetAsync();
    Task<Guid> GetActivePresetIdAsync();
    Task<string?> GetActivePresetNameAsync();
    Task<Dictionary<Guid, GodModeSettings.GodModeSettingsStore.Preset>> GetGodModePresetsAsync();
    Task<GodModeState> GetStateAsync();

    /// <summary>The active preset as it is sent: every value fitted to the firmware's range. Null if that can't be read.</summary>
    Task<GodModePreset?> GetRangeCheckedActivePresetAsync();
    Task SetStateAsync(GodModeState state);
    Task ApplyStateAsync();
    Task<FanTable> GetDefaultFanTableAsync();
    Task<FanTable> GetMinimumFanTableAsync();
    Task<Dictionary<PowerModeState, GodModeDefaults>> GetDefaultsInOtherPowerModesAsync();
    Task RestoreDefaultsInOtherPowerModeAsync(PowerModeState state);

    /// <summary>
    /// The values the firmware reports right now for these preset properties, read the way God Mode reads them, with
    /// the reason for each one that couldn't be read. Null on older (legacy) platforms, whose values are read through
    /// Lenovo's older CPU and GPU methods instead.
    /// </summary>
    Task<CurrentValues?> ReadCurrentValuesAsync(IReadOnlyCollection<string> propertyNames);
}

/// <summary>What <see cref="IGodModeController.ReadCurrentValuesAsync"/> read, by preset property name.</summary>
public sealed record CurrentValues(IReadOnlyDictionary<string, int> Values, IReadOnlyDictionary<string, Exception> Failures);
