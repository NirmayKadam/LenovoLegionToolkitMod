using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;
using Xunit;

namespace LenovoLegionToolkit.Tests.Utils;

/// <summary>
/// The time limit used for WMI calls and driver requests: a call that never answers ends in a TimeoutException for the
/// caller, while answers and the call's own errors come through unchanged.
/// </summary>
public class TimeLimitTests
{
    [Fact]
    public async Task WorkThatNeverAnswers_EndsInATimeout_InsteadOfWaitingForGood()
    {
        var never = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        var waiting = TimeLimit.WithinAsync(never.Task, TimeSpan.FromMilliseconds(200), "test");
        var finished = await Task.WhenAny(waiting, Task.Delay(TimeSpan.FromSeconds(10)));

        Assert.Same(waiting, finished);
        var ex = await Assert.ThrowsAsync<TimeoutException>(() => waiting);
        Assert.Contains("didn't answer", ex.Message);

        never.SetException(new InvalidOperationException("late failure")); // logged, not thrown at anyone
    }

    [Fact]
    public async Task AnAnswerInTime_IsReturned() =>
        Assert.Equal(42, await TimeLimit.WithinAsync(Task.FromResult(42), TimeSpan.FromSeconds(5), "test"));

    [Fact]
    public async Task TheWorksOwnError_IsPassedOn_NotTurnedIntoATimeout()
    {
        var failing = Task.FromException<int>(new InvalidOperationException("driver said no"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => TimeLimit.WithinAsync(failing, TimeSpan.FromSeconds(5), "test"));
        Assert.Equal("driver said no", ex.Message);
    }

    [Fact]
    public async Task ATimeoutThrownByTheWorkItself_IsPassedOnAsItIs()
    {
        var failing = Task.FromException<int>(new TimeoutException("the work's own timeout"));

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => TimeLimit.WithinAsync(failing, TimeSpan.FromSeconds(5), "test"));
        Assert.Equal("the work's own timeout", ex.Message);
    }

    [Fact]
    public async Task WorkWithoutAResult_IsLimitedToo()
    {
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var waiting = TimeLimit.WithinAsync(never.Task, TimeSpan.FromMilliseconds(200), "test");
        Assert.Same(waiting, await Task.WhenAny(waiting, Task.Delay(TimeSpan.FromSeconds(10))));
        await Assert.ThrowsAsync<TimeoutException>(() => waiting);

        await TimeLimit.WithinAsync(Task.CompletedTask, TimeSpan.FromSeconds(5), "test");
    }

    // Throttled event work logging its error: BackgroundTaskTests, which reads a real log.
}
