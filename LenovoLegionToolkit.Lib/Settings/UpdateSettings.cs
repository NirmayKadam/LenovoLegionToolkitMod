using System;
using static LenovoLegionToolkit.Lib.Settings.UpdateSettings;

namespace LenovoLegionToolkit.Lib.Settings;

public class UpdateSettings : AbstractSettings<UpdateSettingsStore>
{
    public UpdateSettings() : base("update_settings.json") { }

    /// <summary>For tests: the settings file lives in <paramref name="folder"/>.</summary>
    internal UpdateSettings(string folder) : base("update_settings.json", folder) { }

    public class UpdateSettingsStore
    {
        public DateTime? LastUpdateCheckDateTime { get; set; }
        public UpdateCheckFrequency UpdateCheckFrequency { get; set; }
        public UpdateChannel UpdateChannel { get; set; }
    }

    protected override UpdateSettingsStore Default => new()
    {
        LastUpdateCheckDateTime = null,
        UpdateCheckFrequency = UpdateCheckFrequency.PerDay,
        UpdateChannel = UpdateChannel.Stable
    };
}
