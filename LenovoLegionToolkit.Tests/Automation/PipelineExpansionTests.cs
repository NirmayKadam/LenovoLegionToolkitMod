using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Automation;
using LenovoLegionToolkit.Lib.Automation.Pipeline;
using LenovoLegionToolkit.Lib.Automation.Steps;
using Xunit;

namespace LenovoLegionToolkit.Tests.Automation;

// Real pipelines with steps that need no hardware (Delay, quick action) plus one recording step.
public class PipelineExpansionTests
{
    /// <summary>A step that records whether it ran; supported or not as told.</summary>
    private sealed class RecordingStep(bool supported) : IAutomationStep
    {
        public bool Ran { get; private set; }
        public Task<bool> IsSupportedAsync() => Task.FromResult(supported);
        public Task RunAsync(AutomationContext context, AutomationEnvironment environment, CancellationToken token)
        {
            Ran = true;
            return Task.CompletedTask;
        }
        public IAutomationStep DeepCopy() => this;
    }

    private static AutomationPipeline QuickAction(string name, params IAutomationStep[] steps)
    {
        var pipeline = new AutomationPipeline(name);
        pipeline.Steps.AddRange(steps);
        return pipeline;
    }

    [Fact]
    public void QuickActionsThatRunEachOther_DoNotRecurse_AndTheRepeatIsMarked()
    {
        // A runs B; B and C run each other. The automation being run is never in its own list, but before the fix
        // B and C expanded each other until the stack overflowed, which ends the process (and the test run).
        var a = QuickAction("A");
        var b = QuickAction("B");
        var c = QuickAction("C");
        a.Steps.Add(new DelayAutomationStep(new Delay(1)));
        a.Steps.Add(new QuickActionAutomationStep(b.Id));
        b.Steps.Add(new QuickActionAutomationStep(c.Id));
        c.Steps.Add(new QuickActionAutomationStep(b.Id));

        var expanded = a.ExpandSteps([b, c]);

        // Delay; C's step back to B (not expanded again); B's step to C; A's step to B.
        Assert.Equal(4, expanded.Count);
        Assert.IsType<DelayAutomationStep>(expanded[0].Step);
        Assert.True(expanded[1].RefersBack);
        Assert.Equal([false, true, false, false], expanded.Select(s => s.RefersBack));
        Assert.All(expanded.Skip(1), s => Assert.Equal(1, s.OwnIndex));
        Assert.Contains(a.GetValidationWarnings([a, b, c]), w => w.Contains(LenovoLegionToolkit.Lib.Automation.Resources.Resource.AutomationRule_QuickActionCycle));
    }
    [Fact]
    public void TheSameQuickActionTwiceInARow_IsExpandedBothTimes()
    {
        var inner = QuickAction("Inner", new DelayAutomationStep(new Delay(1)));
        var outer = QuickAction("Outer", new QuickActionAutomationStep(inner.Id), new QuickActionAutomationStep(inner.Id));

        var expanded = outer.ExpandSteps([inner]);

        Assert.Equal(2, expanded.Count(s => s.Step is DelayAutomationStep));
        Assert.DoesNotContain(expanded, s => s.RefersBack);
    }

    [Fact]
    public async Task AStepThisLaptopDoesNotSupport_IsSkipped_AndTheRestRun()
    {
        var unsupported = new RecordingStep(supported: false);
        var supported = new RecordingStep(supported: true);
        var pipeline = QuickAction("Test", unsupported, supported);

        await pipeline.RunAsync([], CancellationToken.None);

        Assert.False(unsupported.Ran);
        Assert.True(supported.Ran);
    }

    [Fact]
    public void Warnings_NameTheStepNumber()
    {
        var pipeline = QuickAction("Test", new CloseAppAutomationStep(), new DelayAutomationStep(new Delay(1)));

        var warning = Assert.Single(pipeline.GetValidationWarnings([]));
        Assert.StartsWith(string.Format(LenovoLegionToolkit.Lib.Automation.Resources.Resource.AutomationRule_StepIssue, 2, "Delay", string.Empty), warning);
    }

}
