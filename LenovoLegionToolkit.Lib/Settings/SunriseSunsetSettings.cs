using System;
using static LenovoLegionToolkit.Lib.Settings.SunriseSunsetSettings;

namespace LenovoLegionToolkit.Lib.Settings;

public class SunriseSunsetSettings() : AbstractSettings<SunriseSunsetSettingsStore>("sunrise_sunset.json")
{
    public class SunriseSunsetSettingsStore
    {
        public DateTime? LastCheckDateTime { get; set; }
        public Time? Sunrise { get; set; }
        public Time? Sunset { get; set; }
    }
}
