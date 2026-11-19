using System;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Automation;
using LenovoLegionToolkit.Lib.Automation.Utils;
using Xunit;

namespace LenovoLegionToolkit.Tests.Automation;

// The guard with a clock the test moves forward.
public class AutomationLoopGuardTests
{
    private DateTime _now = new(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);
    private readonly AutomationLoopGuard _guard;
    private readonly Guid _pipeline = Guid.NewGuid();

    private static readonly IAutomationEvent PowerModeEvent = new PowerModeAutomationEvent(PowerModeState.Quiet);

    public AutomationLoopGuardTests() => _guard = new AutomationLoopGuard(() => _now);

    private void Advance(double seconds) => _now = _now.AddSeconds(seconds);

    /// <summary>An automation that can change the power mode runs and finishes; its change arrives as an event.</summary>
    private bool AutomationChangesTheModeAndTheEventArrives()
    {
        _guard.RunStarted();
        Advance(0.5);
        _guard.RunFinished();
        Advance(0.5);
        return _guard.IsSelfCaused(PowerModeEvent);
    }

    [Fact]
    public void AnEventWithNoAutomationJustBefore_IsNotSelfCaused() =>
        Assert.False(_guard.IsSelfCaused(PowerModeEvent));

    [Fact]
    public void AnEventDuringOrJustAfterAnAutomation_IsSelfCaused()
    {
        _guard.RunStarted();
        Assert.True(_guard.IsSelfCaused(PowerModeEvent));

        _guard.RunFinished();
        Advance(AutomationLoopGuard.CauseWindow.TotalSeconds);
        Assert.True(_guard.IsSelfCaused(PowerModeEvent));

        Advance(0.1);
        Assert.False(_guard.IsSelfCaused(PowerModeEvent));
    }

    [Fact]
    public void OtherEvents_NeverCount()
    {
        _guard.RunStarted();
        Assert.False(_guard.IsSelfCaused(new StartupAutomationEvent()));
    }

    [Fact]
    public void AnAutomationStartedByAutomationsTooOften_IsPaused()
    {
        for (var i = 0; i < AutomationLoopGuard.MaxSelfCausedStarts; i++)
        {
            Assert.True(AutomationChangesTheModeAndTheEventArrives());
            Assert.False(_guard.RecordSelfCausedStart(_pipeline));
            Assert.False(_guard.IsPaused(_pipeline));
        }

        Assert.True(AutomationChangesTheModeAndTheEventArrives());
        Assert.True(_guard.RecordSelfCausedStart(_pipeline));
        Assert.True(_guard.IsPaused(_pipeline));
        Assert.False(_guard.IsPaused(Guid.NewGuid()));
    }

    [Fact]
    public void StartsSpreadOverMoreThanTheWindow_DoNotPause()
    {
        for (var i = 0; i < 10; i++)
        {
            Assert.False(_guard.RecordSelfCausedStart(_pipeline));
            Advance(AutomationLoopGuard.CountWindow.TotalSeconds / AutomationLoopGuard.MaxSelfCausedStarts + 1);
        }

        Assert.False(_guard.IsPaused(_pipeline));
    }

    [Fact]
    public void SavingAgain_ResumesPausedAutomations()
    {
        for (var i = 0; i <= AutomationLoopGuard.MaxSelfCausedStarts; i++)
            _guard.RecordSelfCausedStart(_pipeline);
        Assert.True(_guard.IsPaused(_pipeline));

        _guard.ResumeAll();

        Assert.False(_guard.IsPaused(_pipeline));
        Assert.False(_guard.RecordSelfCausedStart(_pipeline));
    }
}
