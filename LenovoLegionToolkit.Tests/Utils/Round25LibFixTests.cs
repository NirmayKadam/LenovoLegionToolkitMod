using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.Utils;
using NvAPIWrapper.Native.GPU;
using NvAPIWrapper.Native.Interfaces.GPU;
using Xunit;

namespace LenovoLegionToolkit.Tests.Utils;

/// <summary>Smaller library fixes: each decision checked with given times or real tasks.</summary>
public class Round25LibFixTests
{
    // ---- Machine information after a probe that got no answer ----

    [Fact]
    public void AScanWithAnUnansweredProbe_IsReadAgain_OnlyAfterFiveMinutes()
    {
        const long scannedAt = 1_000_000;

        Assert.False(Compatibility.ShouldRescan(scannedAt, scannedAt + 1_000));                 // a second later: no
        Assert.False(Compatibility.ShouldRescan(scannedAt, scannedAt + 299_999));
        Assert.True(Compatibility.ShouldRescan(scannedAt, scannedAt + 300_000));                // five minutes on
        Assert.Equal(TimeSpan.FromMinutes(5), Compatibility.RescanAfterTimeout);
    }

    // ---- Resume reported twice ----

    [Fact]
    public void ASecondResumeWithinTenSeconds_IsTheDuplicate_ALaterOneIsANewWakeUp()
    {
        const long firstAt = 50_000;

        Assert.False(PowerStateListener.IsDuplicateResume(null, firstAt));                     // the first resume
        Assert.True(PowerStateListener.IsDuplicateResume(firstAt, firstAt + 300));             // the native callback's copy
        Assert.True(PowerStateListener.IsDuplicateResume(firstAt, firstAt + 9_999));
        Assert.False(PowerStateListener.IsDuplicateResume(firstAt, firstAt + 10_000));
    }

    // ---- Throttled work started from several threads at once ----

    [Fact]
    public async Task EventsFromSeveralThreadsAtOnce_RunTheWorkOnce()
    {
        var dispatcher = new ThrottleLastDispatcher(TimeSpan.FromMilliseconds(100));
        var runs = 0;
        using var go = new ManualResetEventSlim();

        var calls = new List<Task>();
        for (var i = 0; i < 8; i++)
            calls.Add(Task.Run(() =>
            {
                go.Wait();
                return dispatcher.DispatchAsync(() =>
                {
                    Interlocked.Increment(ref runs);
                    return Task.CompletedTask;
                });
            }));

        go.Set();   // all eight at the same moment
        await Task.WhenAll(calls);

        Assert.Equal(1, runs);
    }

    // ---- NVIDIA clock offsets ----

    [Fact]
    public void MissingClockOffsets_CountAsZero_InsteadOfFailingTheWholeReading()
    {
        var none = new Dictionary<PerformanceStateId, IPerformanceStates20ClockEntry[]>();
        var p0WithoutEntries = new Dictionary<PerformanceStateId, IPerformanceStates20ClockEntry[]> { [PerformanceStateId.P0_3DPerformance] = [] };

        Assert.Equal(0, AbstractSensorsController.ClockOffsetMhz(none, 0));
        Assert.Equal(0, AbstractSensorsController.ClockOffsetMhz(p0WithoutEntries, 1));
    }
}
