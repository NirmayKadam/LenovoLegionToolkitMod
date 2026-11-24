using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.Settings;
using Xunit;

namespace LenovoLegionToolkit.Tests.Utils;

/// <summary>
/// The keyboard hook and media-key listener added with Lenovo Legion Toolkit 2.38 run only while a key needs them
/// (or key discovery listens), and never in game-safe mode. The listeners are replaced by counters.
/// </summary>
public class ExtraKeyListenersTests
{
    private const int Keyboard = SpecialKeySettings.SpecialKeySettingsStore.KeyboardKeyCodeOffset + 0x2D;        // Fn+I
    private const int MediaKey = SpecialKeySettings.SpecialKeySettingsStore.ConsumerControlKeyCodeOffset + 0xE2; // Fn+F1
    private const int DriverKey = SpecialKeySettings.SpecialKeySettingsStore.DriverKeyCodeOffset + 5;

    private sealed class Counters
    {
        public int HookStarts, HookStops, MediaStarts, MediaStops;
        public bool FailHookStart, FailHookStop;
        public List<int> CustomKeys { get; } = [];

        public ExtraKeyListeners Create() => new(
            () => { if (FailHookStart) throw new InvalidOperationException("hook"); HookStarts++; return Task.CompletedTask; },
            () => { if (FailHookStop) throw new InvalidOperationException("hook"); HookStops++; return Task.CompletedTask; },
            () => { MediaStarts++; return Task.CompletedTask; },
            () => { MediaStops++; return Task.CompletedTask; },
            () => CustomKeys.ToArray());
    }

    [Theory]
    [InlineData(new int[0], false, false, false, false)]
    [InlineData(new[] { Keyboard }, false, false, true, false)]
    [InlineData(new[] { MediaKey }, false, false, true, true)]   // the hook keeps Windows from also changing the volume
    [InlineData(new[] { DriverKey }, false, false, false, false)] // Fn keys the Lenovo driver reports need neither
    [InlineData(new int[0], false, true, false, true)]           // key discovery listens for media keys
    [InlineData(new[] { Keyboard, MediaKey }, true, true, false, false)] // game-safe mode: nothing
    public void Wanted(int[] customKeys, bool suspended, bool discovering, bool hook, bool media)
    {
        Assert.Equal((hook, media), ExtraKeyListeners.Wanted(customKeys, suspended, discovering));
    }

    [Fact]
    public async Task NoCustomKey_NothingIsInstalled()
    {
        var c = new Counters();
        var listeners = c.Create();

        await listeners.UpdateAsync();

        Assert.Equal(0, c.HookStarts);
        Assert.Equal(0, c.MediaStarts);
    }

    [Fact]
    public async Task BoundKey_InstallsTheHookOnce_AndRemovingTheBindingRemovesIt()
    {
        var c = new Counters();
        var listeners = c.Create();
        c.CustomKeys.Add(Keyboard);

        await listeners.UpdateAsync();
        await listeners.UpdateAsync();
        Assert.Equal(1, c.HookStarts);
        Assert.True(listeners.IsHookRunning);

        c.CustomKeys.Clear();
        await listeners.UpdateAsync();
        Assert.Equal(1, c.HookStops);
        Assert.False(listeners.IsHookRunning);
    }

    [Fact]
    public async Task GameSafeMode_RemovesBoth_AndTheyComeBackAfterwards()
    {
        var c = new Counters();
        var listeners = c.Create();
        c.CustomKeys.Add(MediaKey);
        await listeners.UpdateAsync();

        await listeners.SetSuspendedAsync(true);
        Assert.False(listeners.IsHookRunning);
        Assert.False(listeners.IsConsumerControlRunning);
        Assert.Equal((1, 1), (c.HookStops, c.MediaStops));

        // A binding changed while the game runs must not bring them back.
        c.CustomKeys.Add(Keyboard);
        await listeners.UpdateAsync();
        Assert.False(listeners.IsHookRunning);
        Assert.Equal(1, c.HookStarts);

        await listeners.SetSuspendedAsync(false);
        Assert.True(listeners.IsHookRunning);
        Assert.True(listeners.IsConsumerControlRunning);
        Assert.Equal((2, 2), (c.HookStarts, c.MediaStarts));
    }

    [Fact]
    public async Task KeyDiscovery_HoldsTheMediaKeyListener_UntilReleasedOnce()
    {
        var c = new Counters();
        var listeners = c.Create();

        var hold = await listeners.HoldForDiscoveryAsync();
        Assert.True(listeners.IsConsumerControlRunning);
        Assert.False(listeners.IsHookRunning);

        await hold.DisposeAsync();
        await hold.DisposeAsync();
        Assert.False(listeners.IsConsumerControlRunning);
        Assert.Equal(1, c.MediaStops);
    }

    [Fact]
    public async Task KeyDiscovery_InGameSafeMode_InstallsNothing()
    {
        var c = new Counters();
        var listeners = c.Create();
        await listeners.SetSuspendedAsync(true);

        await using var hold = await listeners.HoldForDiscoveryAsync();

        Assert.Equal(0, c.MediaStarts);
    }

    [Fact]
    public async Task FailedStart_IsTriedAgainNextTime()
    {
        var c = new Counters { FailHookStart = true };
        var listeners = c.Create();
        c.CustomKeys.Add(Keyboard);

        await listeners.UpdateAsync();
        Assert.False(listeners.IsHookRunning);

        c.FailHookStart = false;
        await listeners.UpdateAsync();
        Assert.True(listeners.IsHookRunning);
    }

    [Fact]
    public async Task FailedStop_StillCountsAsRunning_SoItIsRemovedLater()
    {
        var c = new Counters();
        var listeners = c.Create();
        c.CustomKeys.Add(Keyboard);
        await listeners.UpdateAsync();

        c.FailHookStop = true;
        await listeners.SetSuspendedAsync(true);
        Assert.True(listeners.IsHookRunning);

        c.FailHookStop = false;
        await listeners.SetSuspendedAsync(true);
        Assert.False(listeners.IsHookRunning);
        Assert.Equal(1, c.HookStops);
    }
}
