using System;
using System.Collections.Generic;
using System.Linq;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Automation.Pipeline;
using LenovoLegionToolkit.Lib.Automation.Pipeline.Triggers;
using LenovoLegionToolkit.Lib.Automation.Resources;
using LenovoLegionToolkit.Lib.Automation.Steps;
using Xunit;

namespace LenovoLegionToolkit.Tests.Automation;

// The rules only look at step types and states, so steps are described with StepInfo (real steps resolve hardware
// features in their constructors). Triggers are the real trigger classes.
public class AutomationRulesTests
{
    private static StepInfo PowerMode(PowerModeState state) => new(typeof(PowerModeAutomationStep), state);
    private static StepInfo AiEngine(ToggleState state) => new(typeof(AIEngineAutomationStep), state);
    private static StepInfo Of<T>(object? state = null) => new(typeof(T), state);
    private static PowerModeAutomationPipelineTrigger When(PowerModeState state) => new(state);

    private static List<StepIssue> Check(IAutomationPipelineTrigger? trigger, params StepInfo[] steps) =>
        [.. AutomationRules.Check(trigger, steps)];

    // ---- AI Engine ----

    [Theory]
    [InlineData(PowerModeState.Performance)]
    [InlineData(PowerModeState.Quiet)]
    [InlineData(PowerModeState.GodMode)]
    public void AiEngineOn_InAutomationStartedByAnotherMode_HasNoEffect(PowerModeState mode)
    {
        var issue = Assert.Single(Check(When(mode), AiEngine(ToggleState.On)));
        Assert.Equal(0, issue.StepIndex);
        Assert.Equal(StepIssueKind.NoEffect, issue.Kind);
        Assert.Equal(Resource.AutomationRule_AIEngineNotBalance, issue.Message);
    }

    [Fact]
    public void AiEngine_InBalanceAutomation_IsFine() =>
        Assert.Empty(Check(When(PowerModeState.Balance), AiEngine(ToggleState.On)));

    [Fact]
    public void AiEngine_AfterAPowerModeStepToBalance_IsFine() =>
        Assert.DoesNotContain(Check(When(PowerModeState.Performance), PowerMode(PowerModeState.Balance), AiEngine(ToggleState.On)),
            i => i.Kind == StepIssueKind.NoEffect && i.StepIndex == 1);

    [Fact]
    public void AiEngine_AfterAPowerModeStepToPerformance_HasNoEffect_EvenWithoutAPowerModeTrigger()
    {
        var issues = Check(new ACAdapterConnectedAutomationPipelineTrigger(), PowerMode(PowerModeState.Performance), AiEngine(ToggleState.Toggle));
        Assert.Contains(issues, i => i.StepIndex == 1 && i.Kind == StepIssueKind.NoEffect);
    }

    [Fact]
    public void AiEngineOff_IsNeverFlagged() =>
        Assert.Empty(Check(When(PowerModeState.Performance), AiEngine(ToggleState.Off)));

    [Fact]
    public void AiEngine_WhenTheModeIsUnknown_IsNotFlagged() =>
        Assert.Empty(Check(new ACAdapterConnectedAutomationPipelineTrigger(), AiEngine(ToggleState.On)));

    [Fact]
    public void AiEngine_InAnAndTriggerWithPerformance_HasNoEffect()
    {
        var trigger = new AndAutomationPipelineTrigger([new ACAdapterConnectedAutomationPipelineTrigger(), When(PowerModeState.Performance)]);
        Assert.Contains(Check(trigger, AiEngine(ToggleState.On)), i => i.Kind == StepIssueKind.NoEffect);
    }

