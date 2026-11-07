using System.Collections.Generic;
using static LenovoLegionToolkit.Lib.Settings.PackageDownloaderSettings;

namespace LenovoLegionToolkit.Lib.Settings;

public class PackageDownloaderSettings() : AbstractSettings<PackageDownloaderSettingsStore>("package_downloader.json")
{
    public class PackageDownloaderSettingsStore
    {
        public string? DownloadPath { get; set; }
        public bool OnlyShowUpdates { get; set; }
        public HashSet<string> HiddenPackages { get; set; } = [];
    }
}
