using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CoordinateSharp;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using Xunit;

namespace LenovoLegionToolkit.Tests.Utils;

/// <summary>
/// How often sunrise and sunset are worked out. Time triggers ask every second; offline, each second used to send
/// another request to the location service. The IP-address lookup is supplied by the test (no network: it counts calls
/// and answers as the real one would); the settings file is real, in a temporary folder.
/// </summary>
public sealed class SunriseSunsetLookupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "llt-sunrise-" + Guid.NewGuid().ToString("N"));
    private DateTime _now = new(2026, 11, 25, 6, 0, 0, DateTimeKind.Utc);
    private int _lookups;

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    private SunriseSunset Create(Func<Coordinate?> answer) =>
        new(new SunriseSunsetSettings(_dir), _ =>
        {
            Interlocked.Increment(ref _lookups);
            return Task.FromResult(answer());
        }, () => _now);

    [Fact]
    public async Task Offline_TheLookupIsTriedOnce_ThenNotAgainForAnHour()
    {
        var sun = Create(() => null);   // no answer from the location service

        for (var second = 0; second < 60; second++)
        {
            Assert.Equal((null, null), await sun.GetSunriseSunsetAsync());
            _now = _now.AddSeconds(1);
        }
        Assert.Equal(1, _lookups);

        _now = _now.Add(SunriseSunset.RetryAfterFailure);
        await sun.GetSunriseSunsetAsync();
        Assert.Equal(2, _lookups);
    }

    [Fact]
    public async Task AFoundLocation_IsWorkedOutOncePerDay_AndSaved()
    {
        var sun = Create(() => new Coordinate(18.52, 73.86, DateTime.Today));   // Pune

        var (sunrise, sunset) = await sun.GetSunriseSunsetAsync();
        await sun.GetSunriseSunsetAsync();

        Assert.NotNull(sunrise);
        Assert.NotNull(sunset);
        Assert.Equal(1, _lookups);
        var saved = new SunriseSunsetSettings(_dir).Store;
        Assert.Equal(sunrise, saved.Sunrise);
        Assert.Equal(DateTime.Today, saved.LastCheckDateTime);
    }

    [Fact]
    public async Task ManyCallersAtOnce_ShareOneLookup()
    {
        var sun = Create(() => null);

        await Task.WhenAll(Task.Run(() => sun.GetSunriseSunsetAsync()), Task.Run(() => sun.GetSunriseSunsetAsync()), Task.Run(() => sun.GetSunriseSunsetAsync()));

        Assert.Equal(1, _lookups);
    }

    [SkippableFact]
    public async Task APlaceWithNoSunriseToday_IsNotWrittenEverySecond()
    {
        // A typed location in the polar night: no sunrise today. Before, this wrote the settings file every second.
        var settings = new SunriseSunsetSettings(_dir);
        settings.Store.Latitude = 78.22;    // Longyearbyen, Svalbard
        settings.Store.Longitude = 15.65;
        settings.SynchronizeStore();
        var sun = new SunriseSunset(settings, _ => throw new InvalidOperationException("a typed location needs no lookup"), () => _now);

        var celestial = new Coordinate(78.22, 15.65, DateTime.Today).CelestialInfo;
        Skip.If(celestial.SunRise is not null && celestial.SunSet is not null,
            "Svalbard has a sunrise and sunset today (spring or autumn), so this case can't be set up on this date.");

        var file = Path.Combine(_dir, "sunrise_sunset.json");
        var before = File.GetLastWriteTimeUtc(file);
        await Task.Delay(30);
        for (var i = 0; i < 5; i++)
            Assert.Equal((null, null), await sun.GetSunriseSunsetAsync());

        Assert.Equal(before, File.GetLastWriteTimeUtc(file));
    }
}
