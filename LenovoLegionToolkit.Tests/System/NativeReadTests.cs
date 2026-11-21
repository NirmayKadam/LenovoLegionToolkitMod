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

    [Fact]
    public void ReadingTheActivePowerPlanOften_DoesNotGrowMemory()
    {
        // The power listener reads it every 500 ms; each read used to leave Windows' copy of the GUID behind.
        for (var i = 0; i < 1_000; i++)
            _ = PowerPlanExtensions.GetActiveSchemeGuid();

        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var before = process.PrivateMemorySize64;

        for (var i = 0; i < 200_000; i++)
            _ = PowerPlanExtensions.GetActiveSchemeGuid();

        process.Refresh();
        var grown = process.PrivateMemorySize64 - before;

        // A leak of one 16-byte allocation per read would be at least 3 MB here; allow for the runtime's own noise.
        Assert.True(grown < 2 * 1024 * 1024, $"private memory grew by {grown / 1024} KB");
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
