using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers;
using Xunit;

namespace LenovoLegionToolkit.Tests.Controllers;

/// <summary>
/// The discrete GPU check runs in a background loop. NVIDIA's API can't be driven in a test, so the controller's
/// internal constructor takes the start-up step and the per-round refresh step; these tests supply them (they only
/// count calls or throw) to check the loop's own behaviour: it runs only while something holds it, it survives a
/// failed round, and it ends cleanly when it can't start. Rounds are 20 ms apart and a reader's hold lasts 300 ms
/// so the tests run quickly. Error reports go to a list instead of the real log folder.
/// </summary>
public class GPURefreshLoopTests
{
    private static readonly TimeSpan ReaderHold = TimeSpan.FromMilliseconds(300);

    private static GPUController Create(Func<Task> initialize, Func<CancellationToken, Task> refresh, Action<string, Exception> reportError, int startupDelayMs = 0) =>
        new(initialize, refresh, reportError, () => 20, startupDelayMs, ReaderHold);

    private static GPUController Counting(Action onRound, int startupDelayMs = 0) =>
        Create(() => Task.CompletedTask, _ => { onRound(); return Task.CompletedTask; }, (_, _) => { }, startupDelayMs);

    private static async Task<bool> EventuallyAsync(Func<bool> condition)
    {
        for (var i = 0; i < 60 && !condition(); i++)
            await Task.Delay(50);
        return condition();
    }

    // ---- Runs only while something needs it ----

    /// <summary>
    /// Before, nothing ever stopped the check once something had started it, so it kept running in the tray.
    /// After the last holder lets go it must stop by itself, and no further rounds run.
    /// </summary>
    [Fact]
    public async Task AfterTheLastHolderReleases_TheLoopStopsByItself()
    {
        var rounds = 0;
        var controller = Counting(() => Interlocked.Increment(ref rounds));
        var card = new object();

        controller.Hold(card);
        Assert.True(await EventuallyAsync(() => Volatile.Read(ref rounds) >= 2), "the loop didn't run while held");

        controller.Release(card);
        Assert.True(await EventuallyAsync(() => !controller.IsStarted), "the loop kept running with nothing holding it");

        var after = Volatile.Read(ref rounds);
        await Task.Delay(200);
        Assert.Equal(after, Volatile.Read(ref rounds));
    }

    /// <summary>
    /// The dashboard card and game detection both need the check. One of them letting go must not stop it for the
    /// other (before, the card had to skip stopping it whenever game detection might use it).
    /// </summary>
    [Fact]
    public async Task TwoHolders_ReleasingOneKeepsItRunning()
    {
        var rounds = 0;
        var controller = Counting(() => Interlocked.Increment(ref rounds));
        var card = new object();
        var gameDetection = new object();

        controller.Hold(card);
        controller.Hold(gameDetection);
        controller.Release(card);

        var before = Volatile.Read(ref rounds);
        Assert.True(await EventuallyAsync(() => Volatile.Read(ref rounds) >= before + 3), "the loop stopped while game detection still held it");
        Assert.True(controller.IsStarted);

        controller.Release(gameDetection);
        Assert.True(await EventuallyAsync(() => !controller.IsStarted));
    }

    /// <summary>
    /// Sensor reads hold the check only for a while: as long as they keep reading it runs, and once they stop it
    /// stops after the hold time.
    /// </summary>
    [Fact]
    public async Task ReaderHold_LastsWhileReading_AndExpiresAfterwards()
    {
        var controller = Counting(() => { });
        var sensors = new object();

        for (var i = 0; i < 12; i++) // 600 ms of reads, twice the hold time
        {
            await controller.GetFreshStateAsync(sensors);
            Assert.True(controller.IsStarted, $"stopped while still being read (read {i})");
            await Task.Delay(50);
        }

        Assert.True(await EventuallyAsync(() => !controller.IsStarted), "the reader's hold never expired");
    }

    /// <summary>
    /// A reader must never act on a state from before the check stopped (that could make the sensors query a
    /// sleeping GPU, or skip an awake one). When the check isn't running, the read refreshes first, even though
    /// the loop itself waits for its start-up delay (1 s by default; 500 ms here).
    /// </summary>
    [Fact]
    public async Task ReaderOnAStoppedCheck_RefreshesBeforeAnswering()
    {
        var rounds = 0;
        var controller = Counting(() => Interlocked.Increment(ref rounds), startupDelayMs: 500);
        Assert.False(controller.IsStarted);

        var state = await controller.GetFreshStateAsync(new object());

        Assert.True(Volatile.Read(ref rounds) >= 1, "answered without refreshing");
        Assert.Equal(GPUState.Unknown, state); // the supplied refresh doesn't set a state
        Assert.True(controller.IsStarted);
    }

