using System;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Controllers;

/// <summary>
/// Stops a GPU overclock that froze or crashed the laptop from being applied again at the next start.
/// <para>
/// Before values that haven't proven stable are applied, a note is saved to the settings file. The note is
/// removed once the app has stayed up with those values for a while (they are then remembered as stable) or when
/// the app exits normally. If a later run finds the note, the previous apply never got that far: the overclock is
/// switched off and the values are kept for the user to lower.
/// </para>
/// <para>
/// This only catches a crash soon after applying. A crash later under load is not detected here; the laptop still
/// starts normally and the user can lower the values.
/// </para>
/// </summary>
public class OverclockApplyGuard(GPUOverclockSettings settings)
{
    private readonly object _lock = new();
    private bool _previousRunChecked;

    /// <summary>How long the app must stay up after applying before the values count as stable.</summary>
    public static readonly TimeSpan StabilityWindow = TimeSpan.FromSeconds(60);

    /// <summary>Whether the overclock is off because a previous run's apply was never confirmed.</summary>
    public bool DisabledAfterInterruptedApply => settings.Store.DisabledAfterInterruptedApply;

    /// <summary>
    /// Checks, once per run and before anything is applied, whether the previous run left an apply unconfirmed.
    /// If so, turns the overclock off (keeping the values) and returns true.
    /// </summary>
    public bool RecoverFromInterruptedApply()
    {
        lock (_lock)
        {
            if (_previousRunChecked)
                return false;
            _previousRunChecked = true;

            var store = settings.Store;
            if (store.PendingInfo is not { } pending)
                return false;

            Log.Instance.Trace($"GPU overclock {pending} was being applied when the previous run ended. Turning overclock off.");

            store.Enabled = false;
            store.PendingInfo = null;
            store.DisabledAfterInterruptedApply = true;
            settings.SynchronizeStore();
            return true;
        }
    }

    /// <summary>
    /// Call right before applying <paramref name="info"/>. Saves the note unless the values are the stock ones
    /// or already proved stable. Returns true when a note was saved, i.e. <see cref="ConfirmStable"/> is due later.
    /// </summary>
    public bool Begin(GPUOverclockInfo info)
    {
        lock (_lock)
        {
            // This run is past the point where a note could be from the previous one.
            _previousRunChecked = true;

            var store = settings.Store;
            if (info == GPUOverclockInfo.Zero || store.StableInfo == info)
            {
                if (store.PendingInfo is not null)
                {
                    store.PendingInfo = null;
                    settings.SynchronizeStore();
                }
                return false;
            }

            if (store.PendingInfo == info)
                return true;

            store.PendingInfo = info;
            settings.SynchronizeStore();
            return true;
        }
    }

    /// <summary>The app stayed up with <paramref name="info"/> applied: remember it as stable and remove the note.</summary>
    public void ConfirmStable(GPUOverclockInfo info)
    {
        lock (_lock)
        {
            var store = settings.Store;
            if (store.PendingInfo != info)
                return;   // other values were applied in the meantime; their own confirmation is pending

            store.StableInfo = info;
            store.PendingInfo = null;
            settings.SynchronizeStore();
        }
    }

    /// <summary>
    /// The apply didn't happen (the driver refused), or the app is exiting normally: remove the note without
    /// calling the values stable.
    /// </summary>
    public void Abandon()
    {
        lock (_lock)
        {
            var store = settings.Store;
            if (store.PendingInfo is null)
                return;

            store.PendingInfo = null;
            settings.SynchronizeStore();
        }
    }

    /// <summary>The user turned the overclock on again after it was switched off for safety.</summary>
    public void AcknowledgeDisabled()
    {
        lock (_lock)
        {
            var store = settings.Store;
            if (!store.DisabledAfterInterruptedApply)
                return;

            store.DisabledAfterInterruptedApply = false;
            settings.SynchronizeStore();
        }
    }

    /// <summary>
    /// The clock offset to send: inside LLT's fixed limits and, when the driver reports a range for this GPU,
    /// inside that as well. A range of a single value (e.g. 0 to 0 when overclocking is locked) allows only that
    /// value. Values in MHz; the driver's are in kHz.
    /// </summary>
    public static int ClampDeltaMhz(int requestedMhz, int fixedMinMhz, int fixedMaxMhz, int? driverMinKhz, int? driverMaxKhz)
    {
        var min = fixedMinMhz;
        var max = fixedMaxMhz;

        if (driverMinKhz is { } dMin && driverMaxKhz is { } dMax && dMax >= dMin)
        {
            // Round inwards so the result is never outside the driver's range.
            min = Math.Max(min, (int)Math.Ceiling(dMin / 1000.0));
            max = Math.Min(max, (int)Math.Floor(dMax / 1000.0));
        }

        return max < min ? 0 : Math.Clamp(requestedMhz, min, max);
    }

    /// <summary>
    /// The voltage lock to send, or null when none may be sent: never above the highest voltage on the GPU's own
    /// voltage/frequency curve, and not at all when that curve couldn't be read.
    /// </summary>
    public static int? ClampVoltageLockMv(int requestedMv, int fixedMinMv, int fixedMaxMv, int? curveMaxMv)
    {
        if (curveMaxMv is not { } curveMax || curveMax <= 0)
            return null;

        var max = Math.Min(fixedMaxMv, curveMax);
        return max < fixedMinMv ? null : Math.Clamp(requestedMv, fixedMinMv, max);
    }
}
