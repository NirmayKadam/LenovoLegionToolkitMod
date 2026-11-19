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

    [Theory]
    [InlineData("18.52, 73.86", 18.52, 73.86)]
    [InlineData(" -33.87 ,151.21 ", -33.87, 151.21)]
    [InlineData("0,0", 0, 0)]
    public void ATypedLocation_IsRead(string text, double lat, double lon)
    {
        Assert.True(SunriseSunset.TryParseLocation(text, out var latitude, out var longitude));
        Assert.Equal((lat, lon), (latitude, longitude));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Pune")]
    [InlineData("18,52, 73,86")]      // decimal commas
    [InlineData("18.52")]
    [InlineData("95, 10")]            // no such latitude
    [InlineData("18.52, 73.86, 5")]
    public void AnythingElse_IsRejected(string? text) =>
        Assert.False(SunriseSunset.TryParseLocation(text, out _, out _));
}
