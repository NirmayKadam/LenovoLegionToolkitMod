using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.AutoListeners;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.GameSessions;

/// <summary>
/// Records each game session for the game session report: while game detection says a game runs, it takes a reading
/// every few seconds (temperatures, GPU clock and load from the Lenovo and NVIDIA sensors; GPU power when detailed
/// sensors are on and not paused; FPS when the OSD measures it). When the game ends, the summary is saved (last 30)
/// and <see cref="SessionEnded"/> is raised. It only reads; nothing on the laptop is changed.
/// </summary>
public sealed class GameSessionRecorder
{
    /// <summary>Sessions shorter than this aren't kept (a launcher or a quick check isn't a game session).</summary>
    public static readonly TimeSpan MinimumSession = TimeSpan.FromMinutes(1);

    /// <summary>A reading older than this isn't used to describe a GPU driver error.</summary>
    public static readonly TimeSpan FreshReading = TimeSpan.FromSeconds(5);

    private readonly GameSessionSettings _history;
    private readonly Func<CancellationToken, Task<GameSessionSample>> _read;
    private readonly Func<DateTime> _utcNow;
    private readonly TimeSpan _interval;
    private readonly object _lock = new();

    private readonly GameAutoListener? _gameListener;

    /// <summary>Whether the report is switched on; null in tests that don't use the setting.</summary>
    private Func<bool>? _wanted;

    /// <summary>Starts and stops listening to game detection.</summary>
    private Func<Task> _subscribe = () => Task.CompletedTask;
    private Func<Task> _unsubscribe = () => Task.CompletedTask;

    /// <summary>One switch at a time: start-up and the card's switch can both ask at once.</summary>
    private readonly SemaphoreSlim _applying = new(1, 1);
    private bool _subscribed;

    private GameSessionAccumulator? _session;
    private CancellationTokenSource? _sampling;
    private Task _samplingTask = Task.CompletedTask;

    public GameSessionRecorder(GameAutoListener gameListener, GameSessionSettings history, ApplicationSettings settings,
        ISensorsController sensors, SensorsGroupController sensorsGroup, FpsSensorController fps)
        : this(history, new SensorReader(sensors, sensorsGroup, fps, settings).ReadAsync, () => DateTime.UtcNow, TimeSpan.FromSeconds(2))
    {
        _gameListener = gameListener;
        _wanted = () => settings.Store.GameSessionReport;
        _subscribe = async () =>
        {
            gameListener.GamesChanged += GameListener_GamesChanged;
            await gameListener.SubscribeChangedAsync(GameListener_Changed).ConfigureAwait(false);
        };
        _unsubscribe = async () =>
        {
            gameListener.GamesChanged -= GameListener_GamesChanged;
            await gameListener.UnsubscribeChangedAsync(GameListener_Changed).ConfigureAwait(false);
        };
    }

    /// <summary>For tests of the switch: whether it is on, and what subscribing and unsubscribing do.</summary>
    internal void UseSwitch(Func<bool> wanted, Func<Task> subscribe, Func<Task> unsubscribe)
    {
        _wanted = wanted;
        _subscribe = subscribe;
        _unsubscribe = unsubscribe;
    }

    /// <summary>
    /// For tests: the readings, the clock, the interval and the last MSI Afterburner profile Indra applied are
    /// supplied by the caller.
    /// </summary>
    internal GameSessionRecorder(GameSessionSettings history, Func<CancellationToken, Task<GameSessionSample>> read, Func<DateTime> utcNow, TimeSpan interval,
        Func<AfterburnerStart?>? lastAfterburner = null)
    {
        _history = history;
        _read = read;
        _utcNow = utcNow;
        _interval = interval;
        _lastAfterburner = lastAfterburner ?? (() => AfterburnerProgram.LastStarted);
    }

    /// <summary>The last MSI Afterburner profile Indra applied (profiles switched in Afterburner itself aren't seen).</summary>
    private readonly Func<AfterburnerStart?> _lastAfterburner;

