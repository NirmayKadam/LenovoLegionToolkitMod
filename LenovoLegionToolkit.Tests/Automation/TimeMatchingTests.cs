using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Automation;
using LenovoLegionToolkit.Lib.Automation.Pipeline.Triggers;
using LenovoLegionToolkit.Lib.Extensions;
using Xunit;

namespace LenovoLegionToolkit.Tests.Automation;

public class TimeMatchingTests
{
    private static readonly TimeSpan Ist = new(5, 30, 0);

    [Fact]
    public void UtcToLocal_Ist_WrapsToNextDay()
    {
        // Monday 02:00 IST is saved by the UI as 20:30 UTC (Sunday).
        var local = new Time(20, 30, 0).UtcToLocal(Ist);
        Assert.Equal(new Time(2, 0, 0), local);
    }

    [Fact]
    public void UtcToLocal_NegativeOffset_WrapsToPreviousDay()
    {
        var local = new Time(3, 0, 0).UtcToLocal(TimeSpan.FromHours(-8));
        Assert.Equal(new Time(19, 0, 0), local);
    }

    [Fact]
    public void UtcToLocal_ZeroOffset_Unchanged()
    {
        Assert.Equal(new Time(13, 45, 7), new Time(13, 45, 7).UtcToLocal(TimeSpan.Zero));
    }

    [Fact]
    public void Event_UsesLocalDayAndTime()
    {
        // 2026-11-09 is a Monday.
        var e = TimeAutomationEvent.FromLocal(new DateTime(2026, 11, 9, 2, 0, 0, DateTimeKind.Local));
        Assert.Equal(DayOfWeek.Monday, e.Day);
        Assert.Equal(new Time(2, 0, 0), e.Time);
    }

    [Fact]
    public void MondayTrigger_SavedAsUtc_MatchesMondayLocalEvent()
    {
        // The bug: the old code compared the UTC day (Sunday) against the user's local selection (Monday).
        var savedUtc = new Time(20, 30, 0);
        var mondayEvent = TimeAutomationEvent.FromLocal(new DateTime(2026, 11, 9, 2, 0, 0, DateTimeKind.Local));

        Assert.Equal(mondayEvent.Time, savedUtc.UtcToLocal(Ist));
        Assert.True(TimeTriggerMatching.IsDayMatching([DayOfWeek.Monday], savedUtc.UtcToLocal(Ist), mondayEvent.Day, savedUtc.UtcToLocal(Ist)));
    }

    [Fact]
    public void IsDayMatching_EmptyDays_AlwaysTrue()
    {
        Assert.True(TimeTriggerMatching.IsDayMatching([], new Time(1, 0, 0), DayOfWeek.Friday, new Time(2, 0, 0)));
    }

    [Theory]
    // Local range 22:00 -> 02:00, selected day Monday.
    [InlineData(DayOfWeek.Monday, 23, true)]   // Monday evening part of Monday's window
    [InlineData(DayOfWeek.Tuesday, 1, true)]   // after midnight, window started Monday
    [InlineData(DayOfWeek.Monday, 1, false)]   // after midnight Monday, window started Sunday
    [InlineData(DayOfWeek.Tuesday, 23, false)] // Tuesday evening is Tuesday's window
    public void IsDayMatching_RangeCrossingLocalMidnight(DayOfWeek currentDay, int currentHour, bool expected)
    {
        var start = new Time(22, 0, 0);
        var end = new Time(2, 0, 0);
        var current = new Time(currentHour, 0, 0);

        Assert.Equal(expected, TimeTriggerMatching.IsDayMatching([DayOfWeek.Monday], start, currentDay, end, current));
    }

    [Theory]
    [InlineData(0, 10, 0, 300, true)]
    [InlineData(0, 10, 1, 300, false)]
    [InlineData(23, 59, 59, 1, true)]
    [InlineData(12, 0, 0, 0, false)]
    public async Task Periodic_UsesEventSecond(int hour, int minute, int second, int periodSeconds, bool expected)
    {
        var trigger = new PeriodicAutomationPipelineTrigger(TimeSpan.FromSeconds(periodSeconds));
        var e = new TimeAutomationEvent(new Time(hour, minute, second), DayOfWeek.Monday);

        Assert.Equal(expected, await trigger.IsMatchingEvent(e));
    }
}
