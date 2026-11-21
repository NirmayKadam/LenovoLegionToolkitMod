using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Windows.Osd;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

/// <summary>
/// UI code stays on the UI thread. <c>ConfigureAwait(false)</c> in a window or control moves the rest of the method to a
/// background thread, where touching a control throws (and, in an event handler, can end the app). It is allowed only
/// where the code runs off the UI thread on purpose and hands results back through the dispatcher; each such place is
/// listed here with the reason.
/// </summary>
public class UiThreadRuleTests
{
    private static readonly Dictionary<string, (int Count, string Why)> AllowedOffTheUiThread = new()
    {
        [Path.Combine("Controls", "Dashboard", "UsageOverviewControl.xaml.cs")] = (5, "ReadLoopAsync runs on a pool thread and posts each reading to the UI"),
        [Path.Combine("Controls", "Dashboard", "SensorsControlV2.xaml.cs")] = (1, "inside Task.Run: the sensor read itself"),
        [Path.Combine("Windows", "MainWindow.xaml.cs")] = (4, "inside Task.Run: the conflicting-software checks, whose results arrive through events"),
        [Path.Combine("Windows", "Dashboard", "GodModeSettingsWindow.xaml.cs")] = (1, "IsBiosOcEnabledAsync only reads WMI and returns a bool"),
    };

    [Fact]
    public void UiCode_DoesNotLeaveTheUiThread_ExceptWhereListed()
    {
        var wpf = Path.Combine(HelpContentTests.FindRepositoryRoot(), "LenovoLegionToolkit.WPF");
        var found = new[] { "Controls", "Pages", "Windows" }
            .SelectMany(folder => Directory.GetFiles(Path.Combine(wpf, folder), "*.cs", SearchOption.AllDirectories))
            .Select(file => (Relative: Path.GetRelativePath(wpf, file), Count: File.ReadAllText(file).Split(".ConfigureAwait(false)").Length - 1))
            .Where(f => f.Count > 0)
            .ToDictionary(f => f.Relative, f => f.Count);

        var unexpected = found.Where(f => !AllowedOffTheUiThread.TryGetValue(f.Key, out var allowed) || allowed.Count != f.Value)
            .Select(f => $"{f.Key}: {f.Value}")
            .ToArray();

        Assert.Empty(unexpected);
    }

    [Fact]
    public void AnOsdColourThatCantBeRead_FallsBackToTheDefault_InsteadOfStoppingStartUp()
    {
        var defaults = new OsdSettings.OsdSettingsStore();

        var bad = OsdWindowBase.OsdBrush("#not-a-colour", defaults.ValueColor);
        var empty = OsdWindowBase.OsdBrush(null, defaults.CriticalColor);
        var good = OsdWindowBase.OsdBrush("#00FF00", defaults.ValueColor);

        Assert.Equal(Colors.White, ((SolidColorBrush)bad).Color);      // the default value colour, #FFFFFF
        Assert.Equal(Colors.Red, ((SolidColorBrush)empty).Color);      // the default critical colour, #FF0000
        Assert.Equal(Color.FromRgb(0, 0xFF, 0), ((SolidColorBrush)good).Color);
    }

    [Fact]
    public void AnAntiCheatChangeHandlerThatFails_DoesNotEscapeTheMonitor()
    {
        // The monitor's timer raises this on a pool thread, where an escaping exception ends the app.
        var monitor = new AntiCheatMonitor(name => name == "vgc");
        monitor.ProtectedGameChanged += (_, _) => throw new global::System.InvalidOperationException("handler failed");

        Assert.True(monitor.Poll());
    }
}