    /// <summary>The last Afterburner start already noted in the running session. Guarded by <see cref="_lock"/>.</summary>
    private AfterburnerStart? _notedAfterburner;

    /// <summary>Notes an Afterburner profile Indra applied that the session hasn't seen yet. Called under <see cref="_lock"/>.</summary>
    private void NoteAfterburner(GameSessionAccumulator session)
    {
        if (_lastAfterburner() is not { } start || start == _notedAfterburner)
            return;

        _notedAfterburner = start;
        session.AddAfterburnerProfile(start.Profile);
    }

    /// <summary>Raised (on a background thread) with each saved session.</summary>
    public event EventHandler<GameSessionReport>? SessionEnded;

    /// <summary>True while a game session is being recorded.</summary>
    public bool IsRecording
    {
        get
        {
            lock (_lock)
                return _session is not null;
        }
    }

    /// <summary>
    /// Starts or stops listening to game detection to match the setting. While on, game detection keeps running
    /// (it opens no game process). Call at start-up and after the setting changes.
    /// </summary>
    public async Task ApplySettingAsync()
    {
        if (_wanted is null)
            return;

        await _applying.WaitAsync().ConfigureAwait(false);
        try
        {
            // Read inside the lock, so whoever runs last acts on the newest setting.
            var wanted = _wanted();
            if (wanted == _subscribed)
                return;

            _subscribed = wanted;
            if (wanted)
            {
                await _subscribe().ConfigureAwait(false);
            }
            else
            {
                await _unsubscribe().ConfigureAwait(false);
                await GameEndedAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _applying.Release();
        }
    }

    private void GameListener_Changed(object? sender, GameAutoListener.ChangedEventArgs e) =>
        _ = e.Running ? GameStartedAsync(_gameListener?.RunningGames ?? []) : GameEndedAsync();

    private void GameListener_GamesChanged(object? sender, EventArgs e)
    {
        lock (_lock)
            _session?.AddGames(_gameListener?.RunningGames ?? []);
    }

    /// <summary>A game started (or another one joined the running session).</summary>
    internal Task GameStartedAsync(IEnumerable<string> games)
    {
        lock (_lock)
        {
            if (_session is not null)
            {
                _session.AddGames(games);
                return Task.CompletedTask;
            }

            _session = new GameSessionAccumulator(_utcNow());
            _session.AddGames(games);
            // The profile Indra applied last is the one in effect when the game starts (as far as Indra knows).
            _notedAfterburner = null;
            NoteAfterburner(_session);
            _sampling = new CancellationTokenSource();
            var token = _sampling.Token;
            var session = _session;
            _samplingTask = Task.Run(() => SampleAsync(session, token));
            Log.Instance.Trace($"Game session started. [games={string.Join(",", games)}]");
            return Task.CompletedTask;
        }
    }

    /// <summary>The last game ended: the session is summarised and, if long enough, saved and reported. Never throws.</summary>
    internal async Task GameEndedAsync()
    {
        GameSessionAccumulator? session;
        CancellationTokenSource? sampling;
        Task samplingTask;
        lock (_lock)
        {
            session = _session;
            sampling = _sampling;
            samplingTask = _samplingTask;
            _session = null;
            _sampling = null;
        }

        if (session is null)
            return;

        try
        {
            sampling?.Cancel();
            try
            {
                await samplingTask.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                Log.Instance.Trace($"The game session's last reading took too long; the session is summarised without it.");
            }

            sampling?.Dispose();

            GameSessionReport report;
            lock (_lock)
                report = session.Build(_utcNow());

            if (report.Duration < MinimumSession)
            {
                Log.Instance.Trace($"Game session not kept: shorter than {MinimumSession.TotalMinutes:0} minute. [duration={report.Duration}]");
                return;
            }

            _history.Add(report);
            Log.Instance.Trace($"Game session ended. [games={string.Join(",", report.Games)}, duration={report.Duration}, samples={session.SampleCount}, driverErrors={report.DriverErrors.Count}]");
            SessionEnded?.Invoke(this, report);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Ending the game session failed.", ex);
        }
    }

    /// <summary>
    /// The GPU readings just before <paramref name="errorUtc"/> ("GPU 1905 MHz, 66 °C, 98% load, 66 W"), or null when
    /// no game session is running or the last reading is older than <see cref="FreshReading"/>.
    /// </summary>
    public string? GpuReadingsBefore(DateTime errorUtc)
    {
        lock (_lock)
        {
            if (_session?.Latest is not { } latest)
                return null;

            var age = errorUtc - latest.Utc;
            return age >= TimeSpan.Zero && age <= FreshReading ? latest.DescribeGpu() : null;
        }
    }

    /// <summary>A GPU driver error happened: it goes into the running session's report (if one is running).</summary>
    public void NoteDriverError(DateTime errorUtc, string? readings)
    {
        lock (_lock)
            _session?.AddDriverError(new GameSessionDriverError(errorUtc, readings));
    }

    private async Task SampleAsync(GameSessionAccumulator session, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var sample = await _read(token).ConfigureAwait(false);
                lock (_lock)
                {
                    session.Add(sample);
                    NoteAfterburner(session);   // a profile Indra applied during the game (an automation)
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"A game session reading failed.", ex);
            }

            try
            {
                await Task.Delay(_interval, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>The real readings. Nothing here needs a kernel driver except GPU power, which is read only when allowed.</summary>
    /// <summary>
    /// The real readings. Nothing here needs a kernel driver except GPU power, which is read only when allowed. Each
    /// source is read on its own: one that fails (a Lenovo sensor read timing out) used to throw away the whole sample,
    /// FPS and GPU power included. Lenovo's sensors, when this laptop has none, are asked again only every few minutes
    /// instead of every 2 s.
    /// </summary>
    private sealed class SensorReader(ISensorsController sensors, SensorsGroupController sensorsGroup, FpsSensorController fps, ApplicationSettings settings)
    {
        private static readonly TimeSpan RecheckUnsupported = TimeSpan.FromMinutes(5);

        private bool _supported;
        private long _checkedAtMs = long.MinValue;

        public async Task<GameSessionSample> ReadAsync(CancellationToken token)
        {
            int cpuTemperature = -1, gpuTemperature = -1, gpuClock = -1, gpuLoad = -1;
            try
            {
                if (!_supported && (_checkedAtMs == long.MinValue || Environment.TickCount64 - _checkedAtMs >= RecheckUnsupported.TotalMilliseconds))
                {
                    _checkedAtMs = Environment.TickCount64;
                    _supported = await sensors.IsSupportedAsync().ConfigureAwait(false);
                    if (_supported)
                        await sensors.PrepareAsync().ConfigureAwait(false);
                }

                if (_supported)
                {
                    var data = await sensors.GetDataAsync().ConfigureAwait(false);
                    (cpuTemperature, gpuTemperature, gpuClock, gpuLoad) = (data.CPU.Temperature, data.GPU.Temperature, data.GPU.CoreClock, data.GPU.Utilization);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Instance.Trace($"A game session reading of Lenovo's sensors failed; FPS and GPU power are still kept.", ex);
            }

            // Detailed sensors use PawnIO; in game-safe mode they're paused and not read.
            double gpuPower = -1;
            try
            {
                if (settings.Store.EnableHardwareSensors && !sensorsGroup.IsPaused)
                    gpuPower = sensorsGroup.Snapshot[SensorItem.GpuPower];
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"GPU power not readable for the game session.", ex);
            }

            int fpsValue = -1, lowFps = -1;
            try
            {
                var fpsData = fps.GetCurrentFpsData();
                fpsValue = int.TryParse(fpsData.Fps, out var f) ? f : -1;
                lowFps = int.TryParse(fpsData.LowFps, out var l) ? l : -1;
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"FPS not readable for the game session.", ex);
            }

            return new GameSessionSample(DateTime.UtcNow, cpuTemperature, gpuTemperature, gpuClock, gpuLoad, gpuPower, fpsValue, lowFps);
        }
    }
}
