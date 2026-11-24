using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.GameSessions;
using LenovoLegionToolkit.Lib.Settings;
using Xunit;

namespace LenovoLegionToolkit.Tests.GameSessions;

/// <summary>
/// The recorder's session handling. The readings come from a list of recorded values (the real ones come from the
/// laptop's sensors) and the clock is stepped by the test; the history file is real, in a temporary folder.
/// </summary>
public sealed class GameSessionRecorderTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 11, 22, 17, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "llt-gamerecorder-" + Guid.NewGuid().ToString("N"));
    private DateTime _now = Start;
    private int _reads;

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    // No MSI Afterburner profile from Indra (the app-wide last one is not read in tests).
    private GameSessionRecorder Create(GameSessionSettings history) =>
        new(history, ReadAsync, () => _now, TimeSpan.FromMilliseconds(20), () => null);

    private Task<GameSessionSample> ReadAsync(CancellationToken token)
    {
        Interlocked.Increment(ref _reads);
        return Task.FromResult(new GameSessionSample(_now, 80, 65, 1905, 98, 66, 66, 52));
    }

    /// <summary>Waits up to 5 s for <paramref name="condition"/>; false if it never came true.</summary>
    private static async Task<bool> WaitForAsync(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(5))
                return false;
            await Task.Delay(10);
        }
        return true;
    }

    [Fact]
    public async Task AGameStartAndEnd_SavesOneSession_AndReportsIt()
    {
        var history = new GameSessionSettings(_dir);
        var recorder = Create(history);
        var reports = new List<GameSessionReport>();
        recorder.SessionEnded += (_, r) => reports.Add(r);

        await recorder.GameStartedAsync(["witcher3"]);
        Assert.True(await WaitForAsync(() => Volatile.Read(ref _reads) >= 3), "Fewer than 3 readings within 5 s.");
        _now = Start.AddMinutes(42);
        await recorder.GameEndedAsync();

        var report = Assert.Single(reports);
        Assert.Equal(["witcher3"], report.Games);
        Assert.Equal(TimeSpan.FromMinutes(42), report.Duration);
        Assert.Equal(66, report.AverageFps);
        Assert.Equal(new GameSessionSettings(_dir).Store.Sessions.Single().StartUtc, report.StartUtc);
        Assert.False(recorder.IsRecording);
    }

    [Fact]
    public async Task ASecondGameDuringTheSession_JoinsIt()
    {
        var recorder = Create(new GameSessionSettings(_dir));
        var reports = new List<GameSessionReport>();
        recorder.SessionEnded += (_, r) => reports.Add(r);

        await recorder.GameStartedAsync(["steam_game"]);
        await recorder.GameStartedAsync(["Heaven"]);
        _now = Start.AddMinutes(5);
        await recorder.GameEndedAsync();

        Assert.Equal(["steam_game", "Heaven"], Assert.Single(reports).Games);
    }

    [Fact]
    public async Task AVeryShortSession_IsntKept()
    {
        var history = new GameSessionSettings(_dir);
        var recorder = Create(history);
        var reports = new List<GameSessionReport>();
        recorder.SessionEnded += (_, r) => reports.Add(r);

        await recorder.GameStartedAsync(["launcher"]);
        _now = Start.AddSeconds(40);
        await recorder.GameEndedAsync();

        Assert.Empty(reports);
        Assert.Empty(new GameSessionSettings(_dir).Store.Sessions);
    }

    [Fact]
    public async Task AGameEndWithoutReadings_StillReportsHowLongItRan()
    {
        var failedReads = 0;
        var recorder = new GameSessionRecorder(new GameSessionSettings(_dir),
            _ =>
            {
                Interlocked.Increment(ref failedReads);
                return Task.FromException<GameSessionSample>(new InvalidOperationException("sensors unavailable"));
            },
            () => _now, TimeSpan.FromMilliseconds(20), () => null);
        var reports = new List<GameSessionReport>();
        recorder.SessionEnded += (_, r) => reports.Add(r);

        await recorder.GameStartedAsync(["witcher3"]);
        Assert.True(await WaitForAsync(() => Volatile.Read(ref failedReads) >= 2), "The recorder tried no readings within 5 s.");
        _now = Start.AddMinutes(10);
        await recorder.GameEndedAsync();

        var report = Assert.Single(reports);
        Assert.Equal(TimeSpan.FromMinutes(10), report.Duration);
        Assert.Null(report.AverageFps);
    }

    [Fact]
    public async Task NothingIsRecorded_WhileNoGameRuns()
    {
        var recorder = Create(new GameSessionSettings(_dir));

        await recorder.GameEndedAsync();   // an end with no start
        await Task.Delay(80);              // four sampling intervals, in which nothing may be read

        Assert.Equal(0, _reads);
        Assert.False(recorder.IsRecording);
        Assert.Null(recorder.GpuReadingsBefore(Start));
    }

    [Fact]
    public async Task ADriverError_GetsTheGpuReadings_OnlyWhileFresh()
    {
        var recorder = Create(new GameSessionSettings(_dir));
        await recorder.GameStartedAsync(["Heaven"]);
        Assert.True(await WaitForAsync(() => Volatile.Read(ref _reads) >= 1), "No reading within 5 s.");

        Assert.Equal("GPU 1905 MHz, 65 °C, 98% load, 66 W", recorder.GpuReadingsBefore(_now.AddSeconds(2)));
        Assert.Null(recorder.GpuReadingsBefore(_now + GameSessionRecorder.FreshReading + TimeSpan.FromSeconds(1)));

        await recorder.GameEndedAsync();
    }
}
