using System;
using System.Diagnostics;
using LenovoLegionToolkit.Lib.System;
using Xunit;

namespace LenovoLegionToolkit.Tests.System;

/// <summary>Whether a program runs, as AI mode asks for the firmware's list of games. Real process list.</summary>
public class RunningProcessesTests
{
    [Fact]
    public void ThisTestRun_IsFoundByItsName()
    {
        using var current = Process.GetCurrentProcess();
        Assert.True(RunningProcesses.IsRunning(current.ProcessName));
    }

    [Fact]
    public void AProgramThatIsNotRunning_IsNotFound() =>
        Assert.False(RunningProcesses.IsRunning("llt-not-running-" + Guid.NewGuid().ToString("N")));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void NoName_IsNeverRunning(string name) => Assert.False(RunningProcesses.IsRunning(name));
}