    [Fact]
    public void AiEngine_CannotBeAdded_ToAPerformanceAutomation_ButCanToBalance()
    {
        Assert.NotNull(AutomationRules.WhyNotAddable(typeof(AIEngineAutomationStep), When(PowerModeState.Performance), []));
        Assert.Null(AutomationRules.WhyNotAddable(typeof(AIEngineAutomationStep), When(PowerModeState.Balance), []));
        Assert.Null(AutomationRules.WhyNotAddable(typeof(AIEngineAutomationStep), new ACAdapterConnectedAutomationPipelineTrigger(), []));
        Assert.Null(AutomationRules.WhyNotAddable(typeof(DelayAutomationStep), When(PowerModeState.Performance), []));
    }

    [Fact]
    public void AiEngine_CanBeAdded_AfterAStepThatSwitchesToBalance() =>
        Assert.Null(AutomationRules.WhyNotAddable(typeof(AIEngineAutomationStep), When(PowerModeState.Performance), [PowerMode(PowerModeState.Balance)]));

    // ---- Close app ----

    [Fact]
    public void StepsAfterCloseApp_NeverRun_AndNothingCanBeAdded()
    {
        var issues = Check(null, Of<DelayAutomationStep>(), Of<CloseAppAutomationStep>(), Of<DelayAutomationStep>(), Of<NotificationAutomationStep>());

        Assert.Equal([2, 3], issues.Where(i => i.Kind == StepIssueKind.NeverRuns).Select(i => i.StepIndex));
        Assert.DoesNotContain(issues, i => i.StepIndex is 0 or 1);
        Assert.NotNull(AutomationRules.WhyNotAddable(typeof(DelayAutomationStep), null, [Of<CloseAppAutomationStep>()]));
    }

    // ---- Overridden settings ----

    [Fact]
    public void TheSameSettingTwice_MarksTheEarlierOne()
    {
        var issues = Check(null, PowerMode(PowerModeState.Quiet), PowerMode(PowerModeState.Performance));

        var issue = Assert.Single(issues);
        Assert.Equal(0, issue.StepIndex);
        Assert.Equal(StepIssueKind.Overridden, issue.Kind);
        Assert.Equal(string.Format(Resource.AutomationRule_Overridden, 2), issue.Message);
    }

    [Fact]
    public void FeatureSteps_AreSettingsToo() =>
        Assert.Single(Check(null, Of<OverDriveAutomationStep>(), Of<OverDriveAutomationStep>()), i => i.Kind == StepIssueKind.Overridden);

    [Fact]
    public void AToggleAfterASetting_DoesNotOverrideIt() =>
        Assert.Empty(Check(null, AiEngine(ToggleState.On), AiEngine(ToggleState.Toggle)));

    [Fact]
    public void ASettingAfterAToggle_OverridesTheToggle() =>
        Assert.Single(Check(null, AiEngine(ToggleState.Toggle), AiEngine(ToggleState.On)), i => i.StepIndex == 0 && i.Kind == StepIssueKind.Overridden);

    [Fact]
    public void ADelayOrProgramInBetween_KeepsBothSteps()
    {
        Assert.Empty(Check(null, Of<OverDriveAutomationStep>(), Of<DelayAutomationStep>(), Of<OverDriveAutomationStep>()));
        Assert.Empty(Check(null, Of<OverDriveAutomationStep>(), Of<RunAutomationStep>(), Of<OverDriveAutomationStep>()));
    }

    [Fact]
    public void ActionsThatAreNotSettings_AreNeverOverridden() =>
        Assert.Empty(Check(null, Of<NotificationAutomationStep>(), Of<NotificationAutomationStep>(), Of<DelayAutomationStep>(), Of<DelayAutomationStep>()));

    [Fact]
    public void ACycleAfterAPreset_DoesNotOverrideIt_ButAPresetAfterACycleDoes()
    {
        Assert.Empty(Check(null, Of<GodModePresetAutomationStep>(Guid.NewGuid()), Of<CycleGodModePresetAutomationStep>()));
        Assert.Single(Check(null, Of<CycleGodModePresetAutomationStep>(), Of<GodModePresetAutomationStep>(Guid.NewGuid())),
            i => i.StepIndex == 0 && i.Kind == StepIssueKind.Overridden);
    }

