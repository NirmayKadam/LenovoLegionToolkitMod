using System;
using LenovoLegionToolkit.Lib.Utils;
using NvAPIWrapper.Native.GPU;
using Xunit;

namespace LenovoLegionToolkit.Tests.Utils;

// How NVIDIA's "performance decrease" reasons are shown on the OSD.
public class GpuLimitTests
{
    [Theory]
    [InlineData(PerformanceDecreaseReason.None, GpuLimit.None)]
    [InlineData(PerformanceDecreaseReason.ThermalProtection, GpuLimit.Thermal)]
    [InlineData(PerformanceDecreaseReason.PowerControl, GpuLimit.Power)]
    [InlineData(PerformanceDecreaseReason.InsufficientPower, GpuLimit.Power)]
    [InlineData(PerformanceDecreaseReason.AC_BATT, GpuLimit.Battery)]
    [InlineData(PerformanceDecreaseReason.ApiTriggered, GpuLimit.App)]
    [InlineData(PerformanceDecreaseReason.Unknown, GpuLimit.Unknown)]
    public void EachReason_HasItsLabel(PerformanceDecreaseReason reason, GpuLimit expected) =>
        Assert.Equal(expected, GpuLimitReading.From(reason));

    [Fact]
    public void WhenSeveralApply_ThermalComesFirst_ThenPower_ThenBattery()
    {
        Assert.Equal(GpuLimit.Thermal, GpuLimitReading.From(PerformanceDecreaseReason.ThermalProtection | PerformanceDecreaseReason.PowerControl | PerformanceDecreaseReason.AC_BATT));
        Assert.Equal(GpuLimit.Power, GpuLimitReading.From(PerformanceDecreaseReason.PowerControl | PerformanceDecreaseReason.AC_BATT | PerformanceDecreaseReason.ApiTriggered));
        Assert.Equal(GpuLimit.Battery, GpuLimitReading.From(PerformanceDecreaseReason.AC_BATT | PerformanceDecreaseReason.ApiTriggered));
    }

    [Fact]
    public void EveryLimit_HasText() =>
        Assert.All(Enum.GetValues<GpuLimit>(), limit => Assert.False(string.IsNullOrWhiteSpace(GpuLimitReading.Label(limit))));
}
