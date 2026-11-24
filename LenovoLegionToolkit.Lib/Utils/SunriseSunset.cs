using System;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using CoordinateSharp;
using LenovoLegionToolkit.Lib.Settings;

namespace LenovoLegionToolkit.Lib.Utils;

public class SunriseSunset
{
    /// <summary>How long a failed attempt (no location, or no sunrise there) is remembered before trying again.</summary>
    internal static readonly TimeSpan RetryAfterFailure = TimeSpan.FromHours(1);

    private readonly SunriseSunsetSettings _settings;
    private readonly Func<CancellationToken, Task<Coordinate?>> _locate;
    private readonly Func<DateTime> _utcNow;
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private DateTime? _lastFailureUtc;

    public SunriseSunset(SunriseSunsetSettings settings, HttpClientFactory httpClientFactory)
        : this(settings, token => GetGeoLocationAsync(httpClientFactory, token), () => DateTime.UtcNow) { }

    /// <summary>For tests: the IP-address lookup and the clock are supplied by the caller (no network).</summary>
    internal SunriseSunset(SunriseSunsetSettings settings, Func<CancellationToken, Task<Coordinate?>> locate, Func<DateTime> utcNow)
    {
        _settings = settings;
        _locate = locate;
        _utcNow = utcNow;
    }

    /// <summary>
    /// Today's sunrise and sunset (UTC), worked out once a day. Time triggers ask every second, so a failed attempt
    /// (offline, the lookup service refusing, or a place with no sunrise today) is remembered for an hour; before, each
    /// second sent another request, or wrote the settings file again.
    /// </summary>
    public async Task<(Time?, Time?)> GetSunriseSunsetAsync(CancellationToken token = default)
    {
        if (Cached() is { } cached)
            return cached;

        await _oneAtATime.WaitAsync(token).ConfigureAwait(false);
        try
        {
            // Another caller may have finished the work while this one waited.
            if (Cached() is { } done)
                return done;

            // A location typed in Settings is used as it is; only without one is the location looked up by IP address.
            var coordinate = _settings.Store is { Latitude: { } lat, Longitude: { } lon } && IsValidCoordinate(lat, lon)
                ? new Coordinate(lat, lon, DateTime.Today)
                : await _locate(token).ConfigureAwait(false);

            var (sunrise, sunset) = coordinate is null ? (null, null) : CalculateSunriseSunset(coordinate);
            if (sunrise is null || sunset is null)
            {
                _lastFailureUtc = _utcNow();
                Log.Instance.Trace($"No sunrise and sunset for today; trying again in {RetryAfterFailure.TotalMinutes:0} minutes.");
                return (null, null);
            }

            _lastFailureUtc = null;
            _settings.Store.LastCheckDateTime = DateTime.Today;
            _settings.Store.Sunrise = sunrise;
            _settings.Store.Sunset = sunset;
            _settings.SynchronizeStore();

            return (sunrise, sunset);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    /// <summary>Today's saved times, or "none" while a recent failure is remembered; null when it's time to work them out.</summary>
    private (Time?, Time?)? Cached()
    {
        var (sunrise, sunset) = (_settings.Store.Sunrise, _settings.Store.Sunset);
        if (_settings.Store.LastCheckDateTime == DateTime.Today && sunrise is not null && sunset is not null)
            return (sunrise, sunset);

        if (_lastFailureUtc is { } failed && _utcNow() - failed < RetryAfterFailure)
            return (null, null);

        return null;
    }

    private static async Task<Coordinate?> GetGeoLocationAsync(HttpClientFactory httpClientFactory, CancellationToken token)
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
