using System;
using System.Globalization;
using System.Linq;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.GameSessions;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

/// <summary>How a game session reads in the notification and the history window.</summary>
public class GameSessionTextTests
{
    private static readonly DateTime Start = new(2026, 11, 22, 17, 0, 0, DateTimeKind.Utc);

    private static GameSessionReport Report(int minutes, int? fps = 64, int? low = 51, int? cpu = 88, int? gpu = 72, params GameSessionDriverError[] errors) =>
        new(["witcher3"], Start, Start.AddMinutes(minutes), fps, low, cpu, gpu, 1902, 66.6, [.. errors]);

    [Fact]
    public void TheSummary_ReadsLikeTheNotification() =>
        Assert.Equal(string.Join(" · ", "witcher3", "42 min", string.Format(Resource.GameSessions_FpsWithLow, 64, 51),
                string.Format(Resource.GameSessions_CpuMax, "88 °C"), string.Format(Resource.GameSessions_GpuMax, "72 °C")),
            GameSessionText.Summary(Report(42), TemperatureUnit.C));

    [Fact]
    public void Fahrenheit_IsUsedWhenChosen() =>
        Assert.Contains(string.Format(Resource.GameSessions_CpuMax, "190 °F"), GameSessionText.Summary(Report(42), TemperatureUnit.F));

    [Fact]
    public void WhatWasntMeasured_IsLeftOut()
    {
        var summary = GameSessionText.Summary(Report(42, fps: null, low: null, cpu: null, gpu: null), TemperatureUnit.C);

        Assert.Equal("witcher3 · 42 min", summary);
        Assert.Contains(Resource.GameSessions_NoFps, GameSessionText.Details(Report(42, fps: null)));
    }

    [Fact]
    public void DriverErrors_AreCounted_AndListedWithTheirReadings()
    {
        var withReadings = new GameSessionDriverError(Start.AddMinutes(3), "GPU 1905 MHz, 66 °C, 98% load, 66 W");
        var without = new GameSessionDriverError(Start.AddMinutes(4), null);
        var report = Report(42, errors: [withReadings, without]);

        Assert.EndsWith(string.Format(Resource.GameSessions_DriverErrors, 2), GameSessionText.Summary(report, TemperatureUnit.C));
        var details = GameSessionText.Details(report);
        Assert.Contains(details, d => d.Contains("GPU 1905 MHz, 66 °C, 98% load, 66 W"));
        var noReadings = string.Format(Resource.GameSessions_DriverErrorNoReadings, without.Utc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture));
        Assert.Equal(1, details.Count(d => d == noReadings));
    }

    [Theory]
    [InlineData(0, "0 min")]
    [InlineData(59, "59 min")]
    [InlineData(65, "1 h 05 min")]
    [InlineData(185, "3 h 05 min")]
    public void Durations_ReadNaturally(int minutes, string expected) =>
        Assert.Equal(expected, GameSessionText.Duration(TimeSpan.FromMinutes(minutes)));

    [Fact]
    public void ADriverErrorDuringAGame_MentionsTheGpuReadings()
    {
        var context = GpuDriverErrorMonitor.DescribeContext(Start, null, null, null, "GPU 1905 MHz, 66 °C");

        Assert.Equal("GPU 1905 MHz, 66 °C", context);
        Assert.Null(GpuDriverErrorMonitor.DescribeContext(Start, null, null, null));
    }
}
