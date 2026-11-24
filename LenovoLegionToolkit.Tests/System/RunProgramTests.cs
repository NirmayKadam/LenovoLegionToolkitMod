using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.System;
using Xunit;

namespace LenovoLegionToolkit.Tests.System;

/// <summary>
/// Running a program and waiting for it (automation "Run" steps), with real Windows programs: cmd.exe and ping.exe.
/// </summary>
public class RunProgramTests
{
    private static readonly string Cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [Fact]
    public async Task AProgramThatStartsAnotherAndEnds_IsNotWaitedForUntilTheOtherEnds()
    {
        // cmd starts ping in the background (it inherits cmd's output) and ends at once; ping keeps the output open for
        // about 8 s. Before, the wait lasted until ping ended, holding up every other automation meanwhile.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var watch = Stopwatch.StartNew();

        var (exitCode, output) = await CMD.RunAsync(Cmd, "/c \"start /b ping -n 9 127.0.0.1 & echo started\"", token: cts.Token);

        watch.Stop();
        Assert.Equal(0, exitCode);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(6), $"Waited {watch.Elapsed.TotalSeconds:0.0} s for the program it started.");
        Assert.NotNull(output);
    }

    [Fact]
    public async Task AProgramThatEnds_GivesItsWholeOutput()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var (exitCode, output) = await CMD.RunAsync(Cmd, "/c echo first& echo second& exit 3", token: cts.Token);

        Assert.Equal(3, exitCode);
        Assert.Contains("first", output);
        Assert.Contains("second", output);
    }
}
