using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Power;
using Windows.Win32.UI.WindowsAndMessaging;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Controllers.GodMode;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Features.Hybrid.Notify;
using System.Linq;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.Overclocking.Amd;
using LenovoLegionToolkit.Lib.Station.Core;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using Microsoft.Win32;

namespace LenovoLegionToolkit.Lib.Listeners;

public sealed class PowerStateListener : IListener<PowerStateListener.ChangedEventArgs>, IDisposable
{
    public class ChangedEventArgs(PowerStateEvent powerStateEvent, bool powerAdapterStateChanged) : EventArgs
    {
        public PowerStateEvent PowerStateEvent { get; } = powerStateEvent;
        public bool PowerAdapterStateChanged { get; } = powerAdapterStateChanged;
    }

    private readonly SafeHandle _recipientHandle;
    private readonly PDEVICE_NOTIFY_CALLBACK_ROUTINE _callback;

    private readonly PowerModeFeature _powerModeFeature;
    private readonly BatteryFeature _batteryFeature;
    private readonly DGPUNotify _dgpuNotify;
    private readonly RGBKeyboardBacklightController _rgbController;

    private readonly SemaphoreSlim _processingLock = new(1, 1);

    private bool _started;
    private bool _disposed;
    private HPOWERNOTIFY _handle;
    private PowerAdapterStatus? _lastPowerAdapterState;

    private readonly SemaphoreSlim _notifyLock = new(1, 1);
    private DateTime _lastNotifyTime = DateTime.MinValue;

    public event EventHandler<ChangedEventArgs>? Changed;

    public unsafe PowerStateListener(
        PowerModeFeature powerModeFeature,
        BatteryFeature batteryFeature,
        DGPUNotify dgpuNotify,
        RGBKeyboardBacklightController rgbController)
    {
        _powerModeFeature = powerModeFeature;
        _batteryFeature = batteryFeature;
        _dgpuNotify = dgpuNotify;
        _rgbController = rgbController;

        _callback = Callback;

        _recipientHandle = new StructSafeHandle<DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS>(new DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS
        {
            Callback = _callback,
            Context = null,
        });
    }

    public async Task StartAsync()
    {
        if (_started || _disposed) return;

        _lastPowerAdapterState = await Power.IsPowerAdapterConnectedAsync().ConfigureAwait(false);

        SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
        RegisterSuspendResumeNotification();

        _started = true;
    }

    public Task StopAsync()
    {
        if (!_started) return Task.CompletedTask;

        SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
        UnRegisterSuspendResumeNotification();

        _started = false;
        return Task.CompletedTask;
    }

