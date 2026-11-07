using System;
using System.Linq;
using LenovoLegionToolkit.Lib.Extensions;

namespace LenovoLegionToolkit.Lib.Automation.Pipeline.Triggers;

/// <summary>
/// Day matching shared by the time triggers. All values passed in must be local time,
/// because the selected days are local days.
/// </summary>
internal static class TimeTriggerMatching
{
    public static bool IsDayMatching(DayOfWeek[] days, Time start, DayOfWeek currentDay, Time end, Time? current = null)
    {
        if (days.IsEmpty())
            return true;

        if (start <= end || current is null)
            return days.Contains(currentDay);

        // Cross-midnight: if current time is in the post-midnight portion (00:00 <= current <= end),
        // the active window originated on the previous day.
        if (current.Value <= end)
        {
            var yesterday = (DayOfWeek)(((int)currentDay + 6) % 7);
            return days.Contains(yesterday);
        }

        return days.Contains(currentDay);
    }
}
