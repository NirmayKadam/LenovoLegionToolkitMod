using System;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;
using Xunit;

namespace LenovoLegionToolkit.Tests.Utils;

// The shared sensor/battery reading. The "read" here is a counter and the clock is supplied, so these check the
// sharing rules; the real reads go through the Lenovo driver and WMI and are checked with a trace on the laptop.
public class SharedReadingTests
{
    private long _now = 1_000;

    private SharedReading<int> New() => new(TimeSpan.FromMilliseconds(750), () => _now);

    [Fact]
    public async Task WithinTheWindow_CallersShareOneRead()
    {
        var shared = New();
        var reads = 0;
        Task<int> Read() => Task.FromResult(++reads);

        Assert.Equal(1, await shared.GetAsync(Read));
        _now += 500;
        Assert.Equal(1, await shared.GetAsync(Read));
        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task AfterTheWindow_ReadsAgain()
    {
        var shared = New();
        var reads = 0;
        Task<int> Read() => Task.FromResult(++reads);

        await shared.GetAsync(Read);
        _now += 750;

        Assert.Equal(2, await shared.GetAsync(Read));
    }

    [Fact]
    public async Task AReadInProgress_IsSharedNotRepeated()
    {
        var shared = New();
        var reads = 0;
        var gate = new TaskCompletionSource();
        async Task<int> Read() { Interlocked.Increment(ref reads); await gate.Task; return 42; }

        var first = shared.GetAsync(Read);
        var second = shared.GetAsync(Read);
        gate.SetResult();

        var results = await Task.WhenAll(first, second);
        Assert.Equal([42, 42], results);
        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task AFailedRead_IsNotKept()
    {
        var shared = New();
        var reads = 0;
        Task<int> Read() => ++reads == 1 ? Task.FromException<int>(new InvalidOperationException("driver busy")) : Task.FromResult(7);

        await Assert.ThrowsAsync<InvalidOperationException>(() => shared.GetAsync(Read));
        Assert.Equal(7, await shared.GetAsync(Read));
    }

    [Fact]
    public async Task AFinishedRead_DoesNotStayInProgress()
    {
        // A read that completes at once must not be handed out again after the window has passed.
        var shared = New();
        var reads = 0;
        Task<int> Read() => Task.FromResult(++reads);

        await shared.GetAsync(Read);
        _now += 10_000;
        Assert.Equal(2, await shared.GetAsync(Read));
        _now += 10_000;
        Assert.Equal(3, await shared.GetAsync(Read));
    }

    [Fact]
    public void SynchronousForm_FollowsTheSameRules()
    {
        var shared = New();
        var reads = 0;

        Assert.Equal(1, shared.Get(() => ++reads));
        Assert.Equal(1, shared.Get(() => ++reads));
        _now += 800;
        Assert.Equal(2, shared.Get(() => ++reads));
        Assert.Throws<InvalidOperationException>(() => { _now += 800; return shared.Get(() => throw new InvalidOperationException()); });
        Assert.Equal(3, shared.Get(() => ++reads));
    }

    [Fact]
    public void Invalidate_MakesTheNextCallerRead()
    {
        var shared = New();
        var reads = 0;

        shared.Get(() => ++reads);
        shared.Invalidate();

        Assert.Equal(2, shared.Get(() => ++reads));
    }
}
