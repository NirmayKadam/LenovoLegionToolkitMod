using System;

namespace LenovoLegionToolkit.WPF.Utils;

/// <summary>How often the Dashboard's sensor-driven parts (status strip, usage rings) read again.</summary>
public static class DashboardRefresh
{
    /// <summary>The fastest they read: a 0 in a hand-edited settings file mustn't become a busy loop.</summary>
    public static readonly TimeSpan Fastest = TimeSpan.FromSeconds(0.5);

    /// <summary>
    /// The interval of whichever sensors section the Dashboard shows: the new sensor dashboard's when it is on (it
    /// needs hardware sensors), otherwise the classic one's. Never faster than <see cref="Fastest"/>.
    /// </summary>
    public static TimeSpan Interval(bool hardwareSensors, bool newSensorDashboard, double newDashboardSeconds, double classicSeconds)
    {
        var seconds = hardwareSensors && newSensorDashboard ? newDashboardSeconds : classicSeconds;
        return TimeSpan.FromSeconds(Math.Max(Fastest.TotalSeconds, double.IsFinite(seconds) ? seconds : Fastest.TotalSeconds));
    }
}
