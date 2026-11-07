using System;

namespace LenovoLegionToolkit.Lib.Extensions;

public static class TimeExtensions
{
    private const int SECONDS_PER_DAY = 24 * 60 * 60;

    public static Time UtcNow
    {
        get
        {
            var utcNow = DateTime.UtcNow;
            return new(utcNow.Hour, utcNow.Minute, utcNow.Second);
        }
    }

    public static Time LocalNow
    {
        get
        {
            var now = DateTime.Now;
            return new(now.Hour, now.Minute, now.Second);
        }
    }

    public static bool IsBetween(this Time current, Time start, Time end)
    {
        if (start <= end)
            return current >= start && current <= end;

        return current >= start || current <= end;
    }

    /// <summary>
    /// Converts a time of day stored in UTC (as the automation UI saves it) to local time of day,
    /// using the current local UTC offset. This is the same conversion the UI uses to display it.
    /// </summary>
    public static Time UtcToLocal(this Time utc) => UtcToLocal(utc, TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow));

    internal static Time UtcToLocal(this Time utc, TimeSpan offset)
    {
        var totalSeconds = (utc.TotalSeconds + (long)offset.TotalSeconds) % SECONDS_PER_DAY;
        if (totalSeconds < 0)
            totalSeconds += SECONDS_PER_DAY;

        var seconds = (int)totalSeconds;
        return new(seconds / 3600, seconds % 3600 / 60, seconds % 60);
    }
}
