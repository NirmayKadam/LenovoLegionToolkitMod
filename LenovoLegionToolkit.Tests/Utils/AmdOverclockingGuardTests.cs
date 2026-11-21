using LenovoLegionToolkit.Lib.Overclocking.Amd;
using Xunit;

namespace LenovoLegionToolkit.Tests.Utils;

/// <summary>
/// AMD overclocking values from a saved profile are checked before they reach the CPU's power controller. Not run on
/// AMD hardware here (the development laptop is Intel); these check the guard itself.
/// </summary>
public class AmdOverclockingGuardTests
{
    [Theory]
    [InlineData((short)35, 35u)]
    [InlineData((short)1, 1u)]
    public void APositiveLimit_IsSentAsItIs(short stored, uint sent) =>
        Assert.Equal(sent, AmdOverclockingController.UsableLimit("test", stored));

    [Theory]
    [InlineData((short)0)]
    [InlineData((short)-1)]      // used to be sent as 4,294,967,295
    [InlineData(short.MinValue)]
    public void AZeroOrNegativeLimit_IsNotSent(short stored) =>
        Assert.Null(AmdOverclockingController.UsableLimit("test", stored));

    [Fact]
    public void NoLimit_StaysNoLimit() =>
        Assert.Null(AmdOverclockingController.UsableLimit("test", null));

    [Theory]
    [InlineData(-50, true)]
    [InlineData(-30, true)]
    [InlineData(0, true)]
    [InlineData(50, true)]
    [InlineData(-51, false)]
    [InlineData(200, false)]
    [InlineData(double.NaN, false)]
    public void CurveOptimizerValues_OutsideAmdsWidestRange_AreNotSent(double value, bool usable) =>
        Assert.Equal(usable, AmdOverclockingController.IsUsableCurveOptimizerValue(value));
}
