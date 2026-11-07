using System;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Automation.Resources;

namespace LenovoLegionToolkit.Lib.Automation.Pipeline.Triggers;

[method: JsonConstructor]
public class PeriodicAutomationPipelineTrigger(TimeSpan period) : IPeriodicAutomationPipelineTrigger
{
    public string DisplayName => Resource.PeriodicActionPipelineTrigger_DisplayName;

    public TimeSpan Period { get; } = period;

    public Task<bool> IsMatchingEvent(IAutomationEvent automationEvent)
    {
        // Use the second the event represents (local), not "now": the listener may emit
        // several caught-up seconds in one tick, and rounding "now" can skip or repeat a second.
        return automationEvent is TimeAutomationEvent e
            ? Task.FromResult(IsMatching(e.Time.TotalSeconds))
            : Task.FromResult(false);
    }

    public Task<bool> IsMatchingState() => Task.FromResult(IsMatching((long)Math.Round(DateTime.Now.TimeOfDay.TotalSeconds)));

    public IAutomationPipelineTrigger DeepCopy() => new PeriodicAutomationPipelineTrigger(Period);

    public IPeriodicAutomationPipelineTrigger DeepCopy(TimeSpan period) => new PeriodicAutomationPipelineTrigger(period);

    private bool IsMatching(long currentDaySeconds)
    {
        var periodSeconds = (long)Math.Round(Period.TotalSeconds);

        if (periodSeconds <= 0)
            return false;

        return currentDaySeconds % periodSeconds == 0;
    }

    public void UpdateEnvironment(AutomationEnvironment environment) => environment.Period = Period;
}
