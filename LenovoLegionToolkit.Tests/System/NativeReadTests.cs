using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using LenovoLegionToolkit.Lib.Extensions;
using Xunit;

namespace LenovoLegionToolkit.Tests.System;

/// <summary>Native reads that hand back memory Windows allocated, or a size Windows reported.</summary>
public class NativeReadTests
{
    [Fact]
    public void TheActivePowerPlan_IsTheOneWindowsReports()
    {
        // Real Windows: compare with powercfg's own answer.
        using var powercfg = Process.Start(new ProcessStartInfo("powercfg", "/getactivescheme")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        var output = powercfg.StandardOutput.ReadToEnd();
        Assert.True(powercfg.WaitForExit(30_000), "powercfg didn't finish");

        var expected = Guid.Parse(Regex.Match(output, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}").Value);

        Assert.Equal(expected, PowerPlanExtensions.GetActiveSchemeGuid());
    }

    [Theory]
    [InlineData(0u)]   // Windows didn't answer the size question
    [InlineData(4u)]   // too small for the structure written into it
    [InlineData(1_000_000u)]
    public void AnUnusableDeviceDetailSize_IsRefused(uint reported) =>
        Assert.Throws<InvalidOperationException>(() => LenovoLegionToolkit.Lib.System.Devices.InterfaceDetailBufferSize(reported));

    [Fact]
    public void AUsableDeviceDetailSize_IsUsed() =>
        Assert.Equal(160, LenovoLegionToolkit.Lib.System.Devices.InterfaceDetailBufferSize(160));
}
