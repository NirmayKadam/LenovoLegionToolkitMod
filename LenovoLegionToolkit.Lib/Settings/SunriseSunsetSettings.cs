using System;
using static LenovoLegionToolkit.Lib.Settings.SunriseSunsetSettings;

namespace LenovoLegionToolkit.Lib.Settings;

public class SunriseSunsetSettings : AbstractSettings<SunriseSunsetSettingsStore>
{
    private const string FileName = "sunrise_sunset.json";

    public SunriseSunsetSettings() : base(FileName) { }

    /// <summary>For tests: the settings file lives in <paramref name="folder"/>.</summary>
    internal SunriseSunsetSettings(string folder) : base(FileName, folder) { }

    public class SunriseSunsetSettingsStore
    {
        public DateTime? LastCheckDateTime { get; set; }
        public Time? Sunrise { get; set; }
        public Time? Sunset { get; set; }

        /// <summary>A location the user typed in; when set, sunrise and sunset are worked out from it with no network lookup.</summary>
        public double? Latitude { get; set; }

        public double? Longitude { get; set; }
    }
}
