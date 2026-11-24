using System;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.Tests.UI;
using Xunit;
using BackgroundTask = LenovoLegionToolkit.Lib.Extensions.TaskExtensions;

namespace LenovoLegionToolkit.Tests.Utils;

/// <summary>
/// Background work started and left alone (<see cref="BackgroundTask.Forget"/>), and throttled event work. Their
/// failures used to be lost, or to surface later as an "Unexpected exception" pop-up; now each is logged and never
/// thrown. The log is a real one in a temporary folder (<see cref="TestLog"/>), so these tests run alone.
/// </summary>
[Collection(nameof(AppLoggerCollection))]
public class BackgroundTaskTests
{
    [Fact]
    public async Task AFailure_IsLogged_AndNotThrown()
    {
        using var log = new TestLog();
        var failing = Task.Run(() => throw new InvalidOperationException("The service check timed out."));

        var thrown = await Record.ExceptionAsync(() => BackgroundTask.ObserveAsync(failing, "Checking Vantage"));

        Assert.Null(thrown);
        Assert.Contains("Checking Vantage failed.", log.Text);
        Assert.Contains("The service check timed out.", log.Text);
    }

    [Fact]
    public async Task ACancellation_IsNeitherThrownNorLoggedAsAFailure()
    {
        using var log = new TestLog();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var thrown = await Record.ExceptionAsync(() => BackgroundTask.ObserveAsync(Task.Delay(1000, cts.Token), "Refreshing"));

        Assert.Null(thrown);
        Assert.DoesNotContain("Refreshing failed.", log.Text);
    }

    [Fact]
    public async Task WorkThatSucceeds_IsAwaitedToTheEnd_WithNothingLogged()
    {
        using var log = new TestLog();
        var done = false;
        var work = Task.Run(async () =>
        {
            await Task.Delay(30);
            done = true;
        });

        await BackgroundTask.ObserveAsync(work, "Refreshing");

        Assert.True(done);
        Assert.DoesNotContain("Refreshing failed.", log.Text);
    }

    [Fact]
    public async Task ThrottledEventWork_LogsItsError_InsteadOfThrowingIntoTheEventHandler()
    {
        using var log = new TestLog();
        var dispatcher = new ThrottleLastDispatcher(TimeSpan.FromMilliseconds(10), "test-tag");

        var thrown = await Record.ExceptionAsync(() => dispatcher.DispatchLoggingErrorsAsync(() => throw new InvalidOperationException("refresh failed")));

        Assert.Null(thrown);
        Assert.Contains("Throttled work failed. [tag=test-tag]", log.Text);
        Assert.Contains("refresh failed", log.Text);

        // The plain dispatch still passes errors on, for callers that handle them.
        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.DispatchAsync(() => throw new InvalidOperationException("refresh failed")));
    }
}
