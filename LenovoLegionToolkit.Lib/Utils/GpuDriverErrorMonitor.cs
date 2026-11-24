using System;
using LenovoLegionToolkit.Lib.GameSessions;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Linq;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.GameProfiles;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.Resources;

namespace LenovoLegionToolkit.Lib.Utils;

/// <summary>A GPU driver error from Windows' System log, with what LLT knew at that moment (when it was seen live).</summary>
public sealed record GpuDriverError(DateTime TimeUtc, string Source, int EventId, string? Context);

/// <summary>The power mode and when it changed, as LLT saw it.</summary>
public sealed record PowerModeChange(PowerModeState Mode, DateTime Utc);

/// <summary>
/// Watches Windows' System log, read only, for NVIDIA driver errors (nvlddmkm) and display driver resets (Display
/// event 4101) while LLT runs. Each error is shown in a notification with what had just happened: a power mode change,
/// an MSI Afterburner profile LLT applied, the game running. The Safety report lists the last day's errors from the
/// same log, so no file of its own is kept.
/// </summary>
public sealed class GpuDriverErrorMonitor : IDisposable
{
    /// <summary>NVIDIA driver errors (critical or error level) and display driver resets.</summary>
    internal const string Query =
        "*[System[Provider[@Name='nvlddmkm'] and (Level=1 or Level=2)]] or *[System[Provider[@Name='Display'] and EventID=4101]]";

    /// <summary>A change within this time before an error is mentioned with it.</summary>
    internal static readonly TimeSpan Recent = TimeSpan.FromSeconds(60);

    private readonly PowerModeListener _powerModeListener;
    private readonly GameProfileController _gameProfiles;
    private readonly GameSessionRecorder _gameSessions;
    private readonly object _lock = new();
    private readonly Dictionary<DateTime, string> _contexts = [];
    private EventLogWatcher? _watcher;
    private PowerModeChange? _lastPowerMode;

    public GpuDriverErrorMonitor(PowerModeListener powerModeListener, GameProfileController gameProfiles, GameSessionRecorder gameSessions)
    {
        _powerModeListener = powerModeListener;
        _gameProfiles = gameProfiles;
        _gameSessions = gameSessions;
    }

    public void Start()
    {
        try
        {
            _powerModeListener.Changed += PowerModeListener_Changed;
            _watcher = new EventLogWatcher(new EventLogQuery("System", PathType.LogName, Query));
            _watcher.EventRecordWritten += Watcher_EventRecordWritten;
            _watcher.Enabled = true;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't watch the System log for GPU driver errors.", ex);
        }
    }

    public void Dispose()
    {
        _powerModeListener.Changed -= PowerModeListener_Changed;
        try
        {
            if (_watcher is not null)
            {
                _watcher.Enabled = false;
                _watcher.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't stop watching for GPU driver errors.", ex);
        }
    }

    /// <summary>The GPU driver errors in the System log since <paramref name="sinceUtc"/>, newest first; null when the log can't be read.</summary>
    public IReadOnlyList<GpuDriverError>? ReadSince(DateTime sinceUtc)
    {
        Dictionary<DateTime, string> contexts;
        lock (_lock)
            contexts = new Dictionary<DateTime, string>(_contexts);

        return ReadFromLog(sinceUtc, contexts);
    }

    /// <summary>Reads the System log; <paramref name="contexts"/> adds what LLT knew about errors it saw live.</summary>
    internal static IReadOnlyList<GpuDriverError>? ReadFromLog(DateTime sinceUtc, IReadOnlyDictionary<DateTime, string> contexts)
    {
        try
        {
            var query = $"({Query}) and *[System[TimeCreated[@SystemTime>='{sinceUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)}']]]";
            using var reader = new EventLogReader(new EventLogQuery("System", PathType.LogName, query) { ReverseDirection = true });
            var errors = new List<GpuDriverError>();
            for (var record = reader.ReadEvent(); record is not null; record = reader.ReadEvent())
            {
                using (record)
                {
                    if (record.TimeCreated is not { } time)
                        continue;

                    var utc = time.ToUniversalTime();
                    errors.Add(new GpuDriverError(utc, record.ProviderName, record.Id, contexts.GetValueOrDefault(utc)));
                }
            }

            return errors;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't read GPU driver errors from the System log.", ex);
            return null;
        }
    }

    private void PowerModeListener_Changed(object? sender, PowerModeListener.ChangedEventArgs e)
    {
        lock (_lock)
            _lastPowerMode = new PowerModeChange(e.State, DateTime.UtcNow);
    }

    private void Watcher_EventRecordWritten(object? sender, EventRecordWrittenEventArgs e)
    {
        try
        {
            using var record = e.EventRecord;
            if (record?.TimeCreated is not { } time)
                return;

            var utc = time.ToUniversalTime();
            PowerModeChange? lastPowerMode;
            lock (_lock)
                lastPowerMode = _lastPowerMode;

            // What the GPU was doing just before (only during a recorded game session, and only if fresh): what an
            // undervolt test needs to know.
            var readings = _gameSessions.GpuReadingsBefore(utc);
            var context = DescribeContext(utc, lastPowerMode, AfterburnerProgram.LastStarted, _gameProfiles.ActiveGameName, readings);
            _gameSessions.NoteDriverError(utc, readings);
            lock (_lock)
                _contexts[utc] = context ?? string.Empty;

            Log.Instance.Trace($"GPU driver error: {record.ProviderName} {record.Id}. {context}");
            MessagingCenter.Publish(new NotificationMessage(NotificationType.GpuDriverError, Describe(time.ToLocalTime(), context)));
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't handle a GPU driver error event.", ex);
        }
    }

    internal static string Describe(DateTime localTime, string? context) =>
        string.IsNullOrEmpty(context)
            ? string.Format(Resource.GpuDriverError_Notification, localTime.ToString("HH:mm:ss", CultureInfo.CurrentCulture))
            : string.Format(Resource.GpuDriverError_NotificationWithContext, localTime.ToString("HH:mm:ss", CultureInfo.CurrentCulture), context);

    /// <summary>What happened shortly before the error, or null when nothing did. Pure: everything comes in.</summary>
    internal static string? DescribeContext(DateTime errorUtc, PowerModeChange? lastPowerMode, AfterburnerStart? lastAfterburner, string? game, string? gpuReadings = null)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(gpuReadings))
            parts.Add(gpuReadings);

        if (lastPowerMode is { } mode && IsShortlyBefore(mode.Utc, errorUtc))
            parts.Add(string.Format(Resource.GpuDriverError_AfterPowerMode, Seconds(errorUtc - mode.Utc), mode.Mode.GetDisplayName()));

        if (lastAfterburner is { } afterburner && IsShortlyBefore(afterburner.Utc, errorUtc))
            parts.Add(string.Format(Resource.GpuDriverError_AfterAfterburner, afterburner.Profile, Seconds(errorUtc - afterburner.Utc)));

        if (!string.IsNullOrWhiteSpace(game))
            parts.Add(string.Format(Resource.GpuDriverError_Game, game));

        return parts.Count == 0 ? null : string.Join("; ", parts);
    }

    private static bool IsShortlyBefore(DateTime changeUtc, DateTime errorUtc) => changeUtc <= errorUtc && errorUtc - changeUtc <= Recent;

    private static int Seconds(TimeSpan span) => (int)Math.Round(span.TotalSeconds);
}
