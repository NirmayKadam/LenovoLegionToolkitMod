using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;
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
    private CancellationTokenSource? _holdCts;

    // Left and right keys are tracked separately: releasing one side must not clear a modifier
    // that is still held on the other side.
    private bool _leftCtrlDepressed;
    private bool _rightCtrlDepressed;
    private bool _leftShiftDepressed;
    private bool _rightShiftDepressed;
    private bool _leftAltDepressed;
    private bool _rightAltDepressed;
    private bool _wasModifierActive;
    private bool _hardwareToggledOff;
    private bool _isDisposed;

    public void OnKeyboardEvent(nuint wParam, KBDLLHOOKSTRUCT kbStruct)
    {
        if (settings.Store.SmartFnLockFlags == 0)
            return;

        bool isModifierActive = IsModifierKeyPressed(wParam, kbStruct);

        lock (_stateLock)
        {
            if (_isDisposed || isModifierActive == _wasModifierActive)
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
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            await _hardwareGate.WaitAsync().ConfigureAwait(false);
                            try
                            {
                                if (Log.Instance.IsTraceEnabled)
                                    Log.Instance.Trace($"Modifier released, re-enabling Fn Lock...");

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
                                Log.Instance.Trace($"Failed to handle keyboard event, {ex.Message}");
                        }
                    });
                }
            }
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

    private bool IsModifierKeyPressed(nuint wParam, KBDLLHOOKSTRUCT kbStruct)
    {
        var isKeyDown = wParam is PInvoke.WM_KEYDOWN or PInvoke.WM_SYSKEYDOWN;
        var vkKeyCode = (VIRTUAL_KEY)kbStruct.vkCode;

        // Low-level hooks report the left/right variants; a generic code is treated as the left key.
        switch (vkKeyCode)
        {
            case VIRTUAL_KEY.VK_LCONTROL or VIRTUAL_KEY.VK_CONTROL:
                _leftCtrlDepressed = isKeyDown;
                break;
            case VIRTUAL_KEY.VK_RCONTROL:
                _rightCtrlDepressed = isKeyDown;
                break;
            case VIRTUAL_KEY.VK_LSHIFT or VIRTUAL_KEY.VK_SHIFT:
                _leftShiftDepressed = isKeyDown;
                break;
            case VIRTUAL_KEY.VK_RSHIFT:
                _rightShiftDepressed = isKeyDown;
                break;
            case VIRTUAL_KEY.VK_LMENU or VIRTUAL_KEY.VK_MENU:
                _leftAltDepressed = isKeyDown;
                break;
            case VIRTUAL_KEY.VK_RMENU:
                _rightAltDepressed = isKeyDown;
                break;
        }

        var ctrlDepressed = _leftCtrlDepressed || _rightCtrlDepressed;
        var shiftDepressed = _leftShiftDepressed || _rightShiftDepressed;
        var altDepressed = _leftAltDepressed || _rightAltDepressed;

        if (!ctrlDepressed && !shiftDepressed && !altDepressed)
            return false;

        var result = false;
        var flags = settings.Store.SmartFnLockFlags;

        if (flags.HasFlag(ModifierKey.Ctrl))
            result |= ctrlDepressed;

        if (flags.HasFlag(ModifierKey.Shift))
            result |= shiftDepressed;

        if (flags.HasFlag(ModifierKey.Alt))
            result |= altDepressed;

        return result;
    }
}