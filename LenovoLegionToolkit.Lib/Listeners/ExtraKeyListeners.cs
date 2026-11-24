using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Listeners;

/// <summary>
/// Runs the two key listeners added with Lenovo Legion Toolkit 2.38 only while they are needed:
/// <list type="bullet">
/// <item>the low-level keyboard hook (<see cref="KeyboardHookListener"/>), which sees every key press system-wide,
/// only while a key it handles (Fn+I, Fn+K, or a media key whose own action is replaced) has a custom action;</item>
/// <item>the media-key listener (<see cref="ConsumerControlKeyListener"/>, raw input) only while a media key has a
/// custom action, or the key discovery window is listening.</item>
/// </list>
/// Game-safe mode removes both while an anti-cheat game runs, like the macro hook; they come back afterwards.
/// </summary>
public sealed class ExtraKeyListeners
{
    private readonly Func<Task> _startHook;
    private readonly Func<Task> _stopHook;
    private readonly Func<Task> _startConsumerControl;
    private readonly Func<Task> _stopConsumerControl;
    private readonly Func<IEnumerable<int>> _customKeyCodes;

    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private bool _hookRunning;
    private bool _consumerControlRunning;
    private bool _suspended;
    private int _discoveryHolds;

    public ExtraKeyListeners(KeyboardHookListener hook, ConsumerControlKeyListener consumerControl, SpecialKeySettings settings)
        : this(hook.StartAsync, hook.StopAsync, consumerControl.StartAsync, consumerControl.StopAsync,
            () => settings.Store.KeyModes.Where(kv => kv.Value == CustomSpecialKey.Custom).Select(kv => kv.Key).ToArray())
    {
    }

    /// <summary>For tests: starting and stopping each listener, and the keys with a custom action, are supplied by the caller.</summary>
    internal ExtraKeyListeners(Func<Task> startHook, Func<Task> stopHook, Func<Task> startConsumerControl, Func<Task> stopConsumerControl, Func<IEnumerable<int>> customKeyCodes)
    {
        _startHook = startHook;
        _stopHook = stopHook;
        _startConsumerControl = startConsumerControl;
        _stopConsumerControl = stopConsumerControl;
        _customKeyCodes = customKeyCodes;
    }

    public bool IsHookRunning => _hookRunning;
    public bool IsConsumerControlRunning => _consumerControlRunning;

    internal static bool IsConsumerControlKeyCode(int code) =>
        code >= SpecialKeySettings.SpecialKeySettingsStore.ConsumerControlKeyCodeOffset
        && code < SpecialKeySettings.SpecialKeySettingsStore.KeyboardKeyCodeOffset;

    internal static bool IsKeyboardKeyCode(int code) =>
        code >= SpecialKeySettings.SpecialKeySettingsStore.KeyboardKeyCodeOffset
        && code < SpecialKeySettings.SpecialKeySettingsStore.KeyboardKeyCodeOffset + 0x10000;

    /// <summary>
    /// Which listeners should run. The hook is needed for Fn+I/Fn+K and also for media keys with a custom action,
    /// since it keeps Windows from also doing the key's own action (volume, play/pause). Nothing runs in game-safe mode.
    /// </summary>
    internal static (bool Hook, bool ConsumerControl) Wanted(IEnumerable<int> customKeyCodes, bool suspended, bool discovering)
    {
        if (suspended)
            return (false, false);

        var codes = customKeyCodes.ToArray();
        var consumerControlKey = codes.Any(IsConsumerControlKeyCode);
        var keyboardKey = codes.Any(IsKeyboardKeyCode);

        return (keyboardKey || consumerControlKey, consumerControlKey || discovering);
    }

    /// <summary>Starts or stops the listeners to match the saved key actions (after a key's action changed, or at start).</summary>
    public Task UpdateAsync() => ApplyAsync(null, 0);

    /// <summary>Game-safe mode: true removes both listeners, false lets them run again where needed.</summary>
    public Task SetSuspendedAsync(bool suspended) => ApplyAsync(suspended, 0);

    /// <summary>Keeps the media-key listener running while the key discovery window listens (not in game-safe mode).</summary>
    public async Task<IAsyncDisposable> HoldForDiscoveryAsync()
    {
        await ApplyAsync(null, +1).ConfigureAwait(false);
        return new DiscoveryHold(this);
    }

    private sealed class DiscoveryHold(ExtraKeyListeners owner) : IAsyncDisposable
    {
        private int _released;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                await owner.ApplyAsync(null, -1).ConfigureAwait(false);
        }
    }

    private async Task ApplyAsync(bool? suspended, int discoveryChange)
    {
        await _oneAtATime.WaitAsync().ConfigureAwait(false);
        try
        {
            if (suspended is { } s)
                _suspended = s;
            _discoveryHolds = Math.Max(0, _discoveryHolds + discoveryChange);

            IEnumerable<int> codes;
            try
            {
                codes = _customKeyCodes();
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Couldn't read the special key settings; the extra key listeners stay off.", ex);
                codes = [];
            }

            var (hook, consumerControl) = Wanted(codes, _suspended, _discoveryHolds > 0);

            _hookRunning = await SwitchAsync("keyboard hook", _hookRunning, hook, _startHook, _stopHook).ConfigureAwait(false);
            _consumerControlRunning = await SwitchAsync("media key listener", _consumerControlRunning, consumerControl, _startConsumerControl, _stopConsumerControl).ConfigureAwait(false);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    /// <summary>Returns whether the listener runs afterwards. A failure is logged and leaves the state as it was, so the next update tries again.</summary>
    private static async Task<bool> SwitchAsync(string name, bool running, bool wanted, Func<Task> start, Func<Task> stop)
    {
        if (running == wanted)
            return running;

        try
        {
            if (wanted)
                await start().ConfigureAwait(false);
            else
                await stop().ConfigureAwait(false);

            Log.Instance.Trace($"Extra key listener {(wanted ? "started" : "stopped")}: {name}.");
            return wanted;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't {(wanted ? "start" : "stop")} the {name}.", ex);
            return running;
        }
    }
}
