using LenovoLegionToolkit.Lib.Utils;
using Xunit;

namespace LenovoLegionToolkit.Tests.Utils;

// The location answer comes over plain HTTP, so only real coordinates are used.
public class SunriseSunsetTests
{
    [Theory]
    [InlineData(18.52, 73.86)]
    [InlineData(-90, -180)]
    [InlineData(90, 180)]
    [InlineData(0, 0)]
    public void RealCoordinates_AreUsed(double lat, double lon) =>
        Assert.True(SunriseSunset.IsValidCoordinate(lat, lon));

    [Theory]
    [InlineData(90.1, 0)]
    [InlineData(-91, 0)]
    [InlineData(0, 180.5)]
    [InlineData(0, -999)]
    [InlineData(double.NaN, 0)]
    [InlineData(0, double.PositiveInfinity)]
    public void ImpossibleCoordinates_AreRejected(double lat, double lon) =>
        Assert.False(SunriseSunset.IsValidCoordinate(lat, lon));
}
