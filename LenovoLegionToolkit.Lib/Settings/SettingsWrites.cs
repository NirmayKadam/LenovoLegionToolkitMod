using System;
using System.Collections.Concurrent;
using System.IO;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Settings;

/// <summary>
/// Stops settings files in a folder from being written for the rest of this run. Restoring a backup or resetting
/// the settings replaces the files on disk and then restarts Indra; on the way out, windows and controllers still
/// save what they hold in memory (the OSD saves its position when it closes), which used to put the old settings
/// straight back over the restored or deleted files.
/// </summary>
public static class SettingsWrites
{
    private static readonly ConcurrentDictionary<string, bool> FrozenFolders = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>From now on in this run, nothing writes settings files in <paramref name="folder"/>.</summary>
    public static void Freeze(string folder)
    {
        if (FrozenFolders.TryAdd(Normalize(folder), true))
            Log.Instance.Trace($"Settings writes frozen until restart. [folder={folder}]");
    }

    /// <summary>Allows writes again, after a restore or reset that didn't go ahead (it failed before the restart).</summary>
    public static void Thaw(string folder)
    {
        if (FrozenFolders.TryRemove(Normalize(folder), out _))
            Log.Instance.Trace($"Settings writes allowed again. [folder={folder}]");
    }

    /// <summary>Whether the settings file at <paramref name="filePath"/> may not be written now.</summary>
    public static bool IsFrozen(string filePath) =>
        !FrozenFolders.IsEmpty && Path.GetDirectoryName(filePath) is { } folder && FrozenFolders.ContainsKey(Normalize(folder));

    private static string Normalize(string folder) => Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
