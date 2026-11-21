using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.System;
using Xunit;

namespace LenovoLegionToolkit.Tests.System;

// These tests start real cmd.exe processes.
public class CmdTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task LargeOutput_DoesNotDeadlock_AndIsReturnedInFull()
    {
        // ~60 KB of stdout, well above the ~4 KB pipe buffer that made the old code hang.
        using var cts = new CancellationTokenSource(Timeout);

        var (exitCode, output) = await CMD.RunAsync("cmd.exe", "/c for /L %i in (1,1,5000) do @echo line %i", token: cts.Token);

        Assert.Equal(0, exitCode);
        var lines = output.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(5000, lines.Length);
        Assert.Equal("line 1", lines.First());
        Assert.Equal("line 5000", lines.Last());
    }

    [Fact]
    public async Task LargeStderr_DoesNotDeadlock()
    {
        // The old code never read stderr at all.
        using var cts = new CancellationTokenSource(Timeout);

        var (exitCode, output) = await CMD.RunAsync("cmd.exe", "/c (for /L %i in (1,1,5000) do @echo err %i 1>&2) & echo done", token: cts.Token);

        Assert.Equal(0, exitCode);
        Assert.Equal("done", output.Trim());
    }

    [Fact]
    public async Task ExitCode_IsReturned()
    {
        using var cts = new CancellationTokenSource(Timeout);

        var (exitCode, _) = await CMD.RunAsync("cmd.exe", "/c exit 7", token: cts.Token);

        Assert.Equal(7, exitCode);
    }

    [Fact]
    public async Task NoWait_ReturnsImmediately()
    {
        using var cts = new CancellationTokenSource(Timeout);

        var (exitCode, output) = await CMD.RunAsync("cmd.exe", "/c for /L %i in (1,1,5000) do @echo line %i", waitForExit: false, token: cts.Token);

        Assert.Equal(-1, exitCode);
        Assert.Equal(string.Empty, output);
    }

    // A ping to an address used only by this test, so the process can be found again by its command line.
    private static int CountPings(string target) =>
        WmiCount($"SELECT ProcessId FROM Win32_Process WHERE Name = 'PING.EXE' AND CommandLine LIKE '%{target}%'");

    private static int WmiCount(string query)
    {
        using var searcher = new global::System.Management.ManagementObjectSearcher(query);
        using var results = searcher.Get();
        return results.Count;
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan limit)
    {
        var deadline = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            await Task.Delay(100);
        }
        return condition();
    }

    [Fact]
    public async Task EndOnCancel_EndsTheProgram_WhenTheWaitIsCancelled()
    {
        // Used by the boot logo's bcdedit/mountvol runs: a tool that hangs is ended, not left running.
        const string target = "127.0.0.71";
        using var cts = new CancellationTokenSource();
        var run = CMD.RunAsync("cmd.exe", $"/c ping -n 60 {target}", token: cts.Token, endOnCancel: true);

        Assert.True(await WaitUntilAsync(() => CountPings(target) == 1, Timeout), "ping didn't start");
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.True(await WaitUntilAsync(() => CountPings(target) == 0, Timeout), "ping is still running");
    }

    [Fact]
    public async Task WithoutEndOnCancel_TheProgramKeepsRunning_AsARunStepsProgramAlwaysHas()
    {
        const string target = "127.0.0.72";
        using var cts = new CancellationTokenSource();
        var run = CMD.RunAsync("cmd.exe", $"/c ping -n 60 {target}", token: cts.Token);

        try
        {
            Assert.True(await WaitUntilAsync(() => CountPings(target) == 1, Timeout), "ping didn't start");
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
            await Task.Delay(500);
            Assert.Equal(1, CountPings(target));
        }
        finally
        {
            // Clean up after the test: end the ping it left running on purpose.
            foreach (var process in global::System.Diagnostics.Process.GetProcessesByName("PING"))
            {
                using (process)
                {
                    try
                    {
                        using var searcher = new global::System.Management.ManagementObjectSearcher(
                            $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {process.Id}");
                        using var results = searcher.Get();
                        foreach (var item in results)
                        {
                            using (item)
                            {
                                if ((item["CommandLine"] as string)?.Contains(target) == true)
                                    process.Kill();
                            }
                        }
                    }
                    catch (InvalidOperationException) { /* already gone */ }
                }
            }
        }
    }
}
