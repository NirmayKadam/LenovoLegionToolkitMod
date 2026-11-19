using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Automation.Pipeline.Triggers;
using LenovoLegionToolkit.Lib.Automation.Resources;
using LenovoLegionToolkit.Lib.Automation.Steps;
using LenovoLegionToolkit.Lib.Automation.Utils;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Utils;
using Newtonsoft.Json;

namespace LenovoLegionToolkit.Lib.Automation.Pipeline;

public class AutomationPipeline
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string? IconName { get; set; }

    public string? Name { get; set; }

    public IAutomationPipelineTrigger? Trigger { get; set; }

    public List<IAutomationStep> Steps { get; init; } = [];

    public bool IsExclusive { get; set; } = true;

    public bool RunOnStartup { get; set; }

    [JsonIgnore]
    public IEnumerable<IAutomationPipelineTrigger> AllTriggers
    {
        get
        {
            if (Trigger is not null && Trigger is not ICompositeAutomationPipelineTrigger)
                yield return Trigger;

            if (Trigger is ICompositeAutomationPipelineTrigger compositeTrigger)
                foreach (var trigger in compositeTrigger.Triggers)
                    yield return trigger;
        }
    }

    public AutomationPipeline() { }

    public AutomationPipeline(string name) => Name = name;

    public AutomationPipeline(IAutomationPipelineTrigger trigger) => Trigger = trigger;

    internal async Task RunAsync(List<AutomationPipeline> otherPipelines, CancellationToken token = default)
    {
        if (token.IsCancellationRequested)
        {
            Log.Instance.Trace($"Pipeline interrupted.");
            return;
        }

        var context = new AutomationContext();
        var environment = new AutomationEnvironment();
        var stepExceptions = new List<Exception>();

        AllTriggers.ForEach(t => t.UpdateEnvironment(environment));

        foreach (var step in GetAllSteps(otherPipelines))
        {
            if (token.IsCancellationRequested)
            {
                Log.Instance.Trace($"Pipeline interrupted.");
                break;
            }

            if (environment.Startup && step.IsDangerousOnStartup)
            {
                Log.Instance.Trace($"Skipping dangerous step on startup. [type={step.GetType().Name}]");
                continue;
            }

            Log.Instance.Trace($"Running step... [type={step.GetType().Name}]");

            try
            {
                // A step this laptop doesn't support (for example from another laptop's backup) would only fail.
                if (!await step.IsSupportedAsync().ConfigureAwait(false))
                {
                    Log.Instance.Trace($"Skipping step not supported on this laptop. [type={step.GetType().Name}]");
                    continue;
                }

                await step.RunAsync(context, environment, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Interrupted by a newer event; not a step failure. The loop check above stops the pipeline.
                Log.Instance.Trace($"Step interrupted. [type={step.GetType().Name}]");
                continue;
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Step run failed. [name={step.GetType().Name}]", ex);

                stepExceptions.Add(ex);
            }

            Log.Instance.Trace($"Step completed successfully. [type={step.GetType().Name}]");
        }

        if (stepExceptions.Count != 0)
            throw new AggregateException(stepExceptions);
    }

    internal IEnumerable<IAutomationStep> GetAllSteps(List<AutomationPipeline> pipelines) => ExpandSteps(pipelines).Select(s => s.Step);

    /// <summary>
    /// This automation's steps with quick actions replaced by their steps (each quick action step itself stays
    /// in the list, as before; it does nothing when run). <see cref="ExpandedStep.OwnIndex"/> is the index of
    /// this automation's own step each entry came from. A quick action that refers back to one already being
    /// expanded is not expanded again (it used to recurse until the stack overflowed and LLT ended) and is
    /// marked <see cref="ExpandedStep.RefersBack"/>.
    /// </summary>
    internal List<ExpandedStep> ExpandSteps(IReadOnlyList<AutomationPipeline> pipelines)
    {
        var result = new List<ExpandedStep>();
        Expand(this, pipelines, [Id], null, result);
        return result;
    }

    private static void Expand(AutomationPipeline pipeline, IReadOnlyList<AutomationPipeline> pipelines, HashSet<Guid> expanding, int? ownIndex, List<ExpandedStep> result)
    {
        for (var i = 0; i < pipeline.Steps.Count; i++)
        {
            var step = pipeline.Steps[i];
            var index = ownIndex ?? i;
            var refersBack = false;

            if (step is QuickActionAutomationStep qas)
            {
                var matchingPipeline = pipelines.FirstOrDefault(p => p.Id == qas.PipelineId && p.AllTriggers.IsEmpty());
                if (matchingPipeline is null)
                    continue;

                if (expanding.Add(matchingPipeline.Id))
                {
                    Expand(matchingPipeline, pipelines, expanding, index, result);
                    expanding.Remove(matchingPipeline.Id);
                }
                else
                {
                    refersBack = true;
                }
            }

            result.Add(new(step, index, refersBack));
        }
    }

    public AutomationPipeline DeepCopy() => new()
    {
        Id = Id,
        IconName = IconName,
        Name = Name,
        Trigger = Trigger?.DeepCopy(),
        Steps = Steps.Select(s => s.DeepCopy()).ToList(),
        IsExclusive = IsExclusive,
        RunOnStartup = RunOnStartup,
    };

    /// <summary>
    /// Every warning for this automation: steps that can't do anything or that conflict (see <see cref="AutomationRules"/>),
    /// Run steps whose program others can replace, loops with <paramref name="pipelines"/>, and the step-order warnings.
    /// </summary>
    /// <param name="pipelines">All automations (this one may be among them); used to expand quick actions and find loops.</param>
    /// <param name="isSupported">Whether this laptop supports a step type; null skips that check.</param>
    public IEnumerable<string> GetValidationWarnings(IEnumerable<AutomationPipeline>? pipelines = null, Func<Type, bool>? isSupported = null)
    {
        var others = (pipelines ?? []).Where(p => p.Id != Id).ToList();
        var expanded = ExpandSteps(others);
        var steps = expanded.Select(s => s.Step).ToList();
        var infos = expanded.Select(s => StepInfo.From(s.Step, s.RefersBack)).ToList();

        foreach (var issue in AutomationRules.Check(Trigger, infos, isSupported))
            yield return FormatIssue(expanded[issue.StepIndex], issue.Message);

        foreach (var entry in expanded)
        {
            if (entry.Step is not RunAutomationStep run)
                continue;

            var message = RunTargetCheck.Describe(RunTargetCheck.Check(run.ScriptPath));
            if (message is not null)
                yield return FormatIssue(entry, message);
        }

        if (Trigger is not null)
        {
            var nodes = others.Append(this)
                .Select(p => new LoopNode(p.Id, p.Name ?? p.Trigger?.DisplayName ?? string.Empty, p.Trigger,
                    p.ExpandSteps(others.Append(this).Where(o => o.Id != p.Id).ToList())
                        .Select(s => StepInfo.From(s.Step, s.RefersBack))
                        .ToList()))
                .ToList();

            if (AutomationRules.FindLoops(nodes).TryGetValue(Id, out var loop))
                yield return string.Format(Resource.AutomationRule_Loop, loop);
        }

        foreach (var warning in StartupAndOrderWarnings(steps, infos))
            yield return warning;
    }

    /// <summary>Why a step of <paramref name="stepType"/> can't usefully be added at the end of this automation, or null when it can.</summary>
    public string? WhyStepCannotBeAdded(Type stepType, IEnumerable<AutomationPipeline>? pipelines = null)
    {
        var others = (pipelines ?? []).Where(p => p.Id != Id).ToList();
        var infos = ExpandSteps(others).Select(s => StepInfo.From(s.Step, s.RefersBack)).ToList();
        return AutomationRules.WhyNotAddable(stepType, Trigger, infos);
    }

    private string FormatIssue(ExpandedStep entry, string message)
    {
        var ownStep = Steps[entry.OwnIndex];
        return string.Format(Resource.AutomationRule_StepIssue,
            entry.OwnIndex + 1,
            AutomationTranslator.Translate(ownStep.GetType().Name),
            message);
    }

    private IEnumerable<string> StartupAndOrderWarnings(List<IAutomationStep> steps, List<StepInfo> infos)
    {
        if (RunOnStartup)
        {
            var dangerousSteps = steps.Where(s => s.IsDangerousOnStartup).ToList();
            if (dangerousSteps.Count > 0)
            {
                var stepNames = dangerousSteps
                    .Select(s => AutomationTranslator.Translate(s.GetType().Name))
                    .ToList();

                var stepList = string.Join(", ", stepNames);
                yield return string.Format(Lib.Resources.Resource.Automation_Warning_Startup, stepList);
            }
        }

        foreach (var warning in AutomationRules.OrderWarnings(infos))
            yield return warning;
    }
}

/// <summary>A step of an automation after quick actions are expanded; see <see cref="AutomationPipeline.ExpandSteps"/>.</summary>
internal readonly record struct ExpandedStep(IAutomationStep Step, int OwnIndex, bool RefersBack);
