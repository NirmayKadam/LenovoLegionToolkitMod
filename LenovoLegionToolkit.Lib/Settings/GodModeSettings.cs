using System;
using System.Collections.Generic;
using static LenovoLegionToolkit.Lib.Settings.GodModeSettings;

namespace LenovoLegionToolkit.Lib.Settings;


public class GodModeSettings : AbstractSettings<GodModeSettingsStore>
{
    private const string FILE_NAME = "godmode.json";

    public GodModeSettings() : base(FILE_NAME) { }

    /// <summary>Keeps the file in <paramref name="folder"/>, so tests never touch the real settings.</summary>
    internal GodModeSettings(string folder) : base(FILE_NAME, folder) { }

    protected override string MigrateJson(string json) => SettingsMigration.RenameEnumValues(json, SettingsMigration.FanTableTypeRenames, onlyProperty: "Type");

    public class GodModeSettingsStore
    {
        public record Preset
        {
            public string Name { get; init; } = string.Empty;
            public Dictionary<PowerOverrideKey, string> Overrides { get; init; } = [];
            public StepperValue? CPULongTermPowerLimit { get; init; }
            public StepperValue? CPUShortTermPowerLimit { get; init; }
            public StepperValue? CPUPeakPowerLimit { get; init; }
            public StepperValue? CPUCrossLoadingPowerLimit { get; init; }
            public StepperValue? CPUPL2Tau { get; init; }
            public StepperValue? APUsPPTPowerLimit { get; init; }
            public StepperValue? CPUTemperatureLimit { get; init; }
            public StepperValue? PrecisionBoostOverdriveScaler { get; init; }
            public StepperValue? PrecisionBoostOverdriveBoostFrequency { get; init; }
            public StepperValue? AllCoreCurveOptimizer { get; init; }
            public bool? EnableAllCoreCurveOptimizer { get; init; }
            public bool? EnableOverclocking { get; init; }
            public StepperValue? GPUPowerBoost { get; init; }
            public StepperValue? GPUConfigurableTGP { get; init; }
            public StepperValue? GPUTemperatureLimit { get; init; }
            public StepperValue? GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline { get; init; }
            public StepperValue? GPUTotalProcessingPowerTargetOnAcOffsetFromBaselineNVAPI { get; init; }
            public StepperValue? GPUToCPUDynamicBoost { get; init; }
            public FanTable? FanTable { get; init; }
            public bool? FanFullSpeed { get; init; }
            public int? MinValueOffset { get; init; }
            public int? MaxValueOffset { get; init; }
        }

        public Guid ActivePresetId { get; set; }

        public Dictionary<Guid, Preset> Presets { get; set; } = [];
    }

    // ReSharper disable once StringLiteralTypo
}
