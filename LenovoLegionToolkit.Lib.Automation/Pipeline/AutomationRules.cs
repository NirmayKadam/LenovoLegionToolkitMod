using System;
using System.Collections.Generic;
using System.Linq;
using LenovoLegionToolkit.Lib.Automation.Pipeline.Triggers;
using LenovoLegionToolkit.Lib.Automation.Resources;
using LenovoLegionToolkit.Lib.Automation.Steps;
using LenovoLegionToolkit.Lib.Extensions;

namespace LenovoLegionToolkit.Lib.Automation.Pipeline;

public enum StepIssueKind
{
    /// <summary>The step runs but changes nothing that lasts.</summary>
    NoEffect,
    /// <summary>The step is never reached.</summary>
    NeverRuns,
    /// <summary>A later step sets the same thing.</summary>
    Overridden,
    /// <summary>Worth knowing; the step still does what it says.</summary>
    Info,
    /// <summary>The laptop doesn't support the step; it is skipped at run time.</summary>
    NotSupported,
    /// <summary>A quick action that refers back to one already being expanded.</summary>
    QuickActionCycle
}

/// <summary>
/// What the rules need to know about a step: its type and, for the steps whose state matters to a rule, that state.
/// <paramref name="RefersBack"/> marks a quick action that points back to a quick action already being expanded.
/// </summary>
public readonly record struct StepInfo(Type Type, object? State = null, bool RefersBack = false)
{
    public static StepInfo From(IAutomationStep step, bool refersBack = false) => step switch
    {
        GodModePresetAutomationStep s => new(step.GetType(), s.PresetId, refersBack),
        IAutomationStep<ToggleState> s => new(step.GetType(), s.State, refersBack),
        IAutomationStep<PowerModeState> s => new(step.GetType(), s.State, refersBack),
        _ => new(step.GetType(), null, refersBack)
    };
}

public sealed record StepIssue(int StepIndex, StepIssueKind Kind, string Message);

/// <summary>An automation as the loop check sees it. Steps are already expanded (quick actions included).</summary>
public sealed record LoopNode(Guid Id, string Name, IAutomationPipelineTrigger? Trigger, IReadOnlyList<StepInfo> Steps);

/// <summary>
/// Checks which steps of an automation can actually do something, and whether automations can start each other
/// in a loop. Pure: reads only what it is given, never the laptop.
/// </summary>
public static class AutomationRules
{
    /// <summary>Steps that set one lasting setting, so a later step of the same kind replaces an earlier one.</summary>
    private static readonly HashSet<Type> SettingSteps =
    [
        typeof(AIEngineAutomationStep),
        typeof(AirplaneModeAutomationStep),
        typeof(FanMaxSpeedAutomationStep),
        typeof(MacroAutomationStep),
        typeof(OsdAutomationStep),
        typeof(OsdLockPositionAutomationStep),
        typeof(OverclockDiscreteGPUAutomationStep),
        typeof(WiFiAutomationStep),
        typeof(DisplayBrightnessAutomationStep),
        typeof(SpeakerVolumeAutomationStep),
        typeof(RGBKeyboardBacklightAutomationStep),
        typeof(SpectrumKeyboardBacklightBrightnessAutomationStep),
        typeof(SpectrumKeyboardBacklightProfileAutomationStep),
        typeof(GodModePresetAutomationStep),
        typeof(CycleGodModePresetAutomationStep),
        typeof(AfterburnerProfileAutomationStep),
    ];

    public static IReadOnlyList<StepIssue> Check(IAutomationPipelineTrigger? trigger, IReadOnlyList<StepInfo> steps, Func<Type, bool>? isSupported = null)
    {
        var issues = new List<StepIssue>();
        var triggerMode = TriggerPowerMode(trigger);
        var mode = triggerMode;
        var powerModeSeen = false;
        var closeIndex = -1;

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];