    // ---- Power mode step vs trigger ----

    [Fact]
    public void PowerModeStep_EqualToTheTrigger_DoesNothing() =>
        Assert.Single(Check(When(PowerModeState.Performance), PowerMode(PowerModeState.Performance)),
            i => i.Kind == StepIssueKind.NoEffect && i.Message == Resource.AutomationRule_AlreadyInMode);

    [Fact]
    public void PowerModeStep_DifferentFromTheTrigger_IsPointedOut() =>
        Assert.Single(Check(When(PowerModeState.Performance), PowerMode(PowerModeState.Quiet)),
            i => i.Kind == StepIssueKind.Info && i.Message == Resource.AutomationRule_SwitchesAway);

    [Fact]
    public void PowerModeStep_WithoutAPowerModeTrigger_IsNotPointedOut() =>
        Assert.Empty(Check(new ACAdapterConnectedAutomationPipelineTrigger(), PowerMode(PowerModeState.Performance)));

    // ---- Custom presets ----

    [Fact]
    public void Preset_InAnAutomationThatIsNotInCustomMode_IsPointedOut()
    {
        Assert.Single(Check(When(PowerModeState.Balance), Of<GodModePresetAutomationStep>(Guid.NewGuid())),
            i => i.Kind == StepIssueKind.Info && i.Message == Resource.AutomationRule_PresetNeedsCustom);
        Assert.Empty(Check(When(PowerModeState.GodMode), Of<GodModePresetAutomationStep>(Guid.NewGuid())));
        Assert.Empty(Check(new ACAdapterConnectedAutomationPipelineTrigger(), Of<CycleGodModePresetAutomationStep>()));
    }

    // ---- Unsupported steps and quick action cycles ----

    [Fact]
    public void UnsupportedSteps_AreMarked()
    {
        var issues = AutomationRules.Check(null, [Of<OverDriveAutomationStep>(), Of<DelayAutomationStep>()], t => t != typeof(OverDriveAutomationStep));
        var issue = Assert.Single(issues);
        Assert.Equal((0, StepIssueKind.NotSupported), (issue.StepIndex, issue.Kind));
    }

    [Fact]
    public void AQuickActionThatRefersBack_IsMarked() =>
        Assert.Single(Check(null, new StepInfo(typeof(QuickActionAutomationStep), null, RefersBack: true)),
            i => i.Kind == StepIssueKind.QuickActionCycle);

    // ---- The step-order warnings LLT always had ----

    [Fact]
    public void OrderWarnings_AreUnchanged()
    {
        Assert.Equal([Resource.AutomationPipeline_Warning_Power_Mode_Visual],
            AutomationRules.OrderWarnings([Of<DisplayBrightnessAutomationStep>(50), PowerMode(PowerModeState.Quiet)]));
        Assert.Equal([Resource.AutomationPipeline_Warning_Power_Mode_Fan],
            AutomationRules.OrderWarnings([Of<FanMaxSpeedAutomationStep>(ToggleState.On), PowerMode(PowerModeState.Quiet)]));
        Assert.Equal([Resource.AutomationPipeline_Warning_Hybrid_Mode],
            AutomationRules.OrderWarnings([Of<HybridModeAutomationStep>(), Of<DelayAutomationStep>()]));
        Assert.Equal([Resource.AutomationPipeline_Warning_GPU_OC],
            AutomationRules.OrderWarnings([Of<DeactivateGPUAutomationStep>(), Of<OverclockDiscreteGPUAutomationStep>(ToggleState.On)]));
        Assert.Equal([Resource.AutomationPipeline_Warning_HDR],
            AutomationRules.OrderWarnings([Of<HDRAutomationStep>(), Of<DisplayBrightnessAutomationStep>(50)]));
        Assert.Equal([Resource.AutomationPipeline_Warning_Resolution],
            AutomationRules.OrderWarnings([Of<RefreshRateAutomationStep>(), Of<ResolutionAutomationStep>()]));

        Assert.Empty(AutomationRules.OrderWarnings([PowerMode(PowerModeState.Quiet), Of<DisplayBrightnessAutomationStep>(50), Of<FanMaxSpeedAutomationStep>(ToggleState.On)]));
        Assert.Empty(AutomationRules.OrderWarnings([Of<ResolutionAutomationStep>(), Of<RefreshRateAutomationStep>(), Of<HybridModeAutomationStep>()]));
    }

