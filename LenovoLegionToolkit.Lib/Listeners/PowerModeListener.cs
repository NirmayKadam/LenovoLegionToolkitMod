using System;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
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

    protected override async Task OnChangedAsync(PowerModeState value)
    {
        Log.Instance.Trace($"PowerModeListener.OnChangedAsync (WMI event path): value={value}");
        var sw = Stopwatch.StartNew();
        try
        {
            await ChangeDependenciesAsync(value).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Each failed step is logged where it failed. The mode did change (Fn+Q), so it is still announced and
            // the Changed event still follows: the dashboard and the fan curves keep up with the laptop.
            Log.Instance.Trace($"Not everything for {value} could be set; the change is still reported.", ex);
        }
        Log.Instance.Trace($"ChangeDependenciesAsync completed [elapsed={sw.ElapsedMilliseconds}ms]");
        PublishNotification(value);
    }

    public async Task NotifyAsync(PowerModeState value)
    {
        Log.Instance.Trace($"PowerModeListener.NotifyAsync (explicit path): value={value}");
        var sw = Stopwatch.StartNew();
        ExceptionDispatchInfo? failure = null;
        try
        {
            await ChangeDependenciesAsync(value).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            failure = ExceptionDispatchInfo.Capture(ex);
        }
        Log.Instance.Trace($"ChangeDependenciesAsync completed [elapsed={sw.ElapsedMilliseconds}ms]");

        // The mode is set either way, so the change is reported; the caller still gets the error to show.
        RaiseChanged(value);
        failure?.Throw();
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
    private async Task ChangeDependenciesAsync(PowerModeState value)
    {
        var sw = Stopwatch.StartNew();

        await RunInOrderAsync(
            ("God Mode", () => ApplyGodModeSideAsync(value)),
            ("Windows power mode", () => windowsPowerModeController.SetPowerModeAsync(value)),
            ("Windows power plan", () => windowsPowerPlanController.SetPowerPlanAsync(value)),
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

    private async Task ApplyGodModeSideAsync(PowerModeState value)
    {
        if (value is PowerModeState.GodMode)
        {
            Log.Instance.Trace($"Delaying GodMode apply...");
            await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);

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
