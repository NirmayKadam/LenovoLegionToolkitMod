using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

/// <summary>
/// A failure while detailed sensors start (for example PawnIO starting slowly after boot) used to switch the user's
/// "hardware sensors" and "new sensor dashboard" settings off for good. That code needs PawnIO and the hardware, so the
/// source is checked instead: across the library and the app, only the places that act on the user's own choice write
/// these two settings.
/// </summary>
public class SensorSettingsKeptTests
{
    /// <summary>An assignment to either setting in any spelling (<c>x.EnableHardwareSensors=false</c> too), not a comparison.</summary>
    private static readonly Regex Write = new(@"\b(EnableHardwareSensors|UseNewSensorDashboard)\s*=(?!=)");

    /// <summary>The only files allowed to write them, and why.</summary>
    private static readonly string[] Allowed =
    [
        "HardwareSensorsFeature.cs",          // the hardware-sensors setting itself, set by the user or their automation
        "SettingsAppBehaviorControl.xaml.cs", // the two switches in Settings
        "PawnIOHelper.cs",                    // the user answering "No" to installing PawnIO
    ];

    [Fact]
    public void TheCheck_FindsEverySpelling_ButNotComparisons()
    {
        Assert.Matches(Write, "settings.Store.EnableHardwareSensors = false;");
        Assert.Matches(Write, "s.UseNewSensorDashboard=false;");
        Assert.DoesNotMatch(Write, "if (s.EnableHardwareSensors == false)");
        Assert.DoesNotMatch(Write, "public bool EnableHardwareSensors { get; set; } = true;");
    }

    [Fact]
    public void OnlyTheUsersOwnChoices_WriteTheSensorSettings()
    {
        var root = HelpContentTests.FindRepositoryRoot();
        var writers = new[] { "LenovoLegionToolkit.Lib", "LenovoLegionToolkit.WPF" }
            .SelectMany(project => Directory.EnumerateFiles(Path.Combine(root, project), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => Write.IsMatch(File.ReadAllText(f)))
            .Select(Path.GetFileName)
            .Except(Allowed)
            .ToList();

        Assert.Empty(writers);
    }

    [Fact]
    public void TheSensorSetUp_SavesNoSettingsAtAll()
    {
        var source = File.ReadAllText(Path.Combine(HelpContentTests.FindRepositoryRoot(),
            "LenovoLegionToolkit.Lib", "Controllers", "Sensors", "SensorsGroupController.cs"));

        Assert.DoesNotContain("SynchronizeStore(", source);
    }
}