    // ---- Loops between automations ----

    private static LoopNode Node(string name, IAutomationPipelineTrigger? trigger, params StepInfo[] steps) => new(Guid.NewGuid(), name, trigger, steps);

    [Fact]
    public void TwoAutomationsSwitchingEachOthersMode_AreALoop()
    {
        var a = Node("A", When(PowerModeState.Performance), PowerMode(PowerModeState.Quiet));
        var b = Node("B", When(PowerModeState.Quiet), PowerMode(PowerModeState.Performance));

        var loops = AutomationRules.FindLoops([a, b]);

        Assert.Equal("A → B → A", loops[a.Id]);
        Assert.Equal("B → A → B", loops[b.Id]);
    }

    [Fact]
    public void AChainThatEnds_IsNotALoop()
    {
        var onAc = Node("On AC", new ACAdapterConnectedAutomationPipelineTrigger(), PowerMode(PowerModeState.Performance));
        var afterburner = Node("Afterburner", When(PowerModeState.Performance), Of<RunAutomationStep>());
        var quiet = Node("Quiet", When(PowerModeState.Quiet), Of<RunAutomationStep>());

        Assert.Empty(AutomationRules.FindLoops([onAc, afterburner, quiet]));
    }

    [Fact]
    public void AnAutomationSettingTheModeThatStartedIt_IsNotALoop()
    {
        var self = Node("Self", new OrAutomationPipelineTrigger([When(PowerModeState.Quiet), When(PowerModeState.Performance)]), PowerMode(PowerModeState.Quiet));
        Assert.Empty(AutomationRules.FindLoops([self]));
    }

    [Fact]
    public void ThreeAutomationsInACircle_AreALoop()
    {
        var a = Node("A", When(PowerModeState.Quiet), PowerMode(PowerModeState.Balance));
        var b = Node("B", When(PowerModeState.Balance), PowerMode(PowerModeState.Performance));
        var c = Node("C", When(PowerModeState.Performance), PowerMode(PowerModeState.Quiet));

        Assert.Equal("A → B → C → A", AutomationRules.FindLoops([a, b, c])[a.Id]);
    }

    [Fact]
    public void PresetAutomationsThatCycleIntoEachOther_AreALoop()
    {
        var p1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();
        var a = Node("A", new GodModePresetChangedAutomationPipelineTrigger(p1), Of<CycleGodModePresetAutomationStep>());
        var b = Node("B", new GodModePresetChangedAutomationPipelineTrigger(p2), Of<CycleGodModePresetAutomationStep>());

        var loops = AutomationRules.FindLoops([a, b]);
        Assert.True(loops.ContainsKey(a.Id));
        Assert.True(loops.ContainsKey(b.Id));
    }

    [Fact]
    public void OneAutomationCyclingAwayFromItsOwnPreset_IsNotALoop()
    {
        var a = Node("A", new GodModePresetChangedAutomationPipelineTrigger(Guid.NewGuid()), Of<CycleGodModePresetAutomationStep>());
        Assert.Empty(AutomationRules.FindLoops([a]));
    }

    [Fact]
    public void ManualAutomations_NeverLoop()
    {
        var manual = Node("Manual", null, PowerMode(PowerModeState.Quiet));
        var quiet = Node("Quiet", When(PowerModeState.Quiet), PowerMode(PowerModeState.Performance));
        Assert.Empty(AutomationRules.FindLoops([manual, quiet]));
    }
}
