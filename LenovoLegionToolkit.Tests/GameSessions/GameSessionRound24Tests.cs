using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.GameSessions;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using Xunit;

namespace LenovoLegionToolkit.Tests.GameSessions;

/// <summary>
/// The report's switch, the MSI Afterburner profile per session, and the CSV export. History files are real, in a
/// temporary folder.
/// </summary>
public sealed class GameSessionRound24Tests : IDisposable
{
    private static readonly DateTime Start = new(2026, 11, 23, 15, 10, 0, DateTimeKind.Utc);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "llt-gamesessions24-" + Guid.NewGuid().ToString("N"));
    private DateTime _now = Start;

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    /// <summary>Readings taken so far (each 15 ms sample reads once).</summary>
    private int _samples;

    private GameSessionRecorder Create(Func<AfterburnerStart?>? lastAfterburner = null) =>
        new(new GameSessionSettings(_dir),
            _ =>
            {
                Interlocked.Increment(ref _samples);
                return Task.FromResult(new GameSessionSample(_now, 80, 65, 1905, 98, 66, 66, 52));
            },
            () => _now, TimeSpan.FromMilliseconds(15), lastAfterburner);

    /// <summary>Waits (5 s at most) until <paramref name="count"/> more samples have been taken; false if they weren't.</summary>
    private async Task<bool> WaitForSamplesAsync(int count)
    {
        var target = Volatile.Read(ref _samples) + count;
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (Volatile.Read(ref _samples) < target)
        {
            if (DateTime.UtcNow > deadline)
                return false;
            await Task.Delay(5);
        }
        return true;
    }

    // ---- The switch ----

    [Fact]
    public async Task TheSwitch_FlippedFromTwoPlacesAtOnce_SubscribesOnce()
    {
        var recorder = Create();
        var wanted = true;
        var subscribed = 0;
        var unsubscribed = 0;
        recorder.UseSwitch(() => wanted,
            async () => { await Task.Delay(50); Interlocked.Increment(ref subscribed); },
            async () => { await Task.Delay(50); Interlocked.Increment(ref unsubscribed); });

        // Start-up and the card's switch at the same moment.
        await Task.WhenAll(Task.Run(recorder.ApplySettingAsync), Task.Run(recorder.ApplySettingAsync), Task.Run(recorder.ApplySettingAsync));
        Assert.Equal(1, subscribed);

        wanted = false;
        await Task.WhenAll(Task.Run(recorder.ApplySettingAsync), Task.Run(recorder.ApplySettingAsync));
        Assert.Equal(1, unsubscribed);
    }

    [Fact]
    public async Task SwitchingOffWhileStillSubscribing_WaitsForTheSubscribe_ThenUnsubscribes()
    {
        var recorder = Create();
        var wanted = true;
        var steps = new List<string>();
        var subscribing = new TaskCompletionSource();
        recorder.UseSwitch(() => wanted,
            async () =>
            {
                lock (steps) steps.Add("subscribe start");
                subscribing.SetResult();
                await Task.Delay(80);
                lock (steps) steps.Add("subscribe end");
            },
            async () =>
            {
                lock (steps) steps.Add("unsubscribe start");
                await Task.Delay(10);
                lock (steps) steps.Add("unsubscribe end");
            });

        var on = Task.Run(recorder.ApplySettingAsync);
        await subscribing.Task;   // the switch is flipped off while subscribing is still under way
        wanted = false;
        var off = Task.Run(recorder.ApplySettingAsync);
        await Task.WhenAll(on, off);

        Assert.Equal(["subscribe start", "subscribe end", "unsubscribe start", "unsubscribe end"], steps);
    }

    // ---- MSI Afterburner profile ----

    [Fact]
    public void TheSessionsProfiles_AreListedOnceEach_InTheOrderApplied()
    {
        var session = new GameSessionAccumulator(Start);
        session.AddAfterburnerProfile(4);
        session.AddAfterburnerProfile(2);
        session.AddAfterburnerProfile(4);   // back to the first curve

        Assert.Equal([4, 2], session.Build(Start.AddMinutes(5)).AfterburnerProfiles);
    }

    [Fact]
    public async Task TheProfileIndraAppliedLast_IsNotedWithTheSession()
    {
        var applied = new AfterburnerStart(4, Start.AddMinutes(-30));
        var recorder = Create(() => applied);
        var reports = new List<GameSessionReport>();
        recorder.SessionEnded += (_, r) => reports.Add(r);

        await recorder.GameStartedAsync(["Heaven"]);   // the profile is noted as the game starts
        _now = Start.AddMinutes(3);
        await recorder.GameEndedAsync();

        Assert.Equal([4], Assert.Single(reports).AfterburnerProfiles);
    }

    [Fact]
    public async Task AProfileAppliedDuringTheGame_IsAddedOnce()
    {
        AfterburnerStart? applied = new(4, Start.AddMinutes(-30));
        var recorder = Create(() => applied);
        var reports = new List<GameSessionReport>();
        recorder.SessionEnded += (_, r) => reports.Add(r);

        await recorder.GameStartedAsync(["Heaven"]);
        applied = new AfterburnerStart(2, Start.AddMinutes(1));   // an automation switched profile mid-game
        Assert.True(await WaitForSamplesAsync(2), "The recorder took no samples within 5 s.");   // the next sample sees it
        _now = Start.AddMinutes(5);
        await recorder.GameEndedAsync();

        Assert.Equal([4, 2], Assert.Single(reports).AfterburnerProfiles);
    }

    [Fact]
    public async Task WithoutAProfileFromIndra_NoneIsListed()
    {
        var recorder = Create(() => null);
        var reports = new List<GameSessionReport>();
        recorder.SessionEnded += (_, r) => reports.Add(r);

        await recorder.GameStartedAsync(["witcher3"]);
        _now = Start.AddMinutes(2);
        await recorder.GameEndedAsync();

        Assert.Null(Assert.Single(reports).AfterburnerProfiles);
    }

    [Fact]
    public void ASessionSavedBeforeProfilesWereKept_StillLoads()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "game_sessions.json"), """
            {
              "Sessions": [
                {
                  "Games": [ "witcher3" ],
                  "StartUtc": "2026-11-23T10:00:00Z",
                  "EndUtc": "2026-11-23T10:42:00Z",
                  "AverageFps": 64,
                  "AverageLowFps": 51,
                  "MaxCpuTemperature": 88,
                  "MaxGpuTemperature": 72,
                  "AverageGpuClock": 1902,
                  "MaxGpuPower": 66.6,
                  "DriverErrors": []
                }
              ]
            }
            """);

        var session = new GameSessionSettings(_dir).Store.Sessions.Single();

        Assert.Equal(["witcher3"], session.Games);
        Assert.Equal(64, session.AverageFps);
        Assert.Null(session.AfterburnerProfiles);
    }

    // ---- CSV ----

    private static GameSessionReport Report(string games, List<int>? profiles = null, params GameSessionDriverError[] errors) =>
        new([.. games.Split('|')], Start, Start.AddMinutes(42.25), 64, 51, 88, 72, 1902, 66.6, [.. errors], profiles);

    [Fact]
    public void AnEmptyHistory_GivesOnlyTheHeader() =>
        Assert.Equal("Start,End,Minutes,Games,Average FPS,1% low FPS (average),Highest CPU °C,Highest GPU °C,Average GPU MHz,"
                     + "Highest GPU W,Afterburner profiles (applied by Indra),GPU driver errors,GPU driver error details\r\n",
            GameSessionCsv.Build([]));

    /// <summary>A time as the CSV writes it: local time, ISO order, whatever the PC's language and calendar.</summary>
    private static string CsvTime(DateTime utc) => utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    [Fact]
    public void ARow_HasEveryValue_WithDotDecimals()
    {
        var csv = GameSessionCsv.Build([Report("witcher3", [4])]);
        var row = csv.Split("\r\n")[1];

        Assert.StartsWith(CsvTime(Start) + "," + CsvTime(Start.AddMinutes(42.25)) + ",", row);
        Assert.Contains(",42.3,witcher3,64,51,88,72,1902,66.6,4,0,", row);
    }

    [Fact]
    public void CellsWithCommasQuotesOrLineBreaks_AreQuoted()
    {
        Assert.Equal("\"a, b\"", GameSessionCsv.Escape("a, b"));
        Assert.Equal("\"say \"\"hi\"\"\"", GameSessionCsv.Escape("say \"hi\""));
        Assert.Equal("\"line1\nline2\"", GameSessionCsv.Escape("line1\nline2"));
        Assert.Equal("plain", GameSessionCsv.Escape("plain"));

        var error = new GameSessionDriverError(Start.AddMinutes(3), "GPU 1905 MHz, 66 °C, 98% load, 66 W");
        var row = GameSessionCsv.Build([Report("witcher3", null, error)]).Split("\r\n")[1];
        Assert.EndsWith(",1,\"" + CsvTime(Start.AddMinutes(3)) + " GPU 1905 MHz, 66 °C, 98% load, 66 W\"", row);
    }

    [Fact]
    public void SeveralGamesOrProfiles_ShareOneCell()
    {
        var row = GameSessionCsv.Build([Report("steam_game|Heaven", [4, 2])]).Split("\r\n")[1];

        Assert.Contains(",steam_game; Heaven,", row);
        Assert.Contains(",4; 2,", row);
    }

    [Fact]
    public void ValuesNeverRead_AreEmptyCells()
    {
        var session = new GameSessionAccumulator(Start).Build(Start.AddMinutes(2));
        var row = GameSessionCsv.Build([session]).Split("\r\n")[1];

        Assert.EndsWith(",2.0,,,,,,,,,0,", row);
    }
}
