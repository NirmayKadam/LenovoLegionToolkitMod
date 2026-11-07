using LenovoLegionToolkit.Lib.Automation.Steps;
using Xunit;

namespace LenovoLegionToolkit.Tests.Automation;

public class RunAutomationStepTests
{
    [Fact]
    public void System32Path_IsNotMatchedBySystemProcess()
    {
        // The old Contains() check treated anything under System32 as running, because "System" always runs.
        Assert.False(RunAutomationStep.IsRunning(@"C:\Windows\System32\notepad.exe", ["System", "Idle", "explorer", "svchost"]));
    }

    [Fact]
    public void RunningExecutable_IsMatched()
    {
        Assert.True(RunAutomationStep.IsRunning(@"C:\Windows\System32\notepad.exe", ["System", "notepad"]));
    }

    [Fact]
    public void Match_IgnoresCase()
    {
        Assert.True(RunAutomationStep.IsRunning(@"C:\Apps\MyApp.EXE", ["myapp"]));
    }

    [Fact]
    public void QuotedPath_IsHandled()
    {
        Assert.True(RunAutomationStep.IsRunning("\"C:\\Program Files\\Steam\\steam.exe\"", ["steam"]));
    }

    [Fact]
    public void BareName_IsHandled()
    {
        Assert.True(RunAutomationStep.IsRunning("notepad", ["notepad"]));
    }

    [Fact]
    public void ProcessNameThatIsOnlyPartOfPath_IsNotMatched()
    {
        Assert.False(RunAutomationStep.IsRunning(@"C:\Code\scripts\backup.bat", ["Code", "scripts"]));
    }

    [Fact]
    public void EmptyPath_IsNotRunning()
    {
        Assert.False(RunAutomationStep.IsRunning("   ", ["System"]));
    }
}
