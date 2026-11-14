using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.AutoListeners;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

// The last-input time is supplied by the test; the listener's timers are bypassed by calling its poll and idle-step
// methods directly, so these cover its decisions. Real idle detection is on the hardware checklist.
public class UserInactivityListenerTests
{
    private sealed class Harness : IAsyncDisposable
    {
        public uint LastInput = 1000;
        public bool Fail;
        public readonly List<uint> Ticks = [];
        public readonly UserInactivityAutoListener Listener;

        public Harness() => Listener = new UserInactivityAutoListener(() => Fail ? throw new InvalidOperationException("no input info") : LastInput);

        private void OnChanged(object? sender, UserInactivityAutoListener.ChangedEventArgs e) => Ticks.Add(e.TickCount);

        public Task StartAsync() => Listener.SubscribeChangedAsync(OnChanged);

        public async ValueTask DisposeAsync() => await Listener.UnsubscribeChangedAsync(OnChanged);
    }

    [Fact]
    public async Task IdleSteps_AreReportedInOrder_WithTheirDuration()
    {
        await using var h = new Harness();
        await h.StartAsync();

        h.Listener.OnIdleTick();
        h.Listener.OnIdleTick();
        h.Listener.OnIdleTick();

        Assert.Equal(new uint[] { 1, 2, 3 }, h.Ticks);
        Assert.Equal(TimeSpan.FromSeconds(30), h.Listener.InactivityTimeSpan);
    }

    [Fact]
    public async Task InputAfterBeingIdle_ReportsActiveOnce_AndStartsCountingOver()
    {
        await using var h = new Harness();
        await h.StartAsync();
        h.Listener.OnIdleTick();
        h.Listener.OnIdleTick();

        h.LastInput = 5000;
        h.Listener.OnPoll();
        h.Listener.OnPoll();   // no new input since

        Assert.Equal(new uint[] { 1, 2, 0 }, h.Ticks);
        Assert.Equal(TimeSpan.Zero, h.Listener.InactivityTimeSpan);

        h.Listener.OnIdleTick();
        Assert.Equal(1u, h.Ticks[^1]);
    }

    [Fact]
    public async Task InputWhileAlreadyActive_ReportsNothing()
    {
        await using var h = new Harness();
        await h.StartAsync();

        h.LastInput = 2000;
        h.Listener.OnPoll();
        h.LastInput = 3000;
        h.Listener.OnPoll();

        Assert.Empty(h.Ticks);
    }

    [Fact]
    public async Task NoNewInput_ReportsNothing()
    {
        await using var h = new Harness();
        await h.StartAsync();
        h.Listener.OnIdleTick();

        h.Listener.OnPoll();

        Assert.Equal(new uint[] { 1 }, h.Ticks);
    }

    [Fact]
    public async Task LastInputTimeUnavailable_IsTreatedAsNoInput()
    {
        await using var h = new Harness();
        await h.StartAsync();
        h.Listener.OnIdleTick();

        h.Fail = true;
        h.Listener.OnPoll();

        Assert.Equal(new uint[] { 1 }, h.Ticks);
    }

    [Fact]
    public async Task HandlerThatStopsListening_FromAnotherThread_DoesNotHang()
    {
        // The event is raised outside the listener's lock, so a handler may wait for work on another thread that
        // needs that lock (here: unsubscribing, which stops the timers under the lock) without a deadlock.
        var listener = new UserInactivityAutoListener(() => 1000);
        var unsubscribed = new TaskCompletionSource();
        EventHandler<UserInactivityAutoListener.ChangedEventArgs>? handler = null;
        handler = (_, _) => Task.Run(() => listener.UnsubscribeChangedAsync(handler!)).GetAwaiter().GetResult();
        await listener.SubscribeChangedAsync(handler);

        var tick = Task.Run(() => { listener.OnIdleTick(); unsubscribed.SetResult(); });

        await unsubscribed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await tick;
    }

    /// <summary>The listener must not go back to low-level input hooks (anti-cheat software watches for them).</summary>
    [Fact]
    public void Listener_InstallsNoInputHooks()
    {
        var source = File.ReadAllText(Path.Combine(HelpContentTests.FindRepositoryRoot(), "LenovoLegionToolkit.Lib", "AutoListeners", "UserInactivityAutoListener.cs"));

        Assert.DoesNotContain("SetWindowsHookEx", source);
        Assert.Contains("GetLastInputInfo", source);
    }
}
