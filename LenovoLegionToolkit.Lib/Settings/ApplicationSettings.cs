using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using static LenovoLegionToolkit.Lib.Settings.ApplicationSettings;

namespace LenovoLegionToolkit.Lib.Settings;

public class ApplicationSettings : AbstractSettings<ApplicationSettingsStore>
{
    public class ApplicationSettingsStore
    {
        public Theme Theme { get; set; }
        public RGBColor? AccentColor { get; set; }
        public AccentColorSource AccentColorSource { get; set; }
        public PowerModeMappingMode PowerModeMappingMode { get; set; } = PowerModeMappingMode.Disabled;
        public Dictionary<PowerModeState, Guid> PowerPlans { get; set; } = [];
        public Dictionary<PowerModeState, WindowsPowerMode> PowerModes { get; set; } = [];
        public Dictionary<PowerModeState, Dictionary<PowerOverrideKey, string>> Overrides { get; set; } = [];

        public Dictionary<ITSMode, Guid> ITSPowerPlans { get; set; } = [];
        public Dictionary<ITSMode, WindowsPowerMode> ITSPowerModes { get; set; } = [];
        public Dictionary<ITSMode, Dictionary<PowerOverrideKey, string>> ITSOverrides { get; set; } = [];

        public bool MinimizeToTray { get; set; } = true;
        public bool MinimizeOnClose { get; set; }
        public bool LegacyPluginNoticeShown { get; set; }
        public WindowSize? WindowSize { get; set; }
        public TemperatureUnit TemperatureUnit { get; set; }
        public List<RefreshRate> ExcludedRefreshRates { get; set; } = [];
        public WarrantyInfo? WarrantyInfo { get; set; }
        public bool SynchronizeBrightnessToAllPowerPlans { get; set; }
        public ModifierKey SmartFnLockFlags { get; set; }
        public bool ResetBatteryOnSinceTimerOnReboot { get; set; }
        public bool UseNewSensorDashboard { get; set; }
        public bool EnableHardwareSensors { get; set; }
        public bool LockWindowSize { get; set; }
        public bool AlwaysOnTop { get; set; }
        public bool CompactMode { get; set; }
        public WindowPosition? WindowPosition { get; set; }
        public bool RememberWindowPosition { get; set; } = true;
        public bool EnableLogging { get; set; }
        public string BackGroundImageFilePath { get; set; } = string.Empty;
        public double Opacity { get; set; } = 0.3;
        public int BackgroundImageBlur { get; set; } = 0;
        public BackgroundImageStretchMode BackgroundImageStretch { get; set; } = BackgroundImageStretchMode.Crop;
        public List<string> ExcludedProcesses { get; set; } = [];
        public List<string> IncludedProcesses { get; set; } = [];
        public GameDetectionSettings GameDetection { get; set; } = new();
        public bool DynamicLightingWarningDontShowAgain { get; set; }
        public bool CustomModeWarningDontShowAgain { get; set; }
        public bool EnableHardwareAcceleration { get; set; }
        public int GPUMonitoringInterval { get; set; } = 5000;
        public int GPUMonitoringStartupDelay { get; set; } = 1000;
        public int GPUKillProcessDelay { get; set; } = 500;
        public WindowBackdropType BackdropType { get; set; } = WindowBackdropType.Mica;
        public bool KeepDgpuAwake { get; set; }
        public int LockedPStateId { get; set; } = -1;
        public UiSkin Skin { get; set; } = UiSkin.Fluent;
        public int NeonGlow { get; set; } = 60;
        public RetroPhosphor RetroPhosphor { get; set; } = RetroPhosphor.Green;
        public int RetroScanlines { get; set; } = 50;
        public bool RetroGlow { get; set; } = true;
        public AnimationLevel Animations { get; set; } = AnimationLevel.Full;
        public bool ShowStatusStrip { get; set; } = true;
        public bool ShowHelpTooltips { get; set; } = true;
        public int HelpTooltipDelayMs { get; set; } = 400;
        public bool GameSafeMode { get; set; } = true;
        public bool PowerModeDetailsExpanded { get; set; } = true;
    }
    public class GameDetectionSettings
    {
        public bool UseDiscreteGPU { get; set; } = true;
        public bool UseGameConfigStore { get; set; } = true;
        public bool UseEffectiveGameMode { get; set; } = true;
    }

    public ApplicationSettings() : base("settings.json")
    {
    }
}
