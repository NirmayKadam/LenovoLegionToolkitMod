using System;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Automation.Resources;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Utils;
using Newtonsoft.Json;

namespace LenovoLegionToolkit.Lib.Automation.Pipeline.Triggers;

[method: JsonConstructor]
public class TimeRangeAutomationPipelineTrigger(bool isSunriseToSunset, bool isSunsetToSunrise, Time? startTime, Time? endTime, DayOfWeek[]? days)
    : ITimeRangeAutomationPipelineTrigger
{
    public bool IsSunriseToSunset { get; } = isSunriseToSunset;

    public bool IsSunsetToSunrise { get; } = isSunsetToSunrise;

    public Time? StartTime { get; } = startTime;

    public Time? EndTime { get; } = endTime;

    public DayOfWeek[] Days { get; } = days ?? [];

    public string DisplayName => Resource.TimeRangeAutomationPipelineTrigger_DisplayName;

    private readonly SunriseSunset _sunriseSunset = IoCContainer.Resolve<SunriseSunset>();

    public async Task<bool> IsMatchingEvent(IAutomationEvent automationEvent)
    {
        if (automationEvent is StartupAutomationEvent)
            return await IsMatchingState().ConfigureAwait(false);

        // Event time and day are local; stored times and sunrise/sunset are UTC and are converted to local.
        if (automationEvent is not TimeAutomationEvent e)
            return false;

        if (IsSunriseToSunset)
        {
            var (sunrise, sunset) = await GetLocalSunriseSunsetAsync().ConfigureAwait(false);
            if (sunrise is not null && sunset is not null)
                return e.Time == sunrise && IsDayMatching(sunrise.Value, e.Day, sunset.Value);
        }

        if (IsSunsetToSunrise)
        {
            var (sunrise, sunset) = await GetLocalSunriseSunsetAsync().ConfigureAwait(false);
            if (sunrise is not null && sunset is not null)
                return e.Time == sunset && IsDayMatching(sunset.Value, e.Day, sunrise.Value);
        }

        if (StartTime is not null && EndTime is not null)
        {
            var start = StartTime.Value.UtcToLocal();
            var end = EndTime.Value.UtcToLocal();
            return e.Time == start && IsDayMatching(start, e.Day, end);
        }

        return false;
    }

    public async Task<bool> IsMatchingState()
    {
        var now = DateTime.Now;
        var time = new Time(now.Hour, now.Minute, now.Second);
        var day = now.DayOfWeek;

        return await IsMatching(time, day).ConfigureAwait(false);
    }

    private async Task<(Time?, Time?)> GetLocalSunriseSunsetAsync()
    {
        var (sunrise, sunset) = await _sunriseSunset.GetSunriseSunsetAsync().ConfigureAwait(false);
        return (sunrise?.UtcToLocal(), sunset?.UtcToLocal());
    }

    public void UpdateEnvironment(AutomationEnvironment environment)
    {
        environment.IsSunriseToSunset = IsSunriseToSunset;
        environment.IsSunsetToSunrise = IsSunsetToSunrise;
        environment.StartTime = StartTime;
        environment.EndTime = EndTime;
        environment.Days = Days;
    }

    // time and dayOfWeek are local.
    private async Task<bool> IsMatching(Time time, DayOfWeek dayOfWeek)
    {
        if (IsSunriseToSunset)
        {
            var (sunrise, sunset) = await GetLocalSunriseSunsetAsync().ConfigureAwait(false);
            if (sunrise is null || sunset is null)
                return false;

            return time.IsBetween(sunrise.Value, sunset.Value) && IsDayMatching(sunrise.Value, dayOfWeek, sunset.Value, time);
        }

        if (IsSunsetToSunrise)
        {
            var (sunrise, sunset) = await GetLocalSunriseSunsetAsync().ConfigureAwait(false);
            if (sunrise is null || sunset is null)
                return false;

            return time.IsBetween(sunset.Value, sunrise.Value) && IsDayMatching(sunset.Value, dayOfWeek, sunrise.Value, time);
        }

        if (StartTime is not null && EndTime is not null)
        {
            var start = StartTime.Value.UtcToLocal();
            var end = EndTime.Value.UtcToLocal();
            return time.IsBetween(start, end) && IsDayMatching(start, dayOfWeek, end, time);
        }

        return false;
    }

    private bool IsDayMatching(Time start, DayOfWeek currentDay, Time end, Time? current = null) =>
        TimeTriggerMatching.IsDayMatching(Days, start, currentDay, end, current);

    public IAutomationPipelineTrigger DeepCopy() =>
        new TimeRangeAutomationPipelineTrigger(IsSunriseToSunset, IsSunsetToSunrise, StartTime, EndTime, Days);

    public ITimeRangeAutomationPipelineTrigger DeepCopy(bool isSunriseToSunset, bool isSunsetToSunrise, Time? startTime, Time? endTime, DayOfWeek[] days) =>
        new TimeRangeAutomationPipelineTrigger(isSunriseToSunset, isSunsetToSunrise, startTime, endTime, days);

    public override bool Equals(object? obj)
    {
        return obj is TimeRangeAutomationPipelineTrigger t &&
               IsSunriseToSunset == t.IsSunriseToSunset &&
               IsSunsetToSunrise == t.IsSunsetToSunrise &&
               StartTime == t.StartTime &&
               EndTime == t.EndTime &&
               Days.SequenceEqual(t.Days);
    }

    public override int GetHashCode()
    {
        var hc = new HashCode();
        hc.Add(IsSunriseToSunset);
        hc.Add(IsSunsetToSunrise);
        hc.Add(StartTime);
        hc.Add(EndTime);
        Days.ForEach(d => hc.Add(d));
        return hc.ToHashCode();
    }

    public override string ToString() =>
        $"{nameof(IsSunriseToSunset)}: {IsSunriseToSunset}," +
        $" {nameof(IsSunsetToSunrise)}: {IsSunsetToSunrise}," +
        $" {nameof(StartTime)}: {StartTime}," +
        $" {nameof(EndTime)}: {EndTime}," +
        $" {nameof(Days)}: [{string.Join(", ", Days)}]";
}
