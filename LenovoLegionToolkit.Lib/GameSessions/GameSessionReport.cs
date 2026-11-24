using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LenovoLegionToolkit.Lib.GameSessions;

/// <summary>
/// One reading taken while a game runs. A value that couldn't be read is -1 (the sensors' own convention), and FPS is
/// -1 when FPS isn't being measured (the OSD isn't showing it).
/// </summary>
public sealed record GameSessionSample(
    DateTime Utc,
    int CpuTemperature,
    int GpuTemperature,
    int GpuClock,
    int GpuLoad,
    double GpuPower,
    int Fps,
    int LowFps)
{
    /// <summary>The GPU readings as one phrase ("GPU 1905 MHz, 66 °C, 98% load, 66 W"), or null when none were read.</summary>
    public string? DescribeGpu()
    {
        var parts = new List<string>();
        if (GpuClock > 0)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{GpuClock} MHz"));
        if (GpuTemperature > 0)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{GpuTemperature} °C"));
        if (GpuLoad >= 0)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{GpuLoad}% load"));
        if (GpuPower > 0)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{GpuPower:0} W"));

        return parts.Count == 0 ? null : "GPU " + string.Join(", ", parts);
    }
}

/// <summary>A GPU driver error during a game, with the GPU readings from just before it (null when none were fresh).</summary>
public sealed record GameSessionDriverError(DateTime Utc, string? Readings);

/// <summary>
/// The summary of one game session. A value that was never read is null; nothing is estimated.
/// </summary>
public sealed record GameSessionReport(
    List<string> Games,
    DateTime StartUtc,
    DateTime EndUtc,
    int? AverageFps,
    int? AverageLowFps,
    int? MaxCpuTemperature,
    int? MaxGpuTemperature,
    int? AverageGpuClock,
    double? MaxGpuPower,
    List<GameSessionDriverError> DriverErrors,
    List<int>? AfterburnerProfiles = null)
{
    public TimeSpan Duration => EndUtc - StartUtc;
}

/// <summary>
/// Collects one game session's readings as they come, keeping only sums and maxima (a long session doesn't grow in
/// memory), and builds the report when the game ends.
/// </summary>
public sealed class GameSessionAccumulator(DateTime startUtc)
{
    private readonly List<string> _games = [];
    private readonly List<GameSessionDriverError> _driverErrors = [];
    private readonly List<int> _afterburnerProfiles = [];

    private long _fpsSum, _fpsCount, _lowFpsSum, _lowFpsCount, _gpuClockSum, _gpuClockCount;
    private int _maxCpuTemperature = -1, _maxGpuTemperature = -1;
    private double _maxGpuPower = -1;

    public DateTime StartUtc { get; } = startUtc;

    /// <summary>The newest reading, for describing a GPU driver error.</summary>
    public GameSessionSample? Latest { get; private set; }

    public int SampleCount { get; private set; }

    public void AddGames(IEnumerable<string> names)
    {
        foreach (var name in names.Where(n => !string.IsNullOrWhiteSpace(n)))
        {
            if (!_games.Contains(name, StringComparer.OrdinalIgnoreCase))
                _games.Add(name);
        }
    }

    public void Add(GameSessionSample sample)
    {
        Latest = sample;
        SampleCount++;

        if (sample.Fps > 0)
        {
            _fpsSum += sample.Fps;
            _fpsCount++;
        }

        if (sample.LowFps > 0)
        {
            _lowFpsSum += sample.LowFps;
            _lowFpsCount++;
        }

        if (sample.GpuClock > 0)
        {
            _gpuClockSum += sample.GpuClock;
            _gpuClockCount++;
        }

        if (sample.CpuTemperature > 0)
            _maxCpuTemperature = Math.Max(_maxCpuTemperature, sample.CpuTemperature);
        if (sample.GpuTemperature > 0)
            _maxGpuTemperature = Math.Max(_maxGpuTemperature, sample.GpuTemperature);
        if (sample.GpuPower > 0)
            _maxGpuPower = Math.Max(_maxGpuPower, sample.GpuPower);
    }

    public void AddDriverError(GameSessionDriverError error) => _driverErrors.Add(error);

    /// <summary>An MSI Afterburner profile Indra applied before or during the game (each kept once, in order).</summary>
    public void AddAfterburnerProfile(int profile)
    {
        if (!_afterburnerProfiles.Contains(profile))
            _afterburnerProfiles.Add(profile);
    }

    public GameSessionReport Build(DateTime endUtc) => new(
        [.. _games],
        StartUtc,
        endUtc < StartUtc ? StartUtc : endUtc,
        Average(_fpsSum, _fpsCount),
        Average(_lowFpsSum, _lowFpsCount),
        _maxCpuTemperature > 0 ? _maxCpuTemperature : null,
        _maxGpuTemperature > 0 ? _maxGpuTemperature : null,
        Average(_gpuClockSum, _gpuClockCount),
        _maxGpuPower > 0 ? Math.Round(_maxGpuPower, 1) : null,
        [.. _driverErrors],
        _afterburnerProfiles.Count > 0 ? [.. _afterburnerProfiles] : null);

    private static int? Average(long sum, long count) => count == 0 ? null : (int)Math.Round((double)sum / count);
}
