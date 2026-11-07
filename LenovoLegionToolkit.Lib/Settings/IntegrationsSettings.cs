using static LenovoLegionToolkit.Lib.Settings.IntegrationsSettings;

namespace LenovoLegionToolkit.Lib.Settings;

public class IntegrationsSettings() : AbstractSettings<IntegrationsSettingsStore>("integrations.json")
{
    public class IntegrationsSettingsStore
    {
        public bool HWiNFO { get; set; }

        public bool CLI { get; set; }
    }
}
