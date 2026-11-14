using System;
using System.Collections.Generic;

namespace LenovoLegionToolkit.Lib.Settings;

/// <summary>Whether this laptop's firmware keeps a custom fan curve outside Custom mode.</summary>
public enum FanCurveFirmwareSupport
{
    Unknown,
    Accepts,
    Rejects,
}

/// <summary>
/// Fan curves the user set for the built-in power modes (Quiet, Balance, Performance, Extreme). Custom mode
/// keeps using its God Mode preset's curve. A mode without an entry uses the firmware's built-in curve.
/// </summary>
public class FanProfileSettings : AbstractSettings<FanProfileSettings.FanProfileSettingsStore>
{
    private const string FILE_NAME = "fan_profiles.json";

    public FanProfileSettings() : base(FILE_NAME) { }

    /// <summary>Keeps the file in <paramref name="folder"/>, so tests never touch the real settings.</summary>
    internal FanProfileSettings(string folder) : base(FILE_NAME, folder) { }

    public class FanProfileSettingsStore
    {
        public bool Enabled { get; set; }

        /// <summary>Fan level (0–10) at each of the firmware's ten temperature steps, per power mode.</summary>
        public Dictionary<PowerModeState, ushort[]> Curves { get; set; } = [];

        /// <summary>Result of the first test on this laptop; <see cref="FanCurveFirmwareSupport.Unknown"/> until then.</summary>
        public FanCurveFirmwareSupport FirmwareSupport { get; set; } = FanCurveFirmwareSupport.Unknown;

        /// <summary>
        /// The Custom preset created for each mode by the Custom-mode method, so creating it again replaces
        /// that preset instead of adding another one.
        /// </summary>
        public Dictionary<PowerModeState, Guid> FallbackPresetIds { get; set; } = [];
    }
}
