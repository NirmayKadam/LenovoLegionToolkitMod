using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace LenovoLegionToolkit.Lib.GameSessions;

/// <summary>
/// Game sessions as CSV, one row per session, to compare runs (for example undervolt curves) in a spreadsheet next to
/// HWiNFO logs. Numbers and times use the invariant culture (dot decimals, ISO times in local time); a value that was
/// never read is an empty cell.
/// </summary>
public static class GameSessionCsv
{
    public static readonly string[] Header =
    [
        "Start", "End", "Minutes", "Games", "Average FPS", "1% low FPS (average)", "Highest CPU °C", "Highest GPU °C",
        "Average GPU MHz", "Highest GPU W", "Afterburner profiles (applied by Indra)", "GPU driver errors", "GPU driver error details",
    ];

    public static string Build(IEnumerable<GameSessionReport> sessions)
    {
        var csv = new StringBuilder();
        csv.Append(string.Join(",", Header.Select(Escape))).Append("\r\n");

        foreach (var s in sessions)
        {
            var cells = new[]
            {
                Time(s.StartUtc),
                Time(s.EndUtc),
                s.Duration.TotalMinutes.ToString("0.0", CultureInfo.InvariantCulture),   // half away from zero, as a spreadsheet rounds
                string.Join("; ", s.Games),
                Number(s.AverageFps),
                Number(s.AverageLowFps),
                Number(s.MaxCpuTemperature),
                Number(s.MaxGpuTemperature),
                Number(s.AverageGpuClock),
                s.MaxGpuPower?.ToString("0.#", CultureInfo.InvariantCulture) ?? string.Empty,
                string.Join("; ", s.AfterburnerProfiles ?? []),
                s.DriverErrors.Count.ToString(CultureInfo.InvariantCulture),
                string.Join("; ", s.DriverErrors.Select(e => $"{Time(e.Utc)} {e.Readings}".Trim())),
            };
            csv.Append(string.Join(",", cells.Select(Escape))).Append("\r\n");
        }

        return csv.ToString();
    }

    private static string Time(DateTime utc) => utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string Number(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>Quotes a cell that holds a comma, a quote or a line break, doubling its quotes (RFC 4180).</summary>
    internal static string Escape(string cell) =>
        cell.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + cell.Replace("\"", "\"\"") + "\"" : cell;
}
