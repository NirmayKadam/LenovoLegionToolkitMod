using System;
using System.Collections.Generic;
using System.Linq;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.WPF;
using Xunit;

namespace LenovoLegionToolkit.Tests.Controllers;

/// <summary>
/// Pieces taken over from Lenovo Legion Toolkit 2.38 (Round 27): which fan table layout the sensors use, the GPU
/// offset ranges offered, and the charge threshold card in the default dashboard. No hardware: only the decisions.
/// </summary>
public class UpstreamSyncControllerTests
{
    // ---- Sensor profile: chosen from the sensor and fan IDs the firmware lists (sensorId, fanId) ----

    [Fact]
    public void SensorProfile_FirstLayout_NeedsOneOfItsSystemFans()
    {
        var index = SensorsController.SelectFanTableProfileIndex([(4, 1), (5, 2), (1, 4)], out var systemFans);

        Assert.Equal(0, index);
        Assert.Equal([0], systemFans);
    }

    [Fact]
    public void SensorProfile_BothSystemFans_AreKept()
    {
        var index = SensorsController.SelectFanTableProfileIndex([(4, 1), (5, 2), (1, 4), (5, 5)], out var systemFans);

        Assert.Equal(0, index);
        Assert.Equal([0, 1], systemFans);
    }

    [Fact]
    public void SensorProfile_WithoutSystemFans_FallsToTheLayoutThatHasNone()
    {
        // Layout 0 has the same CPU and GPU entries but needs a system fan; layout 3 doesn't.
        var index = SensorsController.SelectFanTableProfileIndex([(4, 1), (5, 2)], out var systemFans);

        Assert.Equal(3, index);
        Assert.Empty(systemFans);
    }

    [Theory]
    [InlineData(1, new[] { 1, 1, 5, 2, 4, 4 })]   // CPU on sensor 1, one system fan on sensor 4
    [InlineData(2, new[] { 1, 1, 5, 2, 4, 1 })]   // CPU on sensor 1, with the required sensor 4 entry
    [InlineData(4, new[] { 3, 0, 4, 1 })]         // fan method readings
    [InlineData(5, new[] { 0, 0, 0, 1 })]         // default table
    public void SensorProfile_IsPickedByWhatTheFirmwareLists(int expected, int[] pairs)
    {
        var data = Enumerable.Range(0, pairs.Length / 2).Select(i => (pairs[2 * i], pairs[2 * i + 1]));

        Assert.Equal(expected, SensorsController.SelectFanTableProfileIndex(data, out _));
    }

    [Fact]
    public void SensorProfile_UnknownLayout_IsNone()
    {
        Assert.Equal(-1, SensorsController.SelectFanTableProfileIndex([(1, 1), (5, 2)], out var a));
        Assert.Empty(a);
        Assert.Equal(-1, SensorsController.SelectFanTableProfileIndex([], out var b));
        Assert.Empty(b);
    }

    // ---- GPU overclock ranges: the driver's range, never wider than Indra's fixed limits ----

    [Theory]
    [InlineData(-1000, 1000, -500, 500)]   // driver allows more: the fixed limits win
    [InlineData(-200, 300, -200, 300)]     // driver allows less: the driver wins
    [InlineData(-900, 100, -500, 100)]     // partly
    public void GpuRange_IsNarrowedToBoth(int driverMin, int driverMax, int expectedMin, int expectedMax)
    {
        Assert.Equal((expectedMin, expectedMax), GPUOverclockController.Narrow((driverMin, driverMax), -500, 500));
    }

    [Fact]
    public void GpuRange_NotOverlapping_FallsBackToTheFixedLimits()
    {
        Assert.Equal((-500, 500), GPUOverclockController.Narrow((600, 900), -500, 500));
    }

    // ---- Charge threshold: only sensible values reach Lenovo's power management driver ----

    [Theory]
    [InlineData(true, 40, 80, true)]
    [InlineData(true, 2, 99, true)]
    [InlineData(true, 80, 80, false)]   // Start must be below Stop when on
    [InlineData(true, 90, 60, false)]
    [InlineData(true, 1, 80, false)]    // below 2 %
    [InlineData(true, 40, 100, false)]  // above 99 %
    [InlineData(false, 2, 2, true)]     // off: the stored values only need to be in range
    [InlineData(false, 0, 50, false)]
    public void ChargeThreshold_IsValid(bool enabled, int start, int stop, bool expected)
    {
        Assert.Equal(expected, LenovoLegionToolkit.Lib.Features.ChargeThresholdFeature.IsValid(new LenovoLegionToolkit.Lib.ChargeThreshold(enabled, start, stop)));
    }

    // ---- Charge threshold card in the default dashboard ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChargeThreshold_IsInTheDefaultPowerGroup_OnlyWhereSupported(bool supported)
    {
        var power = DashboardGroup.GetDefaultGroups(false, supported)[0];

        Assert.Equal(supported, power.Items.Contains(DashboardItem.ChargeThreshold));
        if (supported)
            Assert.Equal(DashboardItem.ChargeThreshold, power.Items[3]);
    }

    [Fact]
    public void ChargeThreshold_WithItsMode_KeepsItsModeFirst()
    {
        var power = DashboardGroup.GetDefaultGroups(true, true)[0];

        Assert.Equal(DashboardItem.ItsMode, power.Items[0]);
        Assert.Single(power.Items, DashboardItem.ChargeThreshold);
    }
}