            if (closeIndex >= 0)
            {
                issues.Add(new(i, StepIssueKind.NeverRuns, Resource.AutomationRule_AfterCloseApp));
                continue;
            }

            if (step.RefersBack)
            {
                issues.Add(new(i, StepIssueKind.QuickActionCycle, Resource.AutomationRule_QuickActionCycle));
                continue;
            }

            if (isSupported is not null && !isSupported(step.Type))
                issues.Add(new(i, StepIssueKind.NotSupported, Resource.AutomationRule_NotSupported));

            if (step.Type == typeof(PowerModeAutomationStep) && step.State is PowerModeState target)
            {
                if (!powerModeSeen && triggerMode is { } started)
                {
                    issues.Add(target == started
                        ? new(i, StepIssueKind.NoEffect, Resource.AutomationRule_AlreadyInMode)
                        : new(i, StepIssueKind.Info, Resource.AutomationRule_SwitchesAway));
                }

                powerModeSeen = true;
                mode = target;
            }
            else if (step.Type == typeof(AIEngineAutomationStep)
                     && step.State is ToggleState and not ToggleState.Off
                     && mode is { } aiMode && aiMode != PowerModeState.Balance)
            {
                issues.Add(new(i, StepIssueKind.NoEffect, Resource.AutomationRule_AIEngineNotBalance));
            }
            else if ((step.Type == typeof(GodModePresetAutomationStep) || step.Type == typeof(CycleGodModePresetAutomationStep))
                     && mode is { } presetMode && presetMode != PowerModeState.GodMode)
            {
                issues.Add(new(i, StepIssueKind.Info, Resource.AutomationRule_PresetNeedsCustom));
            }
            else if (step.Type == typeof(CloseAppAutomationStep))
            {
                closeIndex = i;
            }
        }

        var lastReached = closeIndex >= 0 ? closeIndex : steps.Count - 1;
        for (var i = 0; i < lastReached; i++)
        {
            var later = OverriddenBy(steps, i, lastReached);
            if (later is not null)
                issues.Add(new(i, StepIssueKind.Overridden, string.Format(Resource.AutomationRule_Overridden, later.Value + 1)));
        }

        return [.. issues.OrderBy(issue => issue.StepIndex)];
    }

    /// <summary>
    /// Why a step of <paramref name="stepType"/> can't usefully be added at the end of this automation,
    /// or null when it can.
    /// </summary>
    public static string? WhyNotAddable(Type stepType, IAutomationPipelineTrigger? trigger, IReadOnlyList<StepInfo> steps)
    {
        if (steps.Any(s => s.Type == typeof(CloseAppAutomationStep)))
            return Resource.AutomationRule_AddBlocked_CloseApp;

        if (stepType == typeof(AIEngineAutomationStep) && ModeAtEnd(trigger, steps) is { } mode && mode != PowerModeState.Balance)
            return string.Format(Resource.AutomationRule_AddBlocked_AIEngine, mode.GetDisplayName());

        return null;
    }

    /// <summary>
    /// The step-order warnings LLT has always shown. They used to stop after the power mode check when there was no
    /// Power mode step, so the hybrid mode, GPU, HDR and resolution warnings only appeared next to one.
    /// </summary>
    public static IEnumerable<string> OrderWarnings(IReadOnlyList<StepInfo> steps)
    {
        var types = steps.Select(s => s.Type).ToList();

        var powerModeStepIndex = types.FindLastIndex(t => t == typeof(PowerModeAutomationStep));

        for (var i = 0; i < powerModeStepIndex; i++)
        {
            if (types[i] == typeof(DisplayBrightnessAutomationStep) || types[i] == typeof(RefreshRateAutomationStep))
            {
                yield return Resource.AutomationPipeline_Warning_Power_Mode_Visual;
                break;
            }

            if (types[i] != typeof(FanMaxSpeedAutomationStep))
                continue;

            yield return Resource.AutomationPipeline_Warning_Power_Mode_Fan;
            break;
        }

        var hybridModeStepIndex = types.FindIndex(t => t == typeof(HybridModeAutomationStep));
        if (hybridModeStepIndex != -1 && hybridModeStepIndex != types.Count - 1)
            yield return Resource.AutomationPipeline_Warning_Hybrid_Mode;

        var deactivateGPUIndex = types.FindIndex(t => t == typeof(DeactivateGPUAutomationStep));
        var overclockIndex = types.FindIndex(t => t == typeof(OverclockDiscreteGPUAutomationStep));
        if (deactivateGPUIndex != -1 && overclockIndex != -1 && overclockIndex > deactivateGPUIndex)
            yield return Resource.AutomationPipeline_Warning_GPU_OC;

        var hdrStepIndex = types.FindIndex(t => t == typeof(HDRAutomationStep));
        var brightnessStepIndex = types.FindLastIndex(t => t == typeof(DisplayBrightnessAutomationStep));
        if (hdrStepIndex != -1 && brightnessStepIndex != -1 && brightnessStepIndex > hdrStepIndex)
            yield return Resource.AutomationPipeline_Warning_HDR;

        var resolutionIndex = types.FindLastIndex(t => t == typeof(ResolutionAutomationStep));
        var refreshRateIndex = types.FindIndex(t => t == typeof(RefreshRateAutomationStep));
        if (resolutionIndex != -1 && refreshRateIndex != -1 && refreshRateIndex < resolutionIndex)
            yield return Resource.AutomationPipeline_Warning_Resolution;
    }

    /// <summary>
    /// Automations that can start each other in a loop, each with the loop it is part of ("A → B → A").
    /// An automation that sets a power mode (or Custom preset) starts the automations triggered by that mode (or preset).
    /// A chain that ends is not a loop.
    /// </summary>
    public static IReadOnlyDictionary<Guid, string> FindLoops(IReadOnlyList<LoopNode> nodes)
    {
        var listens = nodes.ToDictionary(n => n.Id, n => Listens(n.Trigger));
        var successors = nodes.ToDictionary(n => n.Id, n => nodes
            .Where(m => listens[m.Id].Count > 0 && CanStart(n, listens[m.Id], sameAutomation: m.Id == n.Id))
            .Select(m => m.Id)
            .ToList());
        var names = nodes.ToDictionary(n => n.Id, n => n.Name);

        var result = new Dictionary<Guid, string>();
        foreach (var node in nodes)
        {
            var path = ShortestPathBack(node.Id, successors);
            if (path is null)
                continue;

            result[node.Id] = string.Join(" → ", path.Select(id => names[id]));
        }

        return result;
    }

    internal static PowerModeState? TriggerPowerMode(IAutomationPipelineTrigger? trigger) => trigger switch
    {
        IPowerModeAutomationPipelineTrigger p => p.PowerModeState,
        AndAutomationPipelineTrigger and => and.Triggers.OfType<IPowerModeAutomationPipelineTrigger>()
            .Select(p => (PowerModeState?)p.PowerModeState)
            .FirstOrDefault(),
        _ => null
    };

    private static Guid? TriggerPreset(IAutomationPipelineTrigger? trigger) => trigger switch
    {
        IGodModePresetChangedAutomationPipelineTrigger p => p.PresetId,
        AndAutomationPipelineTrigger and => and.Triggers.OfType<IGodModePresetChangedAutomationPipelineTrigger>()
            .Select(p => (Guid?)p.PresetId)
            .FirstOrDefault(),
        _ => null
    };

    private static PowerModeState? ModeAtEnd(IAutomationPipelineTrigger? trigger, IReadOnlyList<StepInfo> steps)
    {
        var last = steps.LastOrDefault(s => s.Type == typeof(PowerModeAutomationStep) && s.State is PowerModeState);
        return last.State is PowerModeState mode ? mode : TriggerPowerMode(trigger);
    }

    private static int? OverriddenBy(IReadOnlyList<StepInfo> steps, int index, int lastReached)
    {
        var key = SettingKey(steps[index].Type);
        if (key is null || steps[index].RefersBack)
            return null;

        for (var j = index + 1; j <= lastReached; j++)
        {
            var later = steps[j];

            // A pause or a program in between can rely on the earlier value (for example a short blink).
            if (later.Type == typeof(DelayAutomationStep) || later.Type == typeof(RunAutomationStep))
                return null;

            if (SettingKey(later.Type) != key || later.RefersBack)
                continue;

            // A toggle or a cycle starts from the earlier value, so it doesn't replace it.
            if (later.State is ToggleState.Toggle || later.Type == typeof(CycleGodModePresetAutomationStep))
                return null;

            return j;
        }

        return null;
    }

    private static Type? SettingKey(Type stepType)
    {
        if (stepType == typeof(CycleGodModePresetAutomationStep))
            return typeof(GodModePresetAutomationStep);

        return SettingSteps.Contains(stepType) || IsFeatureStep(stepType) ? stepType : null;
    }

    private static bool IsFeatureStep(Type stepType)
    {
        for (var type = stepType.BaseType; type is not null; type = type.BaseType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(AbstractFeatureAutomationStep<>))
                return true;
        }

        return false;
    }

    private static List<string> Listens(IAutomationPipelineTrigger? trigger)
    {
        var triggers = trigger is ICompositeAutomationPipelineTrigger composite ? composite.Triggers : trigger is null ? [] : [trigger];
        var keys = new List<string>();
        foreach (var t in triggers)
        {
            if (t is IPowerModeAutomationPipelineTrigger p)
                keys.Add($"pm:{p.PowerModeState}");
            else if (t is IGodModePresetChangedAutomationPipelineTrigger g)
                keys.Add($"preset:{g.PresetId}");
        }

        return keys;
    }

    /// <summary>
    /// True when what <paramref name="node"/> sets can raise one of the events in <paramref name="listened"/>.
    /// A step only raises an event when it changes something: the mode or preset that started the automation
    /// is already set, and an automation that starts itself by setting a fixed mode or preset finds it already
    /// set the second time. Only cycling presets can keep starting the same automation.
    /// </summary>
    private static bool CanStart(LoopNode node, List<string> listened, bool sameAutomation)
    {
        if (node.Trigger is null)
            return false;

        var startedMode = TriggerPowerMode(node.Trigger);
        var lastMode = node.Steps.LastOrDefault(s => s.Type == typeof(PowerModeAutomationStep) && s.State is PowerModeState);
        if (!sameAutomation && lastMode.State is PowerModeState mode && mode != startedMode && listened.Contains($"pm:{mode}"))
            return true;

        var startedPreset = TriggerPreset(node.Trigger);
        foreach (var step in node.Steps)
        {
            if (!sameAutomation && step.Type == typeof(GodModePresetAutomationStep) && step.State is Guid preset && preset != startedPreset && listened.Contains($"preset:{preset}"))
                return true;

            if (step.Type == typeof(CycleGodModePresetAutomationStep)
                && listened.Any(key => key.StartsWith("preset:", StringComparison.Ordinal) && key != $"preset:{startedPreset}"))
                return true;
        }

        return false;
    }

    private static List<Guid>? ShortestPathBack(Guid start, Dictionary<Guid, List<Guid>> successors)
    {
        var previous = new Dictionary<Guid, Guid>();
        var queue = new Queue<Guid>();
        foreach (var next in successors[start])
        {
            if (next == start)
                return [start, start];

            if (previous.TryAdd(next, start))
                queue.Enqueue(next);
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var next in successors[current])
            {
                if (next == start)
                {
                    var path = new List<Guid> { start, current };
                    for (var at = current; previous[at] != start; at = previous[at])
                        path.Insert(1, previous[at]);
                    path.Add(start);
                    return path;
                }

                if (previous.TryAdd(next, current))
                    queue.Enqueue(next);
            }
        }

        return null;
    }
}