    /// <summary>
    /// A hold that arrives while the loop is deciding to stop must leave it running (or start a new one), never
    /// leave it stopped. Rounds are 20 ms apart, so many release/hold pairs land on the decision.
    /// </summary>
    [Fact]
    public async Task HoldDuringTheStopDecision_AlwaysEndsRunning()
    {
        var rounds = 0;
        var controller = Counting(() => Interlocked.Increment(ref rounds));
        var holder = new object();

        controller.Hold(holder);
        for (var i = 0; i < 200; i++)
        {
            controller.Release(holder);
            await Task.Delay(i % 7);
            controller.Hold(holder);
        }

        var before = Volatile.Read(ref rounds);
        Assert.True(await EventuallyAsync(() => Volatile.Read(ref rounds) >= before + 3), "held, but no rounds ran");
        Assert.True(controller.IsStarted);
        controller.Release(holder);
        Assert.True(await EventuallyAsync(() => !controller.IsStarted));
    }

    [Fact]
    public async Task ReaderWhoseRefreshFails_GetsUnknown_WithoutAnError()
    {
        var controller = Create(() => Task.CompletedTask, _ => throw new InvalidOperationException("driver busy"), (_, _) => { });

        var error = await Record.ExceptionAsync(() => controller.GetFreshStateAsync(new object()));

        Assert.Null(error);
        Assert.Equal(GPUState.Unknown, await controller.GetFreshStateAsync(new object()));
        Assert.True(await EventuallyAsync(() => !controller.IsStarted));
    }

    // ---- Errors ----

    [Fact]
    public async Task OneFailingRound_DoesNotStopTheLoop_AndIsReported()
    {
        var rounds = 0;
        var reports = new List<Exception>();
        var controller = Create(
            () => Task.CompletedTask,
            _ => Interlocked.Increment(ref rounds) == 1 ? throw new InvalidOperationException("driver busy") : Task.CompletedTask,
            (_, ex) => { lock (reports) reports.Add(ex); });
        var holder = new object();

        controller.Hold(holder);
        try
        {
            Assert.True(await EventuallyAsync(() => Volatile.Read(ref rounds) >= 3), $"the loop stopped after {Volatile.Read(ref rounds)} round(s)");
            Assert.True(controller.IsStarted);
            lock (reports)
                Assert.Single(reports);
        }
        finally
        {
            controller.Release(holder);
        }
    }

    [Fact]
    public async Task RepeatedFailures_AreReportedOncePerRun()
    {
        var rounds = 0;
        var reports = new List<Exception>();
        var controller = Create(
            () => Task.CompletedTask,
            _ => { Interlocked.Increment(ref rounds); throw new InvalidOperationException("driver busy"); },
            (_, ex) => { lock (reports) reports.Add(ex); });
        var holder = new object();

        controller.Hold(holder);
        try
        {
            Assert.True(await EventuallyAsync(() => Volatile.Read(ref rounds) >= 4), $"only {Volatile.Read(ref rounds)} round(s) ran");
            lock (reports)
                Assert.Single(reports);
        }
        finally
        {
            controller.Release(holder);
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
        var controller = Create(
            () => Interlocked.Increment(ref starts) == 1 ? throw new InvalidOperationException("no driver") : Task.CompletedTask,
            _ => { Interlocked.Increment(ref rounds); return Task.CompletedTask; },
            (_, _) => { });
        var first = new object();
        var second = new object();

        controller.Hold(first);
        Assert.True(await EventuallyAsync(() => !controller.IsStarted), "still marked as started after the start-up step failed");
        Assert.Equal(0, Volatile.Read(ref rounds));
        controller.Release(first);

        controller.Hold(second);
        try
        {
            Assert.True(await EventuallyAsync(() => Volatile.Read(ref rounds) > 0), "the second start didn't run");
            Assert.True(controller.IsStarted);
        }
        finally
        {
            controller.Release(second);
        }
    }
}
