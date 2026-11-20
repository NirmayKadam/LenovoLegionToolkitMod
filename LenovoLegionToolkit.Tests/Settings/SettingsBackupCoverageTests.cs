using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LenovoLegionToolkit.Tests.UI;
using LenovoLegionToolkit.WPF.Windows.Settings;
using Xunit;

namespace LenovoLegionToolkit.Tests.Settings;

/// <summary>
/// Every settings file (read from the real settings classes' sources) can be included in a settings backup,
/// unless it is left out on purpose for the reason given here.
/// </summary>
public class SettingsBackupCoverageTests
{
    private static readonly Dictionary<string, string> NotBackedUpOnPurpose = new()
    {
        // A game profile's note of what to put back after a game. Restoring an old one could put back
        // services or app priorities from another time.
        ["game_profile_session.json"] = "session note, not a setting",

        // The services LLT stopped for normal use. Restoring an old one could start or keep stopped services
        // from another time.
        ["background_services_session.json"] = "session note, not a setting",

        // When the Health check last ran and which problems were already notified. Restoring an old one would only
        // hide or repeat notifications; the check reads Windows afresh anyway.
        ["health.json"] = "check state, not a setting",
    };

    private static IEnumerable<string> SettingsFileNames()
    {
        var root = HelpContentTests.FindRepositoryRoot();
        var folders = new[]
        {
            Path.Combine(root, "LenovoLegionToolkit.Lib", "Settings"),
            Path.Combine(root, "LenovoLegionToolkit.WPF", "Settings"),
        };

        return folders
            .SelectMany(folder => Directory.GetFiles(folder, "*.cs"))
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), "\"([a-z_]+\\.json)\"").Select(m => m.Groups[1].Value))
            .Distinct();
    }

    [Fact]
    public void TheSourcesNameSettingsFiles()
    {
        Assert.Contains("settings.json", SettingsFileNames());   // the scan itself works
    }

    [Fact]
    public void EverySettingsFile_IsInTheBackup_OrLeftOutOnPurpose()
    {
        var missing = SettingsFileNames()
            .Where(name => !SettingsBackupWindow.KnownSettingsFiles.Contains(name) && !NotBackedUpOnPurpose.ContainsKey(name))
            .Order()
            .ToArray();

        Assert.Empty(missing);
    }

    [Fact]
    public void FilesLeftOutOnPurpose_AreReallyLeftOut()
    {
        Assert.All(NotBackedUpOnPurpose.Keys, name => Assert.DoesNotContain(name, SettingsBackupWindow.KnownSettingsFiles));
    }
}
