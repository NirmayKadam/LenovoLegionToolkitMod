using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.GameSessions;
using LenovoLegionToolkit.WPF.Resources;

namespace LenovoLegionToolkit.WPF.Utils;

/// <summary>How a game session reads in the notification, the Game sessions card and its history window.</summary>
public static class GameSessionText
{
    /// <summary>One line: "witcher3 · 42 min · 64 FPS (51 at 1% low) · CPU 88 °C max · GPU 72 °C max".</summary>
    public static string Summary(GameSessionReport report, TemperatureUnit unit)
    {
        var parts = new List<string>
        {
            report.Games.Count > 0 ? string.Join(", ", report.Games) : Resource.GameSessions_UnknownGame,
            Duration(report.Duration),
        };

        if (report.AverageFps is { } fps)
            parts.Add(report.AverageLowFps is { } low
                ? string.Format(Resource.GameSessions_FpsWithLow, fps, low)
                : string.Format(Resource.GameSessions_Fps, fps));

        if (report.MaxCpuTemperature is { } cpu)
            parts.Add(string.Format(Resource.GameSessions_CpuMax, Temperature(cpu, unit)));

        if (report.MaxGpuTemperature is { } gpu)
            parts.Add(string.Format(Resource.GameSessions_GpuMax, Temperature(gpu, unit)));

        if (report.AfterburnerProfiles is { Count: > 0 } profiles)
            parts.Add(string.Format(Resource.GameSessions_AfterburnerShort, string.Join(", ", profiles)));

        if (report.DriverErrors.Count > 0)
            parts.Add(string.Format(Resource.GameSessions_DriverErrors, report.DriverErrors.Count));

        return string.Join(" · ", parts);
    }

    /// <summary>The details below the summary in the history window.</summary>
    public static IReadOnlyList<string> Details(GameSessionReport report)
    {
        var lines = new List<string>
        {
            string.Format(Resource.GameSessions_Started, report.StartUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)),
        };

        if (report.AverageFps is null)
            lines.Add(Resource.GameSessions_NoFps);

        if (report.AverageGpuClock is { } clock)
            lines.Add(string.Format(Resource.GameSessions_GpuClock, clock));

        if (report.MaxGpuPower is { } power)
            lines.Add(string.Format(Resource.GameSessions_GpuPower, power.ToString("0", CultureInfo.CurrentCulture)));

        if (report.AfterburnerProfiles is { Count: > 0 } profiles)
            lines.Add(string.Format(Resource.GameSessions_Afterburner, string.Join(", ", profiles)));

        lines.AddRange(report.DriverErrors.Select(e =>
        {
            var time = e.Utc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);
            return string.IsNullOrWhiteSpace(e.Readings)
                ? string.Format(Resource.GameSessions_DriverErrorNoReadings, time)
                : string.Format(Resource.GameSessions_DriverError, time, e.Readings);
        }));

        return lines;
    }

    /// <summary>"42 min", or "1 h 05 min" from an hour on.</summary>
    public static string Duration(TimeSpan duration) =>
        duration.TotalMinutes < 60
            ? string.Create(CultureInfo.CurrentCulture, $"{(int)Math.Max(0, duration.TotalMinutes)} min")
            : string.Create(CultureInfo.CurrentCulture, $"{(int)duration.TotalHours} h {duration.Minutes:00} min");

    /// <summary>A temperature in the user's unit.</summary>
    public static string Temperature(int celsius, TemperatureUnit unit) =>
        unit == TemperatureUnit.F
            ? string.Create(CultureInfo.CurrentCulture, $"{celsius * 9.0 / 5.0 + 32:0} °F")
            : string.Create(CultureInfo.CurrentCulture, $"{celsius} °C");
}
