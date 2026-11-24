using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Tests.UI;
using LenovoLegionToolkit.WPF.Windows.Settings;
using Xunit;

namespace LenovoLegionToolkit.Tests.Settings;

/// <summary>
/// Every settings file (each settings class found by reflection, its file name read from its source) can be included in a settings backup,
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

        // The last game sessions' summaries. History, not a setting.
        ["game_sessions.json"] = "history, not a setting",
    };

    /// <summary>Every settings class (each subclass of AbstractSettings) in the library, automation, macro and app assemblies.</summary>
    private static Type[] SettingsClasses() =>
    [
        .. new[]
            {
                typeof(LenovoLegionToolkit.Lib.Settings.ApplicationSettings).Assembly,
                typeof(LenovoLegionToolkit.Lib.Automation.Utils.AutomationSettings).Assembly,
                typeof(LenovoLegionToolkit.Lib.Macro.Utils.MacroSettings).Assembly,
                typeof(SettingsBackupWindow).Assembly,
            }
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false } && IsSettings(type)),
    ];

    private static bool IsSettings(Type type)
    {
        for (var t = type.BaseType; t is not null; t = t.BaseType)
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(AbstractSettings<>))
                return true;
        return false;
    }

    private static readonly Regex JsonName = new("\"([A-Za-z0-9_.-]+\\.json)\"");

    /// <summary>The file name each settings class saves to, read from that class's own source.</summary>
    private static Dictionary<string, string[]> FileNamesByClass()
    {
        var root = HelpContentTests.FindRepositoryRoot();
        var sources = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}LenovoLegionToolkit.Tests{Path.DirectorySeparatorChar}"))
            .Select(f => File.ReadAllText(f))
            .ToList();

        return SettingsClasses().ToDictionary(
            type => type.FullName!,
            type => sources
                .Where(source => Regex.IsMatch(source, $@"\bclass\s+{type.Name}\b"))
                .SelectMany(source => JsonName.Matches(source).Select(m => m.Groups[1].Value))
                .Distinct()
                .ToArray());
    }

    private static IEnumerable<string> SettingsFileNames() => FileNamesByClass().Values.SelectMany(names => names).Distinct();

    [Fact]
    public void EverySettingsClass_InEveryAssembly_IsFound_WithItsFileName()
    {
        var classes = FileNamesByClass();

        Assert.Contains(typeof(LenovoLegionToolkit.Lib.Automation.Utils.AutomationSettings).FullName!, classes.Keys);
        Assert.Contains(typeof(LenovoLegionToolkit.Lib.Macro.Utils.MacroSettings).FullName!, classes.Keys);
        Assert.Empty(classes.Where(c => c.Value.Length == 0).Select(c => c.Key));   // each one's file name was read
    }

    [Fact]
    public void TheSourcesNameSettingsFiles()
    {
        Assert.Contains("settings.json", SettingsFileNames());   // the scan itself works
        Assert.Contains("automation.json", SettingsFileNames());
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
