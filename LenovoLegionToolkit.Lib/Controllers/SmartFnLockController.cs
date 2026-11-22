using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Controllers;

public class SmartFnLockController(FnLockFeature feature, ApplicationSettings settings) : IDisposable
{
    private const int HOLD_THRESHOLD_MS = 180;
    private const int HARDWARE_SETTLE_MS = 150;

    private readonly SemaphoreSlim _hardwareGate = new(1, 1);
    private readonly object _stateLock = new();
    private readonly ModifierKeyState _keys = new();
    private CancellationTokenSource? _holdCts;

    private bool _wasModifierActive;
    private bool _hardwareToggledOff;
    private bool _isDisposed;

    public void OnKeyboardEvent(nuint wParam, KBDLLHOOKSTRUCT kbStruct)
    {
        var flags = settings.Store.SmartFnLockFlags;
        if (flags == 0)
        {
            // Switched off while a modifier was held: no event will put Fn Lock back now.
            if (IsHoldingOrToggled())
                _ = KeysNoLongerSeenAsync();
            return;
        }

        var isKeyDown = wParam is PInvoke.WM_KEYDOWN or PInvoke.WM_SYSKEYDOWN;

        lock (_stateLock)
        {
            if (_isDisposed)
                return;

            _keys.Update(kbStruct.vkCode, isKeyDown);
            var isModifierActive = _keys.IsActive(flags);

            if (isModifierActive == _wasModifierActive)
                return;

            _wasModifierActive = isModifierActive;

            _holdCts?.Cancel();
            _holdCts?.Dispose();
            _holdCts = new CancellationTokenSource();
            var ct = _holdCts.Token;

            if (isModifierActive)
            {
                Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(HOLD_THRESHOLD_MS, ct).ConfigureAwait(false);
                        if (ct.IsCancellationRequested)
                            return;

                        await _hardwareGate.WaitAsync(ct).ConfigureAwait(false);
                        var didWrite = false;
                        try
                        {
                            if (ct.IsCancellationRequested)
                                return;

                            var state = await feature.GetStateAsync().ConfigureAwait(false);
                            if (state == FnLockState.Off || ct.IsCancellationRequested)
                                return;

                            lock (_stateLock)
                            {
                                if (_isDisposed || ct.IsCancellationRequested)
                                    return;

                                _hardwareToggledOff = true;
                            }

                            if (Log.Instance.IsTraceEnabled)
                                Log.Instance.Trace($"Modifier held past threshold, disabling Fn Lock temporarily...");

                            await feature.SetStateAsync(FnLockState.Off, verify: false).ConfigureAwait(false);
                            didWrite = true;
                        }
                        finally
                        {
                            if (didWrite)
                                await Task.Delay(HARDWARE_SETTLE_MS).ConfigureAwait(false);
                            _hardwareGate.Release();
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        if (Log.Instance.IsTraceEnabled)
                            Log.Instance.Trace($"Failed to handle keyboard event, {ex.Message}");
                    }
                }, ct);
            }
            else
            {
                var needRestore = _hardwareToggledOff;
                _hardwareToggledOff = false;

                if (needRestore)
                    Task.Run(() => RestoreFnLockAsync("Modifier released"));
            }
        }
    }

    /// <summary>
    /// Key events stop arriving: game-safe mode or shutdown removed the keyboard hook, or Smart Fn Lock was switched
    /// off. A modifier held at that moment would never be seen released, which used to leave Fn Lock off (and the key
    /// counted as held). Forgets the held keys and, if Fn Lock was turned off for one, turns it back on.
    /// </summary>
    public Task KeysNoLongerSeenAsync()
    {
        bool needRestore;
        lock (_stateLock)
        {
            if (_isDisposed)
                return Task.CompletedTask;

            _holdCts?.Cancel();
            _holdCts?.Dispose();
            _holdCts = null;

            _keys.Clear();
            _wasModifierActive = false;

            needRestore = _hardwareToggledOff;
            _hardwareToggledOff = false;
        }

        return needRestore ? RestoreFnLockAsync("Keys no longer seen") : Task.CompletedTask;
    }

    private bool IsHoldingOrToggled()
    {
        lock (_stateLock)
            return _wasModifierActive || _hardwareToggledOff;
    }

    /// <summary>
    /// Turns Fn Lock back on after it was turned off for a held modifier. Waits for any write in progress (the gate),
    /// so a hold that had just turned it off can't do so again afterwards. Never throws.
    /// </summary>
    private async Task RestoreFnLockAsync(string why)
    {
        try
        {
            await _hardwareGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"{why}, re-enabling Fn Lock...");

                await feature.SetStateAsync(FnLockState.On, verify: false).ConfigureAwait(false);
            }
            finally
            {
                await Task.Delay(HARDWARE_SETTLE_MS).ConfigureAwait(false);
                _hardwareGate.Release();
            }
        }
        catch (Exception ex)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Failed to re-enable Fn Lock ({why}), {ex.Message}");
        }
    }

    public void Dispose()
    {
        lock (_stateLock)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _holdCts?.Cancel();
            _holdCts?.Dispose();
        }

        // Wait for any in-flight hardware write (for example a hold task that already decided to
        // switch Fn Lock off) before deciding whether to restore. Reading the flag first, as before,
        // could restore "On" and then have the in-flight write turn it off again after exit.
        var gateAcquired = _hardwareGate.Wait(TimeSpan.FromSeconds(1));

        bool needRestore;
        lock (_stateLock)
        {
            needRestore = _hardwareToggledOff;
            _hardwareToggledOff = false;
        }

        if (needRestore)
        {
            try { feature.SetStateAsync(FnLockState.On, verify: false).GetAwaiter().GetResult(); }
            catch (Exception ex)
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Failed to restore Fn Lock on dispose, {ex.Message}");
            }
        }

        if (gateAcquired)
        {
            _hardwareGate.Release();
            _hardwareGate.Dispose();
        }
        else if (Log.Instance.IsTraceEnabled)
        {
            Log.Instance.Trace($"Hardware gate still busy on dispose; leaving it for the running task.");
        }

        GC.SuppressFinalize(this);
    }
}
