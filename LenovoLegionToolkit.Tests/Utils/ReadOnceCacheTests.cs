using System;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;
using Xunit;

namespace LenovoLegionToolkit.Tests.Utils;

// The cache God Mode uses for the firmware's ranges, so each apply doesn't read them again. The read is a counter
// here; what the real read does is on the hardware checklist.
public class ReadOnceCacheTests
{
    [Fact]
    public async Task SecondCall_UsesTheFirstResult_WithoutReadingAgain()
    {
        var cache = new ReadOnceCache<string>();
        var reads = 0;
        Task<string> Read() { reads++; return Task.FromResult($"read {reads}"); }

        Assert.Equal("read 1", await cache.GetAsync(Read));
        Assert.Equal("read 1", await cache.GetAsync(Read));
        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task FailedRead_IsNotKept_AndTheNextCallReadsAgain()
    {
        var cache = new ReadOnceCache<string>();
        var reads = 0;
        Task<string> Read() => ++reads == 1 ? throw new InvalidOperationException("firmware busy") : Task.FromResult("ranges");

        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetAsync(Read));
        Assert.Equal("ranges", await cache.GetAsync(Read));
        Assert.Equal(2, reads);
    }

    [Fact]
    public async Task Clear_MakesTheNextCallReadAgain()
    {
        var cache = new ReadOnceCache<string>();
        var reads = 0;
        Task<string> Read() { reads++; return Task.FromResult($"read {reads}"); }

        await cache.GetAsync(Read);
        cache.Clear();   // e.g. after resume from sleep

        Assert.Equal("read 2", await cache.GetAsync(Read));
    }

    [Fact]
    public async Task ConcurrentFirstCalls_ShareOneRead()
    {
        var cache = new ReadOnceCache<string>();
        var reads = 0;
        var gate = new TaskCompletionSource();
        async Task<string> Read() { Interlocked.Increment(ref reads); await gate.Task; return "ranges"; }

        var first = cache.GetAsync(Read);
        var second = cache.GetAsync(Read);
        gate.SetResult();

        Assert.Equal(["ranges", "ranges"], await Task.WhenAll(first, second));
        Assert.Equal(1, reads);
    }
}
