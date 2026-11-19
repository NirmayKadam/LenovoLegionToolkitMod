using System;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using CoordinateSharp;
using LenovoLegionToolkit.Lib.Settings;

namespace LenovoLegionToolkit.Lib.Utils;

public class SunriseSunset(SunriseSunsetSettings settings, HttpClientFactory httpClientFactory)
{
    public async Task<(Time?, Time?)> GetSunriseSunsetAsync(CancellationToken token = default)
    {
        var (sunrise, sunset) = (settings.Store.Sunrise, settings.Store.Sunset);
        if (settings.Store.LastCheckDateTime == DateTime.Today && sunrise is not null && sunset is not null)
            return (sunrise, sunset);

        // A location typed in Settings is used as it is; only without one is the location looked up by IP address.
        var coordinate = settings.Store is { Latitude: { } lat, Longitude: { } lon } && IsValidCoordinate(lat, lon)
            ? new Coordinate(lat, lon, DateTime.Today)
            : await GetGeoLocationAsync(token).ConfigureAwait(false);

        if (coordinate is null)
            return (null, null);

        (sunrise, sunset) = CalculateSunriseSunset(coordinate);

        settings.Store.LastCheckDateTime = DateTime.Today;
        settings.Store.Sunrise = sunrise;
        settings.Store.Sunset = sunset;
        settings.SynchronizeStore();

        return (sunrise, sunset);
    }

    private async Task<Coordinate?> GetGeoLocationAsync(CancellationToken token)
    {
        try
        {
            using var httpClient = httpClientFactory.Create();
            var responseJson = await httpClient.GetStringAsync("http://ip-api.com/json?fields=lat,lon", token).ConfigureAwait(false);
            var responseJsonNode = JsonNode.Parse(responseJson);
            // The answer comes over plain HTTP (the service has no free HTTPS), so only real coordinates are used.
            if (responseJsonNode is not null
                && double.TryParse(responseJsonNode["lat"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                && double.TryParse(responseJsonNode["lon"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)
                && IsValidCoordinate(lat, lon))
                return new Coordinate(lat, lon, DateTime.Today);

            Log.Instance.Trace($"Geolocation answer had no valid coordinates.");
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to get geolocation.", ex);
        }

        return null;
    }

    /// <summary>"18.52, 73.86" (latitude, longitude; a dot as decimal point). False for anything else or impossible values.</summary>
    public static bool TryParseLocation(string? text, out double latitude, out double longitude)
    {
        latitude = longitude = 0;
        var parts = text?.Split(',', StringSplitOptions.TrimEntries);
        return parts is { Length: 2 }
               && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out latitude)
               && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out longitude)
               && IsValidCoordinate(latitude, longitude);
    }

    internal static bool IsValidCoordinate(double lat, double lon) =>
        double.IsFinite(lat) && double.IsFinite(lon) && lat is >= -90 and <= 90 && lon is >= -180 and <= 180;

    private static (Time?, Time?) CalculateSunriseSunset(Coordinate coordinate)
    {
        var sunrise = coordinate.CelestialInfo.SunRise;
        var sunset = coordinate.CelestialInfo.SunSet;

        if (sunrise is null || sunset is null)
            return (null, null);

        return (new Time(sunrise.Value.Hour, sunrise.Value.Minute, sunrise.Value.Second), new Time(sunset.Value.Hour, sunset.Value.Minute, sunset.Value.Second));
    }
}
