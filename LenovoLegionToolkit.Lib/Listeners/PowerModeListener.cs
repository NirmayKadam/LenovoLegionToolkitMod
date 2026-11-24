using System;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Controllers.GodMode;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.Overclocking.Amd;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Listeners;

public class PowerModeListener(
    GodModeController godModeController,
    WindowsPowerModeController windowsPowerModeController,
    WindowsPowerPlanController windowsPowerPlanController)
    : AbstractWMIListener<PowerModeListener.ChangedEventArgs, PowerModeState, int>(WMI.LenovoGameZoneSmartFanModeEvent.Listen), INotifyingListener<PowerModeListener.ChangedEventArgs, PowerModeState>
{
    public class ChangedEventArgs(PowerModeState state) : EventArgs
    {
        public PowerModeState State { get; } = state;
    }

    protected override PowerModeState GetValue(int value)
    {
        var result = (PowerModeState)(value - 1);
        return result;
    }

    protected override ChangedEventArgs GetEventArgs(PowerModeState value) => new(value);

    private readonly SemaphoreSlim _changeLock = new(1, 1);
    private PowerModeState? _lastState;

    // Changed is raised by ProcessChangeAsync once the follow-up steps ran, not straight after the WMI event.
    protected override bool RaiseChangedAutomatically => false;

    /// <summary>Fn+Q or the firmware changed the mode: the same mode twice in a row is handled once.</summary>
    protected override Task OnChangedAsync(PowerModeState value) =>
        ProcessChangeAsync(value, rethrowStepFailure: false);

    public Task NotifyAsync(PowerModeState value) =>
        ProcessChangeAsync(value, force: true, showNotification: false);

    /// <summary>
    /// Runs <paramref name="change"/> (the actual mode switch) and everything that follows it as one step, so a WMI
    /// event raised by the switch itself waits instead of applying the same mode a second time.
    /// </summary>
    public Task ChangeAsync(PowerModeState value, Func<Task> change, bool applyGodModePreset = true) =>
        ProcessChangeAsync(value, change, applyGodModePreset, force: true, showNotification: false);

    private async Task ProcessChangeAsync(PowerModeState value, Func<Task>? change = null,
        bool applyGodModePreset = true, bool force = false, bool showNotification = true, bool rethrowStepFailure = true)
    {
        await _changeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!force && _lastState == value)
            {
                Log.Instance.Trace($"Power mode {value} already handled; skipping the repeat.");
                return;
            }

            _lastState = null;
            if (change is not null)
                await change().ConfigureAwait(false);

            if (showNotification)
                PublishNotification(value);

            Log.Instance.Trace($"Power mode changed to {value}. [force={force}, applyGodModePreset={applyGodModePreset}]");
            var sw = Stopwatch.StartNew();
            ExceptionDispatchInfo? failure = null;
            try
            {
                await ChangeDependenciesAsync(value, applyGodModePreset).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Each failed step is logged where it failed. The mode did change, so it is still reported: the
                // dashboard and the fan curves keep up with the laptop. An explicit change still gets the error to show.
                Log.Instance.Trace($"Not everything for {value} could be set; the change is still reported.", ex);
                failure = ExceptionDispatchInfo.Capture(ex);
            }
            Log.Instance.Trace($"ChangeDependenciesAsync completed [elapsed={sw.ElapsedMilliseconds}ms]");

            RaiseChanged(value);
            _lastState = value;

            if (rethrowStepFailure)
                failure?.Throw();
        }
        finally
        {
            _changeLock.Release();
        }
    }
    protected override async Task<bool> CanStartAsync()
    {
        var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);
        return Compatibility.IsLegion(mi.LegionSeries);
    }

    /// <summary>
    /// Everything that follows a power-mode change. A step that fails (the firmware refusing a Custom-mode limit, for
    /// example) no longer skips the ones after it: the Windows power mode and plan still follow the new mode.
    /// </summary>
    private async Task ChangeDependenciesAsync(PowerModeState value, bool applyGodModePreset = true)
    {
        var sw = Stopwatch.StartNew();

        await RunInOrderAsync(
            ("God Mode", () => ApplyGodModeSideAsync(value, applyGodModePreset)),
            ("Windows power mode", () => windowsPowerModeController.SetPowerModeAsync(value, skipThrottle: true)),
            ("Windows power plan", () => windowsPowerPlanController.SetPowerPlanAsync(value, skipThrottle: true)),
            ("GPU overclock", ScheduleGpuOverclockAsync),
            ("AMD overclocking", ApplyAmdDefaultsIfNeededAsync)
        ).ConfigureAwait(false);

        Log.Instance.Trace($"ChangeDependenciesAsync total [elapsed={sw.ElapsedMilliseconds}ms]");
    }

    /// <summary>
    /// Runs every step in order, even after one fails; each failure is logged. Afterwards the first failure is thrown
    /// again, so the caller still learns that not everything was set.
    /// </summary>
    internal static async Task RunInOrderAsync(params (string Name, Func<Task> Step)[] steps)
    {
        ExceptionDispatchInfo? firstFailure = null;

        foreach (var (name, step) in steps)
        {
            try
            {
                await step().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Power mode change: {name} failed; carrying on with the next step.", ex);
                firstFailure ??= ExceptionDispatchInfo.Capture(ex);
            }
        }

        firstFailure?.Throw();
    }

    private async Task ApplyGodModeSideAsync(PowerModeState value, bool applyGodModePreset)
    {
        if (value is PowerModeState.GodMode)
        {
            Log.Instance.Trace($"Delaying GodMode apply... [applyGodModePreset={applyGodModePreset}]");
            await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);

            if (!applyGodModePreset)
            {
                Log.Instance.Trace($"Suppressed God Mode preset apply, caller takes over.");
                return;
            }

            Log.Instance.Trace($"Calling GodModeController.ApplyStateAsync...");
            var godSw = Stopwatch.StartNew();
            await godModeController.ApplyStateAsync().ConfigureAwait(false);
            Log.Instance.Trace($"ApplyStateAsync completed [elapsed={godSw.ElapsedMilliseconds}ms]");
        }
        else
        {
            Log.Instance.Trace($"Delaying restore defaults in other power mode...");
            await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);

            Log.Instance.Trace($"Calling GodModeController.RestoreDefaultsInOtherPowerModeAsync({value})...");
            var restSw = Stopwatch.StartNew();
            await godModeController.RestoreDefaultsInOtherPowerModeAsync(value).ConfigureAwait(false);
            Log.Instance.Trace($"RestoreDefaultsInOtherPowerModeAsync completed [elapsed={restSw.ElapsedMilliseconds}ms]");
        }
    }

    private static async Task ScheduleGpuOverclockAsync()
    {
        var gpuOverclockController = IoCContainer.Resolve<GPUOverclockController>();
        Log.Instance.Trace($"Checking GPUOverclock IsSupportedAsync...");
        if (!await gpuOverclockController.IsSupportedAsync().ConfigureAwait(false))
            return;

        Log.Instance.Trace($"GPU overclock supported, scheduling re-apply after 1s");
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                await gpuOverclockController.EnsureOverclockIsAppliedAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Re-applying the GPU overclock after the mode change failed.", ex);
            }
        });
    }

    private static async Task ApplyAmdDefaultsIfNeededAsync()
    {
        var amdOverclockingController = IoCContainer.Resolve<AmdOverclockingController>();
        if (amdOverclockingController.IsActive() && !amdOverclockingController.AllowInAllPowerModes)
        {
            Log.Instance.Trace($"Applying AMD OC default profile...");
            await amdOverclockingController.ApplyDefaultProfileAsync().ConfigureAwait(false);
        }
    }

    private static void PublishNotification(PowerModeState value)
    {
        switch (value)
        {
            case PowerModeState.Quiet:
                MessagingCenter.Publish(new NotificationMessage(NotificationType.PowerModeQuiet, value.GetDisplayName()));
                break;
            case PowerModeState.Balance:
                MessagingCenter.Publish(new NotificationMessage(NotificationType.PowerModeBalance, value.GetDisplayName()));
                break;
            case PowerModeState.Performance:
                MessagingCenter.Publish(new NotificationMessage(NotificationType.PowerModePerformance, value.GetDisplayName()));
                break;
            case PowerModeState.Extreme:
                MessagingCenter.Publish(new NotificationMessage(NotificationType.PowerModeExtreme, value.GetDisplayName()));
                break;
            case PowerModeState.GodMode:
                MessagingCenter.Publish(new NotificationMessage(NotificationType.PowerModeGodMode, value.GetDisplayName()));
                break;
        }
    }
}
