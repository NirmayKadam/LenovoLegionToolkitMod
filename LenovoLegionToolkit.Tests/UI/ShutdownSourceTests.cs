using System;
using System.IO;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

/// <summary>
/// Quitting Indra leaves the laptop's power limits as they are. Shutdown used to send the Balance defaults whatever
/// the mode was, which replaced Performance or Custom limits on older Legions and Custom mode's GPU power target on
/// other models. The shutdown code can't run outside the app, so its source is checked.
/// </summary>
public class ShutdownSourceTests
{
    [Fact]
    public void Shutdown_SendsNothingToTheFirmware()
    {
        var root = HelpContentTests.FindRepositoryRoot();
        var app = File.ReadAllText(Path.Combine(root, "LenovoLegionToolkit.WPF", "App.xaml.cs"));

        var start = app.IndexOf("private async Task ShutdownCoreAsync()", StringComparison.Ordinal);
        Assert.True(start >= 0, "ShutdownCoreAsync not found in App.xaml.cs");

        var end = app.IndexOf("private static async Task SafeExecuteAsync<T>", start, StringComparison.Ordinal);
        Assert.True(end > start, "The end of ShutdownCoreAsync not found in App.xaml.cs");

        var shutdown = app[start..end];
        // The controller in any form (resolved, a field, the firmware seam) and the calls that send limits.
        foreach (var sendsLimits in new[] { "GodModeController", "IGodModeFirmware", "RestoreDefaultsInOtherPowerModeAsync", "SetFeatureValueAsync", "ApplyStateAsync" })
            Assert.DoesNotContain(sendsLimits, shutdown);
    }
}
