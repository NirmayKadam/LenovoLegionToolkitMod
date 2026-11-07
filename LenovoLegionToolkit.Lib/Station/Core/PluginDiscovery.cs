using System;
using System.Collections.Generic;
using System.IO;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Station.Core;

public static class PluginDiscovery
{
    /// <summary>
    /// Plugin DLLs under <paramref name="root"/>: top-level *.dll, Dependency\*.dll, and &lt;name&gt;\&lt;name&gt;.dll.
    /// Other subfolders (for example Configs) are not scanned.
    /// </summary>
    public static List<string> FindPluginDlls(string root)
    {
        var dlls = new List<string>();
        if (!Directory.Exists(root))
            return dlls;

        var depDir = Path.Combine(root, "Dependency");
        if (Directory.Exists(depDir))
            dlls.AddRange(Directory.EnumerateFiles(depDir, "*.dll", SearchOption.TopDirectoryOnly));

        dlls.AddRange(Directory.EnumerateFiles(root, "*.dll", SearchOption.TopDirectoryOnly));

        foreach (var subDir in Directory.EnumerateDirectories(root))
        {
            var folderName = Path.GetFileName(subDir);
            if (string.Equals(folderName, "Dependency", StringComparison.OrdinalIgnoreCase))
                continue;

            var candidateDll = Path.Combine(subDir, $"{folderName}.dll");
            if (File.Exists(candidateDll))
                dlls.Add(candidateDll);
        }

        return dlls;
    }

    /// <summary>
    /// Plugin DLLs in the old per-user folders (&lt;appData&gt;\Plugins and &lt;appData&gt;\Plugin), found with the same rules.
    /// These are never loaded, because any program running as the user can write there.
    /// </summary>
    public static List<string> FindLegacyPluginDlls(string appData)
    {
        var result = new List<string>();
        foreach (var name in new[] { "Plugins", "Plugin" })
        {
            try
            {
                result.AddRange(FindPluginDlls(Path.Combine(appData, name)));
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to scan old plugin folder {name}.", ex);
            }
        }
        return result;
    }
}
