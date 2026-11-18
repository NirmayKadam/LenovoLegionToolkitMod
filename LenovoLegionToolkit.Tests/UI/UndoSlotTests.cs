using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

/// <summary>The one change that can be undone: its 30 seconds (clock supplied by the test), replacement, and running once.</summary>
public class UndoSlotTests
{
    private DateTime _now = new(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);

    private UndoSlot Slot() => new(() => _now);

    [Fact]
    public void Offer_IsPending_WithTheTimeLeft()
    {
        var slot = Slot();
        slot.Offer("Fan curve saved.", () => Task.FromResult(true));
        _now += TimeSpan.FromSeconds(10);

        var view = slot.Current;

        Assert.Equal(UndoStatus.Pending, view.Status);
        Assert.Equal("Fan curve saved.", view.Description);
        Assert.Equal(TimeSpan.FromSeconds(20), view.Remaining);
    }

    [Fact]
    public async Task AfterThirtySeconds_ItExpires_AndUndoDoesNothing()
    {
        var slot = Slot();
        var ran = false;
        slot.Offer("x", () => { ran = true; return Task.FromResult(true); });
        _now += UndoSlot.Window;

        Assert.Equal(UndoStatus.None, slot.Current.Status);
        Assert.Equal(UndoStatus.None, await slot.TryUndoAsync());
        Assert.False(ran);
    }

    [Fact]
    public async Task ANewerChange_ReplacesTheOlder()
    {
        var slot = Slot();
        string? undone = null;
        slot.Offer("first", () => { undone = "first"; return Task.FromResult(true); });
        slot.Offer("second", () => { undone = "second"; return Task.FromResult(true); });

        await slot.TryUndoAsync();

        Assert.Equal("second", undone);
    }

    [Fact]
    public async Task Undo_RunsOnce_ThenShowsItsResult_ThenClears()
    {
        var slot = Slot();
        var runs = 0;
        slot.Offer("x", () => { runs++; return Task.FromResult(true); });

        Assert.Equal(UndoStatus.Done, await slot.TryUndoAsync());
        Assert.Equal(UndoStatus.None, await slot.TryUndoAsync());
        Assert.Equal(1, runs);
        Assert.Equal(UndoStatus.Done, slot.Current.Status);

        _now += UndoSlot.ResultShownFor;
        Assert.Equal(UndoStatus.None, slot.Current.Status);
    }

    [Fact]
    public async Task Undo_NotEverythingApplied_IsReported()
    {
        var slot = Slot();
        slot.Offer("x", () => Task.FromResult(false));

        Assert.Equal(UndoStatus.NotAllApplied, await slot.TryUndoAsync());
    }

    [Fact]
    public async Task Undo_ThatThrows_IsReported_NotThrown()
    {
        var slot = Slot();
        slot.Offer("x", () => throw new InvalidOperationException("firmware call failed"));

        Assert.Equal(UndoStatus.NotAllApplied, await slot.TryUndoAsync());
    }

    [Fact]
    public async Task WhileAnUndoRuns_ANewOfferIsNotMixedIn()
    {
        var slot = Slot();
        var release = new TaskCompletionSource<bool>();
        slot.Offer("first", () => release.Task);

        var running = slot.TryUndoAsync();
        slot.Offer("second", () => Task.FromResult(true));
        Assert.Equal(UndoStatus.Running, slot.Current.Status);

        release.SetResult(true);
        Assert.Equal(UndoStatus.Done, await running);
    }

    [Fact]
    public void Changed_IsRaised_ForEachOffer()
    {
        var slot = Slot();
        var raised = 0;
        slot.Changed += (_, _) => raised++;

        slot.Offer("x", () => Task.FromResult(true));

        Assert.Equal(1, raised);
    }
}
