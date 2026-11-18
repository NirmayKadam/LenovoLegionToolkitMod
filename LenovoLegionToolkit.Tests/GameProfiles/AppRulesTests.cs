using System;
using System.Collections.Generic;
using System.IO;
using LenovoLegionToolkit.Lib.GameProfiles;
using Xunit;

namespace LenovoLegionToolkit.Tests.GameProfiles;

public class AppRulesTests
{
    private static readonly string Windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    [Theory]
    [InlineData("explorer")]
    [InlineData("EXPLORER")]
    [InlineData("StartMenuExperienceHost")]
    [InlineData("Lenovo Legion Toolkit")]
    public void WindowsShellAndLlt_AreProtected(string name) => Assert.True(AppRules.IsProtectedName(name));

    [Fact]
    public void Paths_InTheWindowsFolder_AreRefused_InAnyCase()
    {
        Assert.False(AppRules.IsAllowedPath(Path.Combine(Windows, "explorer.exe")));
        Assert.False(AppRules.IsAllowedPath(Path.Combine(Windows.ToUpperInvariant(), "SystemApps", "x", "StartMenuExperienceHost.exe")));
        Assert.True(AppRules.IsAllowedPath(@"C:\Users\me\AppData\Local\Programs\comet\comet.exe"));
        Assert.True(AppRules.IsAllowedPath(Windows + "Apps\\tool.exe"));   // a folder that only starts with the same letters
        Assert.False(AppRules.IsAllowedPath(null));
        Assert.False(AppRules.IsAllowedPath("  "));
    }

    /// <summary>
    /// The picker drops protected names and the excluded ones (profiled games, apps already listed) before asking for
    /// any path: the path lookup is supplied by the test and records every name it is asked about, and the game's
    /// name must never appear there (looking up its path would open the game's process).
    /// </summary>
    [Fact]
    public void FilterApps_DropsNamesBeforeLookingUpPaths_ThenWindowsFolderApps()
    {
        var asked = new List<string>();
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["comet"] = @"C:\Users\me\comet.exe",
            ["claude"] = @"C:\Users\me\claude.exe",
            ["notepad"] = Path.Combine(Windows, "notepad.exe"),
            ["Nahimic3"] = @"C:\Program Files\Nahimic\Nahimic3.exe",
        };

        var result = AppRules.FilterApps(
            ["VALORANT-Win64-Shipping", "explorer", "comet", "claude", "claude", "notepad", "Nahimic3"],
            ["valorant-win64-shipping", "Nahimic3"],
            name =>
            {
                asked.Add(name);
                return paths.GetValueOrDefault(name);
            });

        Assert.Equal(["claude", "comet"], result);
        Assert.DoesNotContain("VALORANT-Win64-Shipping", asked);
        Assert.DoesNotContain("explorer", asked);
        Assert.DoesNotContain("Nahimic3", asked);
    }
}
