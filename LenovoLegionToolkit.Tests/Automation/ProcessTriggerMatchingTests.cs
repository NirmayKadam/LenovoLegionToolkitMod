using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.AutoListeners;
using LenovoLegionToolkit.Lib.Automation;
using LenovoLegionToolkit.Lib.Automation.Pipeline.Triggers;
using Xunit;

namespace LenovoLegionToolkit.Tests.Automation;

/// <summary>
/// "When a program starts / stops" triggers. Windows' start event cuts long names to 15 characters, and names were
/// compared case-sensitively, so triggers on programs like RobloxPlayerBeta never fired. The real triggers are used;
/// the programs named here don't run on any test machine.
/// </summary>
public class ProcessTriggerMatchingTests
{
    private static readonly string NeverRunning = "LltNoSuchProgramForTests" + Guid.NewGuid().ToString("N")[..8];

    [Theory]
    [InlineData("RobloxPlayerBet")]   // as the start event reports RobloxPlayerBeta.exe
    [InlineData("robloxplayerbeta")]  // another case
    [InlineData("RobloxPlayerBeta")]
    public void ACutOrDifferentlyCasedName_IsTheListedProgram(string reported) =>
        Assert.True(ProcessTriggerMatching.IsListed([new ProcessInfo("RobloxPlayerBeta", @"C:\Roblox\RobloxPlayerBeta.exe")], new ProcessInfo(reported, null)));

    [Theory]
    [InlineData("RobloxPlayer")]   // shorter, but not what a cut name looks like
    [InlineData("Discord")]
    public void OtherPrograms_AreNotListed(string reported) =>
        Assert.False(ProcessTriggerMatching.IsListed([new ProcessInfo("RobloxPlayerBeta", null)], new ProcessInfo(reported, null)));

    [Fact]
    public async Task TheStopTrigger_FiresForACutName_WhenNothingListedStillRuns()
    {
        var listed = new ProcessInfo(NeverRunning, null);
        var trigger = new ProcessesStopRunningAutomationPipelineTrigger([listed]);
        var cut = new ProcessInfo((NeverRunning + ".exe")[..15].Replace(".exe", string.Empty), null);

        Assert.True(await trigger.IsMatchingEvent(new ProcessAutomationEvent(ProcessEventInfoType.Stopped, cut)));
        Assert.False(await trigger.IsMatchingEvent(new ProcessAutomationEvent(ProcessEventInfoType.Stopped, new ProcessInfo("Discord", null))));
    }

    [Fact]
    public async Task TheStartTrigger_FiresOnlyWhileTheProgramRuns()
    {
        // The test host itself is a program that runs now.
        var running = new ProcessInfo(global::System.Diagnostics.Process.GetCurrentProcess().ProcessName, null);
        var startTrigger = new ProcessesAreRunningAutomationPipelineTrigger([running]);
        var notRunningTrigger = new ProcessesAreRunningAutomationPipelineTrigger([new ProcessInfo(NeverRunning, null)]);

        Assert.True(await startTrigger.IsMatchingEvent(new ProcessAutomationEvent(ProcessEventInfoType.Started, running)));
        Assert.False(await notRunningTrigger.IsMatchingEvent(new ProcessAutomationEvent(ProcessEventInfoType.Started, new ProcessInfo(NeverRunning, null))));
    }

    // ---- The process cache clean-up ----

    [Theory]
    [InlineData(10, 250)]    // a small cache: the usual limit
    [InlineData(249, 498)]
    [InlineData(400, 800)]   // 400 programs really running: the next clean-up waits for 800, not for the very next event
    public void TheNextCleanUp_GrowsWithWhatIsLeft(int left, int nextAt) =>
        Assert.Equal(nextAt, ProcessAutoListener.NextCleanUpAt(left));
}
