using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Timers;

namespace LenovoLegionToolkit.Lib.AutoListeners;

public class TimeAutoListener : AbstractAutoListener<TimeAutoListener.ChangedEventArgs>
{
    public class ChangedEventArgs(DateTime utcInstant) : EventArgs
    {
        /// <summary>
        /// The second this event represents, in UTC (truncated to whole seconds).
        /// </summary>
        public DateTime UtcInstant { get; } = utcInstant;
    }

    /// <summary>
    /// The timer ticks roughly once a second but drifts, so a second can be skipped.
    /// Skipped seconds are caught up as long as the gap is at most this long.
    /// Larger gaps (sleep/resume, clock changes) only emit the current second.
    /// </summary>
    internal static readonly TimeSpan MaxCatchUp = TimeSpan.FromSeconds(5);

    private readonly Timer _timer;
    private readonly object _tickLock = new();
    private DateTime? _lastEmitted;

    public TimeAutoListener()
    {
        _timer = new Timer(1_000);
        _timer.Elapsed += Timer_Elapsed;
        _timer.AutoReset = true;
    }

    protected override Task StartAsync()
    {
        lock (_tickLock)
            _lastEmitted = null;

        if (!_timer.Enabled)
            _timer.Enabled = true;

        return Task.CompletedTask;
    }

    protected override Task StopAsync()
    {
        _timer.Enabled = false;

        return Task.CompletedTask;
    }

    private void Timer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        List<DateTime> seconds;

        lock (_tickLock)
        {
            var now = TruncateToSecond(DateTime.UtcNow);
            seconds = GetSecondsToEmit(_lastEmitted, now, MaxCatchUp);
            if (seconds.Count > 0)
                _lastEmitted = seconds[^1];
        }

        foreach (var second in seconds)
            RaiseChanged(new ChangedEventArgs(second));
    }

    internal static DateTime TruncateToSecond(DateTime dateTime) =>
        new(dateTime.Ticks - dateTime.Ticks % TimeSpan.TicksPerSecond, dateTime.Kind);

    /// <summary>
    /// Returns the seconds that should be emitted for a tick at <paramref name="now"/>,
    /// given the last emitted second. Both values must be truncated to whole seconds.
    /// </summary>
    internal static List<DateTime> GetSecondsToEmit(DateTime? lastEmitted, DateTime now, TimeSpan maxCatchUp)
    {
        if (lastEmitted is not { } last)
            return [now];

        if (now <= last)
            return [];

        if (now - last > maxCatchUp)
            return [now];

        var result = new List<DateTime>();
        for (var second = last.AddSeconds(1); second <= now; second = second.AddSeconds(1))
            result.Add(second);
        return result;
    }
}
