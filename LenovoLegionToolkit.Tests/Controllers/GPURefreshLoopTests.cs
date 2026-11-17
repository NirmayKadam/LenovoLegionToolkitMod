using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers;
using Xunit;

namespace LenovoLegionToolkit.Tests.Controllers;

/// <summary>
/// The discrete GPU check runs in a background loop. NVIDIA's API can't be driven in a test, so the controller's
/// internal constructor takes the start-up step and the per-round refresh step; these tests supply them (they only
/// count calls or throw) to check the loop's own behaviour: it survives a failed round and it ends cleanly when it
/// can't start. Error reports go to a list instead of the real log folder.
/// </summary>
public class GPURefreshLoopTests
{
    private static async Task<bool> EventuallyAsync(Func<bool> condition)
    {
        for (var i = 0; i < 60 && !condition(); i++)
            await Task.Delay(50);
        return condition();
    }

    [Fact]
    public async Task OneFailingRound_DoesNotStopTheLoop_AndIsReported()
    {
        var rounds = 0;
        var reports = new List<Exception>();
        var controller = new GPUController(
            () => Task.CompletedTask,
            _ => Interlocked.Increment(ref rounds) == 1 ? throw new InvalidOperationException("driver busy") : Task.CompletedTask,
            (_, ex) => { lock (reports) reports.Add(ex); });

        await controller.StartAsync(delay: 0, interval: 20);
        try
        {
            Assert.True(await EventuallyAsync(() => Volatile.Read(ref rounds) >= 3), $"the loop stopped after {Volatile.Read(ref rounds)} round(s)");
            Assert.True(controller.IsStarted);
            lock (reports)
                Assert.Single(reports);
        }
        finally
        {
            await controller.StopAsync();
        }
    }

    [Fact]
    public async Task RepeatedFailures_AreReportedOncePerRun()
    {
        var rounds = 0;
        var reports = new List<Exception>();
        var controller = new GPUController(
            () => Task.CompletedTask,
            _ => { Interlocked.Increment(ref rounds); throw new InvalidOperationException("driver busy"); },
            (_, ex) => { lock (reports) reports.Add(ex); });

        await controller.StartAsync(delay: 0, interval: 20);
        try
        {
            Assert.True(await EventuallyAsync(() => Volatile.Read(ref rounds) >= 4), $"only {Volatile.Read(ref rounds)} round(s) ran");
            lock (reports)
                Assert.Single(reports);
        }
        finally
        {
            await controller.StopAsync();
        }
    }

    /// <summary>
    /// When the start-up step fails (for example, the NVIDIA driver isn't available), the loop must end and say
    /// it isn't running, so a later start can try again. Before, it stayed "started" with no loop behind it.
    /// </summary>
    [Fact]
    public async Task StartUpFailure_EndsCleanly_AndALaterStartWorks()
    {
        var starts = 0;
        var rounds = 0;
        var controller = new GPUController(
            () => Interlocked.Increment(ref starts) == 1 ? throw new InvalidOperationException("no driver") : Task.CompletedTask,
            _ => { Interlocked.Increment(ref rounds); return Task.CompletedTask; },
            (_, _) => { });

        await controller.StartAsync(delay: 0, interval: 20);
        Assert.True(await EventuallyAsync(() => !controller.IsStarted), "still marked as started after the start-up step failed");
        Assert.Equal(0, Volatile.Read(ref rounds));

        await controller.StartAsync(delay: 0, interval: 20);
        try
        {
            Assert.True(await EventuallyAsync(() => Volatile.Read(ref rounds) > 0), "the second start didn't run");
            Assert.True(controller.IsStarted);
        }
        finally
        {
            await controller.StopAsync();
        }
    }
}