    private async void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        try
        {
            Log.Instance.Trace($"Event received: {e.Mode}");

            var powerStateEvent = e.Mode switch
            {
                PowerModes.StatusChange => PowerStateEvent.StatusChange,
                PowerModes.Resume => PowerStateEvent.Resume,
                PowerModes.Suspend => PowerStateEvent.Suspend,
                _ => PowerStateEvent.Unknown
            };

            if (powerStateEvent is PowerStateEvent.Unknown) return;

            await ProcessPowerEventAsync(powerStateEvent).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Error in SystemEvents_PowerModeChanged: {ex}");
        }
    }

    private unsafe uint Callback(void* context, uint type, void* setting)
    {
        TriggerNativeCallback(type);
        return (uint)WIN32_ERROR.ERROR_SUCCESS;
    }

    private void TriggerNativeCallback(uint type)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await CallbackAsync(type).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Error in Native Power Callback: {ex}");
            }
        });
    }

    private async Task CallbackAsync(uint type)
    {
        var powerStateEvent = type switch
        {
            PInvoke.PBT_APMSUSPEND => PowerStateEvent.Suspend,
            PInvoke.PBT_APMRESUMEAUTOMATIC => PowerStateEvent.Resume,
            PInvoke.PBT_APMPOWERSTATUSCHANGE => PowerStateEvent.StatusChange,
            _ => PowerStateEvent.Unknown
        };

        if (powerStateEvent == PowerStateEvent.Unknown) return;

        var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);
        if (!mi.Properties.SupportsAlwaysOnAc.status)
        {
            return;
        }

        Log.Instance.Trace($"Event value: {type}");

        if (powerStateEvent is not PowerStateEvent.Resume)
            return;

        await ProcessPowerEventAsync(powerStateEvent).ConfigureAwait(false);
    }

    /// <summary>A second resume this soon after the first is Windows' duplicate of it, not a new wake-up.</summary>
    internal static readonly TimeSpan DuplicateResumeWindow = TimeSpan.FromSeconds(10);

    /// <summary>Whether a resume at <paramref name="nowMs"/> repeats the one handled at <paramref name="lastResumeAtMs"/>.</summary>
    internal static bool IsDuplicateResume(long? lastResumeAtMs, long nowMs) =>
        lastResumeAtMs is { } last && nowMs - last < DuplicateResumeWindow.TotalMilliseconds;

    /// <summary>When the last resume was handled (steady clock); cleared by a suspend. Guarded by <see cref="_processingLock"/>.</summary>
    private long? _lastResumeAtMs;

    private async Task ProcessPowerEventAsync(PowerStateEvent powerStateEvent)
    {
        // Events are handled one at a time and none is dropped: a power adapter change or a suspend that arrives while
        // a slow resume is handled waits for it (it used to be dropped after 5 s).
        if (!await _processingLock.WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false))
        {
            Log.Instance.Trace($"Skipped {powerStateEvent}: the previous power event has been handled for over a minute.");
            return;
        }

        try
        {
            // Windows delivers resume through two routes (SystemEvents and the native callback) on some laptops. The
            // second copy used to run the whole resume sequence again; it is recognised by time now.
            switch (powerStateEvent)
            {
                case PowerStateEvent.Resume when IsDuplicateResume(_lastResumeAtMs, Environment.TickCount64):
                    Log.Instance.Trace($"Skipped a second resume event: Windows reported the same wake-up twice.");
                    return;
                case PowerStateEvent.Resume:
                    _lastResumeAtMs = Environment.TickCount64;
                    break;
                case PowerStateEvent.Suspend:
                    _lastResumeAtMs = null;
                    break;
            }

            var powerAdapterState = await Power.IsPowerAdapterConnectedAsync().ConfigureAwait(false);
            Log.Instance.Trace($"Handle {powerStateEvent}. [newState={powerAdapterState}]");

            var powerAdapterStateChanged = powerAdapterState != _lastPowerAdapterState;

            switch (powerStateEvent)
            {
                case PowerStateEvent.Suspend:
                    await HandleSuspendAsync().ConfigureAwait(false);
                    break;

                case PowerStateEvent.Resume:
                    await HandleResumeInternalAsync(powerAdapterState).ConfigureAwait(false);
                    break;

                case PowerStateEvent.StatusChange when powerAdapterState == PowerAdapterStatus.Connected:
                    if (powerAdapterStateChanged)
                    {
                        await HandleConnectedStatusChangeAsync().ConfigureAwait(false);
                    }

                    break;
            }

            HandlePowerStateChangeNotification(powerStateEvent, powerAdapterState);
        }
        finally
        {
            _processingLock.Release();
        }
    }

    private async Task HandleSuspendAsync()
    {
        if (await _powerModeFeature.IsSupportedAsync().ConfigureAwait(false) &&
            await _powerModeFeature.GetStateAsync().ConfigureAwait(false) == PowerModeState.GodMode)
        {
            Log.Instance.Trace($"Going to dark.");
            await _powerModeFeature.SuspendMode(PowerModeState.Balance).ConfigureAwait(false);
        }

        var feature = IoCContainer.Resolve<AmdOverclockingController>();
        if (feature.IsActive())
        {
            await feature.ResetAllActiveCoresCoAsync().ConfigureAwait(false);
        }

        if (HasExtensionProvider("FanControl"))
        {
            MessagingCenter.Publish(new FanStateMessage(FanState.Auto));
        }
    }

    /// <summary>
    /// Puts things back after sleep. Each step runs on its own: one that fails (say, the keyboard lighting) is logged
    /// and no longer stops the steps after it (the battery mode, God Mode).
    /// </summary>
    private async Task HandleResumeInternalAsync(PowerAdapterStatus currentAdapterStatus)
    {
        await ResumeStepAsync("battery mode", async () =>
        {
            if (await _batteryFeature.IsSupportedAsync().ConfigureAwait(false))
                await _batteryFeature.EnsureCorrectBatteryModeIsSetAsync().ConfigureAwait(false);
        }).ConfigureAwait(false);

        await ResumeStepAsync("keyboard lighting", async () =>
        {
            if (await _rgbController.IsSupportedAsync().ConfigureAwait(false))
                await _rgbController.SetLightControlOwnerAsync(true, true).ConfigureAwait(false);
        }).ConfigureAwait(false);

        await ResumeStepAsync("AMD overclocking", async () =>
        {
            var overclockingController = IoCContainer.Resolve<AmdOverclockingController>();
            if (!overclockingController.IsActive())
                return;

            var shouldApply = currentAdapterStatus == PowerAdapterStatus.Connected || overclockingController.AllowOnBattery;
            if (shouldApply)
            {
                Log.Instance.Trace($"Applying overclocking profile...");
                await overclockingController.ApplyInternalProfileAsync().ConfigureAwait(false);
            }
        }).ConfigureAwait(false);

        await ResumeStepAsync("GPU overclock", async () =>
        {
            var gpuOverclockController = IoCContainer.Resolve<GPUOverclockController>();
            if (await gpuOverclockController.IsSupportedAsync().ConfigureAwait(false))
                await gpuOverclockController.EnsureOverclockIsAppliedAsync().ConfigureAwait(false);
        }).ConfigureAwait(false);

        await ResumeStepAsync("power mode", async () =>
        {
            // The God Mode ranges are re-read on the next apply, in case BIOS settings changed while asleep.
            IoCContainer.Resolve<GodModeController>().ForgetFirmwareRanges();

            if (!await _powerModeFeature.IsSupportedAsync().ConfigureAwait(false))
                return;

            if (_powerModeFeature.LastPowerModeState == PowerModeState.GodMode)
            {
                Log.Instance.Trace($"Restore to {_powerModeFeature.LastPowerModeState}");
                await _powerModeFeature.SetStateAsync(_powerModeFeature.LastPowerModeState).ConfigureAwait(false);
            }

            await _powerModeFeature.EnsureGodModeStateIsAppliedAsync().ConfigureAwait(false);
        }).ConfigureAwait(false);

        if (HasExtensionProvider("FanControl"))
        {
            MessagingCenter.Publish(new FanStateMessage(FanState.Manual));
        }

        _ = NotifyDgpuAsync();
    }

    private static async Task ResumeStepAsync(string what, Func<Task> step)
    {
        try
        {
            await step().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Restoring {what} after sleep failed.", ex);
        }
    }

    private async Task HandleConnectedStatusChangeAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

        if (await _powerModeFeature.IsSupportedAsync().ConfigureAwait(false))
        {
            await _powerModeFeature.EnsureGodModeStateIsAppliedAsync().ConfigureAwait(false);
        }

        _ = NotifyDgpuAsync();
    }

    private static bool HasExtensionProvider(string capability) =>
        IoCContainer.TryResolve<IExtensionManager>()?.HasProvider(capability) ?? false;

    private async Task NotifyDgpuAsync()
    {
        var now = DateTime.Now;
        if ((now - _lastNotifyTime).TotalSeconds < 10)
        {
            Log.Instance.Trace($"NotifyDgpuAsync skipped due to debounce (last call at {_lastNotifyTime:HH:mm:ss})");
            return;
        }

        await _notifyLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if ((DateTime.Now - _lastNotifyTime).TotalSeconds < 10)
            {
                Log.Instance.Trace($"NotifyDgpuAsync skipped after lock (debounce)");
                return;
            }

            _lastNotifyTime = DateTime.Now;
        }
        finally
        {
            _notifyLock.Release();
        }

        try
        {
            if (await _dgpuNotify.IsSupportedAsync().ConfigureAwait(false))
            {
                await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                await _dgpuNotify.NotifyAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Error in NotifyDgpuAsync: {ex}");
        }
    }

    private void HandlePowerStateChangeNotification(PowerStateEvent powerStateEvent, PowerAdapterStatus newAdapterState)
    {
        var powerAdapterStateChanged = newAdapterState != _lastPowerAdapterState;
        _lastPowerAdapterState = newAdapterState;

        if (powerAdapterStateChanged)
        {
            Notify(newAdapterState);
        }

        Changed?.Invoke(this, new(powerStateEvent, powerAdapterStateChanged));
    }

    private unsafe void RegisterSuspendResumeNotification()
    {
        var result = PInvoke.PowerRegisterSuspendResumeNotification(REGISTER_NOTIFICATION_FLAGS.DEVICE_NOTIFY_CALLBACK, _recipientHandle, out var handle);
        _handle = result == WIN32_ERROR.ERROR_SUCCESS ? new HPOWERNOTIFY(new IntPtr(handle)) : HPOWERNOTIFY.Null;
    }

    private void UnRegisterSuspendResumeNotification()
    {
        if (_handle != HPOWERNOTIFY.Null)
        {
            PInvoke.PowerUnregisterSuspendResumeNotification(_handle);
            _handle = HPOWERNOTIFY.Null;
        }
    }

    private static void Notify(PowerAdapterStatus newState)
    {
        var msgType = newState switch
        {
            PowerAdapterStatus.Connected => NotificationType.ACAdapterConnected,
            PowerAdapterStatus.ConnectedLowWattage => NotificationType.ACAdapterConnectedLowWattage,
            PowerAdapterStatus.Disconnected => NotificationType.ACAdapterDisconnected,
            _ => (NotificationType?)null
        };

        if (msgType.HasValue)
        {
            MessagingCenter.Publish(new NotificationMessage(msgType.Value));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        StopAsync().GetAwaiter().GetResult();
        _processingLock.Dispose();
        _recipientHandle?.Dispose();

        _disposed = true;
    }
}