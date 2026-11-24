using System;
using System.IO;
using System.Linq;
using LenovoLegionToolkit.Lib.GameSessions;
using LenovoLegionToolkit.Lib.Settings;
using Xunit;

namespace LenovoLegionToolkit.Tests.GameSessions;

/// <summary>
/// The game session summary. Readings shaped like the 2026-11-22 Witcher 3 session (HWiNFO log): GPU 1905 MHz,
/// 64–70 FPS, CPU in the 80s °C. Nothing is estimated: a value never read stays empty.
/// </summary>
public sealed class GameSessionReportTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 11, 22, 17, 0, 43, DateTimeKind.Utc);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "llt-gamesessions-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    private static GameSessionSample Sample(int seconds, int cpu = 80, int gpu = 65, int clock = 1905, int load = 98, double power = 66, int fps = 66, int low = 52) =>
        new(Start.AddSeconds(seconds), cpu, gpu, clock, load, power, fps, low);

    [Fact]
    public void AveragesAndMaxima_ComeFromTheReadings()
    {
        var session = new GameSessionAccumulator(Start);
        session.AddGames(["witcher3"]);
        session.Add(Sample(2, cpu: 81, gpu: 64, fps: 64, low: 50, power: 63.9));
        session.Add(Sample(4, cpu: 88, gpu: 66, fps: 70, low: 54, power: 66.6));
        session.Add(Sample(6, cpu: 84, gpu: 65, clock: 1897, fps: 65, low: 52, power: 65.4));

        var report = session.Build(Start.AddMinutes(3));

        Assert.Equal(["witcher3"], report.Games);
        Assert.Equal(TimeSpan.FromMinutes(3), report.Duration);
        Assert.Equal(66, report.AverageFps);           // (64 + 70 + 65) / 3 = 66.3
        Assert.Equal(52, report.AverageLowFps);        // (50 + 54 + 52) / 3
        Assert.Equal(88, report.MaxCpuTemperature);
        Assert.Equal(66, report.MaxGpuTemperature);
        Assert.Equal(1902, report.AverageGpuClock);    // (1905 + 1905 + 1897) / 3 = 1902.3
        Assert.Equal(66.6, report.MaxGpuPower!.Value, precision: 3);
    }

    [Fact]
    public void ValuesThatWereNeverRead_StayEmpty()
    {
        // No OSD FPS, no detailed sensors (game-safe mode), a sleeping GPU: every reading -1.
        var session = new GameSessionAccumulator(Start);
        session.Add(new GameSessionSample(Start.AddSeconds(2), -1, -1, -1, -1, -1, -1, -1));

        var report = session.Build(Start.AddMinutes(2));

        Assert.Null(report.AverageFps);
        Assert.Null(report.AverageLowFps);
        Assert.Null(report.MaxCpuTemperature);
        Assert.Null(report.MaxGpuTemperature);
        Assert.Null(report.AverageGpuClock);
        Assert.Null(report.MaxGpuPower);
    }

    [Fact]
    public void OnlyReadingsThatWereMeasured_CountInTheAverages()
    {
        // FPS came in only after the OSD was opened mid-game.
        var session = new GameSessionAccumulator(Start);
        session.Add(Sample(2, fps: -1, low: -1));
        session.Add(Sample(4, fps: 60, low: 40));
        session.Add(Sample(6, fps: 70, low: 50));

        var report = session.Build(Start.AddMinutes(1));

        Assert.Equal(65, report.AverageFps);
        Assert.Equal(45, report.AverageLowFps);
    }

    [Fact]
    public void GamesAreListedOnce_InTheOrderTheyStarted()
    {
        var session = new GameSessionAccumulator(Start);
        session.AddGames(["Heaven"]);
        session.AddGames(["witcher3", "heaven", " "]);

        Assert.Equal(["Heaven", "witcher3"], session.Build(Start.AddMinutes(1)).Games);
    }

    [Fact]
    public void DriverErrors_KeepTheReadingsFromBeforeThem()
    {
        var session = new GameSessionAccumulator(Start);
        session.Add(Sample(2));
        session.AddDriverError(new GameSessionDriverError(Start.AddSeconds(3), session.Latest!.DescribeGpu()));

        var error = session.Build(Start.AddMinutes(1)).DriverErrors.Single();

        Assert.Equal("GPU 1905 MHz, 65 °C, 98% load, 66 W", error.Readings);
    }

    [Fact]
    public void TheGpuReadings_LeaveOutWhatWasntRead()
    {
        Assert.Equal("GPU 1905 MHz, 66 °C, 98% load", Sample(0, gpu: 66, power: -1).DescribeGpu());
        Assert.Null(new GameSessionSample(Start, -1, -1, -1, -1, -1, -1, -1).DescribeGpu());
    }

    [Fact]
    public void OnlyTheLast30Sessions_AreKept_NewestFirst()
    {
        var history = new GameSessionSettings(_dir);
        for (var i = 0; i < 35; i++)
            history.Add(new GameSessionAccumulator(Start.AddHours(i)).Build(Start.AddHours(i).AddMinutes(10)));

        var saved = new GameSessionSettings(_dir).Store.Sessions;

        Assert.Equal(GameSessionSettings.MaxSessions, saved.Count);
        Assert.Equal(Start.AddHours(34), saved[0].StartUtc);
        Assert.Equal(Start.AddHours(5), saved[^1].StartUtc);
    }

    [Fact]
    public void ASavedSession_ReadsBackTheSame()
    {
        var session = new GameSessionAccumulator(Start);
        session.AddGames(["witcher3"]);
        session.Add(Sample(2));
        session.AddDriverError(new GameSessionDriverError(Start.AddSeconds(3), "GPU 1905 MHz"));
        session.AddAfterburnerProfile(4);
        var report = session.Build(Start.AddMinutes(5));

        new GameSessionSettings(_dir).Add(report);
        var back = new GameSessionSettings(_dir).Store.Sessions.Single();

        // Every field (the record holds lists, so record equality would compare list references, not contents).
        Assert.Equal(report.Games, back.Games);
        Assert.Equal(report.StartUtc, back.StartUtc);
        Assert.Equal(report.EndUtc, back.EndUtc);
        Assert.Equal(report.AverageFps, back.AverageFps);
        Assert.Equal(report.AverageLowFps, back.AverageLowFps);
        Assert.Equal(report.MaxCpuTemperature, back.MaxCpuTemperature);
        Assert.Equal(report.MaxGpuTemperature, back.MaxGpuTemperature);
        Assert.Equal(report.AverageGpuClock, back.AverageGpuClock);
        Assert.Equal(report.MaxGpuPower, back.MaxGpuPower);
        Assert.Equal(report.DriverErrors, back.DriverErrors);
        Assert.Equal(report.AfterburnerProfiles, back.AfterburnerProfiles);
    }
}
