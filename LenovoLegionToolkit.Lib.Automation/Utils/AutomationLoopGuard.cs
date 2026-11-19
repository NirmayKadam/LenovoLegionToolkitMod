using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LenovoLegionToolkit.Lib.Automation.Steps;

namespace LenovoLegionToolkit.Lib.Automation.Utils;

/// <summary>
/// Stops automations that keep starting each other, such as "Performance → set Quiet" with "Quiet → set Performance".
/// A power mode or Custom preset event that arrives while an automation that can change those is running, or within
/// <see cref="CauseWindow"/> after it finished, counts as caused by an automation. An automation started that way
/// more than <see cref="MaxSelfCausedStarts"/> times within <see cref="CountWindow"/> is paused until
/// <see cref="ResumeAll"/> (automations saved again, or LLT restarted). Events with no automation running just before
/// them, such as Fn+Q, never count.
/// </summary>
internal sealed class AutomationLoopGuard(Func<DateTime> utcNow)
{
    internal static readonly TimeSpan CauseWindow = TimeSpan.FromSeconds(3);
    internal static readonly TimeSpan CountWindow = TimeSpan.FromSeconds(60);
    internal const int MaxSelfCausedStarts = 3;

    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, Queue<DateTime>> _starts = [];
    private readonly HashSet<Guid> _paused = [];
    private int _causingRuns;
    private DateTime _lastCausingRunEnd = DateTime.MinValue;

    public AutomationLoopGuard() : this(() => DateTime.UtcNow) { }

    /// <summary>Steps whose changes raise the events loops are made of.</summary>
    public static bool CanCauseEvents(IEnumerable<IAutomationStep> steps) =>
        steps.Any(s => s is PowerModeAutomationStep or GodModePresetAutomationStep or CycleGodModePresetAutomationStep or AIEngineAutomationStep);

    public static bool IsLoopableEvent(IAutomationEvent e) => e is PowerModeAutomationEvent or CustomModePresetAutomationEvent;

    public void RunStarted()
    {
        lock (_lock)
            _causingRuns++;
    }

    public void RunFinished()
    {
        lock (_lock)
        {
            _causingRuns = Math.Max(0, _causingRuns - 1);
            _lastCausingRunEnd = utcNow();
        }
    }

    public bool IsSelfCaused(IAutomationEvent e)
    {
        if (!IsLoopableEvent(e))
            return false;

        lock (_lock)
            return _causingRuns > 0 || utcNow() - _lastCausingRunEnd <= CauseWindow;
    }

    public bool IsPaused(Guid pipelineId)
    {
        lock (_lock)
            return _paused.Contains(pipelineId);
    }

    /// <summary>
    /// Records a start caused by an automation. True when this start goes over the limit: the automation is
    /// paused from now on and this run must be skipped.
    /// </summary>
    public bool RecordSelfCausedStart(Guid pipelineId)
    {
        lock (_lock)
        {
            var now = utcNow();
            if (!_starts.TryGetValue(pipelineId, out var starts))
                _starts[pipelineId] = starts = new Queue<DateTime>();

            while (starts.Count > 0 && now - starts.Peek() > CountWindow)
                starts.Dequeue();

            starts.Enqueue(now);

            if (starts.Count <= MaxSelfCausedStarts)
                return false;

            _starts.Remove(pipelineId);
            _paused.Add(pipelineId);
            return true;
        }
    }

    public void ResumeAll()
    {
        lock (_lock)
        {
            _paused.Clear();
            _starts.Clear();
        }
    }
}
