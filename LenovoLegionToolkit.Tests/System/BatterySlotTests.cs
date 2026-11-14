using LenovoLegionToolkit.Lib.System;
using Xunit;

namespace LenovoLegionToolkit.Tests.System;

// Which of Lenovo's three battery-data driver slots to ask, in order. The driver itself is on the laptop.
public class BatterySlotTests
{
    [Fact]
    public void NothingRemembered_TriesAllThreeInOrder() =>
        Assert.Equal([0u, 1u, 2u], Battery.BatterySlotsToTry(null));

    [Theory]
    [InlineData(1u, new uint[] { 1, 0, 2 })]
    [InlineData(2u, new uint[] { 2, 0, 1 })]
    [InlineData(0u, new uint[] { 0, 1, 2 })]
    public void RememberedSlot_IsTriedFirst_ThenTheRest(uint remembered, uint[] expected) =>
        Assert.Equal(expected, Battery.BatterySlotsToTry(remembered));

    [Fact]
    public void ARememberedSlotOutOfRange_IsIgnored() =>
        Assert.Equal([0u, 1u, 2u], Battery.BatterySlotsToTry(7));
}
