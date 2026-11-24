using System;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;
using Xunit;

namespace LenovoLegionToolkit.Tests.Utils;

/// <summary>The sensor update's one-at-a-time rule (<see cref="SingleFlight"/>), with real tasks.</summary>
public class SingleFlightTests
{
    [Fact]
    public async Task WorkThatFinishesAtOnce_IsNeverKeptAsRunning_SoTheNextCallRunsAgain()
    {
        // The sensor freeze: an update with nothing to read finished on the caller's thread and stayed "running".
        var flight = new SingleFlight();
        var runs = 0;

        for (var i = 0; i < 3; i++)
            await flight.RunAsync(() =>
            {
                runs++;
                return Task.CompletedTask;
            });

        Assert.Equal(3, runs);
        Assert.False(flight.IsRunning);
    }

    [Fact]
    public async Task CallsDuringARun_ShareIt()
    {
        var flight = new SingleFlight();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runs = 0;

        Task Work()
        {
            Interlocked.Increment(ref runs);
            return release.Task;
        }

        var first = flight.RunAsync(Work);
        var second = flight.RunAsync(Work);
        Assert.True(flight.IsRunning);
        Assert.Same(first, second);

        release.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, runs);
        Assert.False(flight.IsRunning);
    }

    [Fact]
    public async Task AFailedRun_IsPassedOn_AndTheNextCallRunsAgain()
    {
        var flight = new SingleFlight();

        await Assert.ThrowsAsync<InvalidOperationException>(() => flight.RunAsync(async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException("read failed");
        }));

        var ranAgain = false;
        await flight.RunAsync(() =>
        {
            ranAgain = true;
            return Task.CompletedTask;
        });
        Assert.True(ranAgain);
    }
}
