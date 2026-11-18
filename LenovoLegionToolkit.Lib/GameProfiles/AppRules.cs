using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LenovoLegionToolkit.Lib.GameProfiles;

/// <summary>
/// Which apps a game profile may ease or close. Windows' own shell and system processes are never touched, by name
/// and whatever their path, and neither is anything in the Windows folder. LLT itself is never touched either.
/// </summary>
public static class AppRules
{
    public const string LltProcessName = "Lenovo Legion Toolkit";

    public static readonly FrozenSet<string> Protected = new[]
    {
        "explorer", "StartMenuExperienceHost", "ShellExperienceHost", "SearchHost", "SearchApp", "TextInputHost",
        "ApplicationFrameHost", "SystemSettings", "LockApp", "dwm", "csrss", "winlogon", "wininit", "services", "lsass",
        "smss", "sihost", "ctfmon", "taskhostw", "RuntimeBroker", "svchost", "fontdrvhost", "conhost", "dllhost",
        LltProcessName,
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly string WindowsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    public static bool IsProtectedName(string exeName) => Protected.Contains(exeName);

    /// <summary>False for anything in the Windows folder, and for an unknown or malformed path.</summary>
    public static bool IsAllowedPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            var windows = WindowsFolder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return !Path.GetFullPath(path).StartsWith(windows, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// The apps the Game profiles window may offer: protected names and the names in <paramref name="excludeNames"/>
    /// (profiled games, apps already listed) are dropped first, so <paramref name="pathOf"/> is only asked about the
    /// rest; then apps in the Windows folder are dropped. Names come back once each, sorted.
    /// </summary>
    public static IReadOnlyList<string> FilterApps(IEnumerable<string> names, IEnumerable<string> excludeNames, Func<string, string?> pathOf)
    {
        var excluded = new HashSet<string>(excludeNames, StringComparer.OrdinalIgnoreCase);
        return names
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(n => !IsProtectedName(n) && !excluded.Contains(n))
            .Where(n => IsAllowedPath(pathOf(n)))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
