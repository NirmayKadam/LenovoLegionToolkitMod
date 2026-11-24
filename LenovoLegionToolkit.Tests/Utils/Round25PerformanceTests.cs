using System;
using System.Collections.Generic;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.WPF.Utils;
using Xunit;

namespace LenovoLegionToolkit.Tests.Utils;

/// <summary>The performance limits: each decision checked with given times and settings.</summary>
public class Round25PerformanceTests
{
    [Fact]
    public void FpsNumbers_ArePassedOn_AtMostEvery100Ms()
    {
        const long last = 10_000;

        Assert.False(FpsSensorController.ShouldPublish(last, last + 7));     // the next frame at 144 FPS
        Assert.False(FpsSensorController.ShouldPublish(last, last + 99));
        Assert.True(FpsSensorController.ShouldPublish(last, last + 100));
    }

    [Fact]
    public void DriveLetters_AreReadAgainOnAMiss_AtMostEvery30Seconds()
    {
        const long last = 1_000_000;

        Assert.False(ProcessImagePath.ShouldRereadDrives(last, last + 1));
        Assert.False(ProcessImagePath.ShouldRereadDrives(last, last + 29_999));
        Assert.True(ProcessImagePath.ShouldRereadDrives(last, last + 30_000));
    }

    [Fact]
    public void SensorReads_AreNeverFasterThanTheUpdateGate()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(100), SensorsGroupController.ClampInterval(TimeSpan.Zero));   // a hand-edited 0
        Assert.Equal(TimeSpan.FromMilliseconds(100), SensorsGroupController.ClampInterval(TimeSpan.FromMilliseconds(20)));
        Assert.Equal(TimeSpan.FromSeconds(2), SensorsGroupController.ClampInterval(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void ASnapshotWithSeveralSets_IsTheSameAsOneSetAfterTheOther()
    {
        var cpu = new Dictionary<SensorItem, float> { [SensorItem.CpuTemperature] = 71, [SensorItem.CpuPower] = 45 };
        var gpu = new Dictionary<SensorItem, float> { [SensorItem.GpuPower] = 66, [SensorItem.CpuPower] = 46 };   // a later set wins

        var once = new HardwareSensorSnapshot().WithValues(cpu, gpu);
        var stepByStep = new HardwareSensorSnapshot().WithValues(cpu).WithValues(gpu);

        foreach (var item in Enum.GetValues<SensorItem>())
            Assert.Equal(stepByStep[item], once[item]);
        Assert.Equal(46, once[SensorItem.CpuPower]);
        Assert.Equal(-1, once[SensorItem.GpuFrequency]);   // never read: still "missing"
    }

    [Theory]
    [InlineData(true, true, 2.0, 1.0, 2.0)]     // new sensor dashboard on: its interval
    [InlineData(true, false, 2.0, 1.0, 1.0)]    // classic dashboard
    [InlineData(false, true, 2.0, 1.0, 1.0)]    // the new one needs hardware sensors
    [InlineData(true, true, 0.0, 1.0, 0.5)]     // a hand-edited 0: never faster than half a second
    public void TheDashboardRefreshInterval_FollowsTheSensorsSectionShown(bool hardware, bool newDashboard, double newSeconds, double classicSeconds, double expected) =>
        Assert.Equal(TimeSpan.FromSeconds(expected), DashboardRefresh.Interval(hardware, newDashboard, newSeconds, classicSeconds));
}
