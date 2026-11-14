using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers.GodMode;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Controllers;

/// <summary>A value a power mode sets.</summary>
public enum PowerModeValueKind
{
    CpuLongTermPowerLimit,
    CpuShortTermPowerLimit,
    CpuPeakPowerLimit,
    CpuCrossLoadingPowerLimit,
    CpuPl2Tau,
    ApuSpptPowerLimit,
    CpuTemperatureLimit,
    GpuConfigurableTgp,
    GpuPowerBoost,
    GpuToCpuDynamicBoost,
    GpuTemperatureLimit,
}

/// <summary>Where a mode's fan curve comes from.</summary>
public enum FanCurveSource
{
    /// <summary>The firmware's built-in curve for that mode. Lenovo doesn't expose it, so it isn't shown.</summary>
    Firmware,

    /// <summary>The active Custom (God Mode) preset's curve.</summary>
    CustomPreset,

    /// <summary>A curve the user set for this mode, and the last write of it was kept.</summary>
    UserCurve,

    /// <summary>A curve the user set for this mode that hasn't been written yet in this run.</summary>
    UserCurvePending,

    /// <summary>The user set a curve, but the last attempt wasn't kept (or failed): the firmware's curve is in use.</summary>
    UserCurveNotKept,
}

/// <summary>
/// What a power mode sets, as far as the laptop reveals it. Only values the firmware or preset actually
/// provides are present; a value of 0 is treated as "not provided".
/// </summary>
public sealed record PowerModeProfile(
    PowerModeState Mode,
    IReadOnlyDictionary<PowerModeValueKind, int> Values,
    FanCurveSource FanSource,
    ushort[]? FanTable,
    GPUOverclockInfo? GpuClockOffsets);

/// <summary>One row of the before/after comparison.</summary>
public sealed record PowerModeValueChange(PowerModeValueKind Kind, int? Previous, int? Current)
{
    public bool Changed => Previous != Current;
}

/// <summary>
/// Reads what each power mode sets:
/// <list type="bullet">
/// <item>Quiet/Balance/Performance/Extreme: the firmware's own values for that mode
/// (<see cref="IGodModeController.GetDefaultsInOtherPowerModesAsync"/>);</item>
/// <item>Custom: the active God Mode preset.</item>
/// </list>
/// </summary>
public class PowerModeProfileReader(IGodModeController godModeController, GPUOverclockController gpuOverclockController)
{
    /// <summary>
    /// Optional per-mode user fan curves (set by the fan-profile feature): the curve and whether it is in use.
    /// </summary>
    public Func<PowerModeState, (ushort[]? Curve, FanCurveSource Source)>? UserFanCurve { get; set; }

    public async Task<PowerModeProfile> GetProfileAsync(PowerModeState mode)
    {
        GPUOverclockInfo? offsets = null;
        try
        {
            var (enabled, info) = gpuOverclockController.GetState();
            offsets = enabled ? info : null;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't read GPU clock offsets.", ex);
        }

        if (mode == PowerModeState.GodMode)
        {
            var (_, saved) = await godModeController.GetActivePresetAsync().ConfigureAwait(false);
            var sent = await godModeController.GetRangeCheckedActivePresetAsync().ConfigureAwait(false);
            return FromPreset(saved, sent, offsets);
        }

        var defaults = await godModeController.GetDefaultsInOtherPowerModesAsync().ConfigureAwait(false);
        defaults.TryGetValue(mode, out var modeDefaults);
        var (userCurve, userSource) = UserFanCurve?.Invoke(mode) ?? (null, FanCurveSource.Firmware);
        return FromFirmwareDefaults(mode, modeDefaults, userCurve, offsets, userSource);
    }

    /// <summary>
    /// The values the firmware reports for a built-in mode (exposed for tests). <paramref name="userCurveSource"/>
    /// says whether the user's curve is in use; when it wasn't kept, no curve is shown (the firmware's isn't known).
    /// </summary>
    public static PowerModeProfile FromFirmwareDefaults(PowerModeState mode, GodModeDefaults? defaults, ushort[]? userFanCurve, GPUOverclockInfo? offsets, FanCurveSource userCurveSource = FanCurveSource.UserCurve)
    {
        var values = new Dictionary<PowerModeValueKind, int>();
        if (defaults is { } d)
        {
            Add(values, PowerModeValueKind.CpuLongTermPowerLimit, d.CPULongTermPowerLimit);
            Add(values, PowerModeValueKind.CpuShortTermPowerLimit, d.CPUShortTermPowerLimit);
            Add(values, PowerModeValueKind.CpuPeakPowerLimit, d.CPUPeakPowerLimit);
            Add(values, PowerModeValueKind.CpuCrossLoadingPowerLimit, d.CPUCrossLoadingPowerLimit);
            Add(values, PowerModeValueKind.CpuPl2Tau, d.CPUPL2Tau);
            Add(values, PowerModeValueKind.ApuSpptPowerLimit, d.APUsPPTPowerLimit);
            Add(values, PowerModeValueKind.CpuTemperatureLimit, d.CPUTemperatureLimit);
            Add(values, PowerModeValueKind.GpuConfigurableTgp, d.GPUConfigurableTGP);
            Add(values, PowerModeValueKind.GpuPowerBoost, d.GPUPowerBoost);
            Add(values, PowerModeValueKind.GpuToCpuDynamicBoost, d.GPUToCPUDynamicBoost);
            Add(values, PowerModeValueKind.GpuTemperatureLimit, d.GPUTemperatureLimit);
        }

        // The defaults' FanTable is LLT's placeholder level table, not the firmware's curve, so it isn't shown.
        if (userCurveSource == FanCurveSource.UserCurveNotKept)
            return new PowerModeProfile(mode, values, FanCurveSource.UserCurveNotKept, null, offsets);

        return userFanCurve is { Length: > 0 } && userCurveSource is FanCurveSource.UserCurve or FanCurveSource.UserCurvePending
            ? new PowerModeProfile(mode, values, userCurveSource, userFanCurve, offsets)
            : new PowerModeProfile(mode, values, FanCurveSource.Firmware, null, offsets);
    }

    /// <summary>
    /// The values a Custom (God Mode) preset sets, as they are sent: <paramref name="sent"/> is the preset fitted to
    /// the firmware's ranges, and only settings the <paramref name="saved"/> preset has are listed (the same rule
    /// as applying). When the ranges couldn't be read (<paramref name="sent"/> is null), no limits are sent, so
    /// none are listed. Exposed for tests.
    /// </summary>
    public static PowerModeProfile FromPreset(GodModeSettings.GodModeSettingsStore.Preset saved, GodModePreset? sent, GPUOverclockInfo? offsets)
    {
        int? Sent(StepperValue? savedValue, StepperValue? sentValue) => savedValue is null ? null : sentValue?.Value;

        var values = new Dictionary<PowerModeValueKind, int>();
        if (sent is { } s)
        {
            Add(values, PowerModeValueKind.CpuLongTermPowerLimit, Sent(saved.CPULongTermPowerLimit, s.CPULongTermPowerLimit));
            Add(values, PowerModeValueKind.CpuShortTermPowerLimit, Sent(saved.CPUShortTermPowerLimit, s.CPUShortTermPowerLimit));
            Add(values, PowerModeValueKind.CpuPeakPowerLimit, Sent(saved.CPUPeakPowerLimit, s.CPUPeakPowerLimit));
            Add(values, PowerModeValueKind.CpuCrossLoadingPowerLimit, Sent(saved.CPUCrossLoadingPowerLimit, s.CPUCrossLoadingPowerLimit));
            Add(values, PowerModeValueKind.CpuPl2Tau, Sent(saved.CPUPL2Tau, s.CPUPL2Tau));
            Add(values, PowerModeValueKind.ApuSpptPowerLimit, Sent(saved.APUsPPTPowerLimit, s.APUsPPTPowerLimit));
            Add(values, PowerModeValueKind.CpuTemperatureLimit, Sent(saved.CPUTemperatureLimit, s.CPUTemperatureLimit));
            Add(values, PowerModeValueKind.GpuConfigurableTgp, Sent(saved.GPUConfigurableTGP, s.GPUConfigurableTGP));
            Add(values, PowerModeValueKind.GpuPowerBoost, Sent(saved.GPUPowerBoost, s.GPUPowerBoost));
            Add(values, PowerModeValueKind.GpuToCpuDynamicBoost, Sent(saved.GPUToCPUDynamicBoost, s.GPUToCPUDynamicBoost));
            Add(values, PowerModeValueKind.GpuTemperatureLimit, Sent(saved.GPUTemperatureLimit, s.GPUTemperatureLimit));
        }

        // A saved fan table below the minimum is replaced before it is sent; the checked preset carries that result.
        var table = saved.FanTable is null ? null : (sent?.FanTableInfo?.Table ?? saved.FanTable)?.GetTable();
        return new PowerModeProfile(PowerModeState.GodMode, values,
            table is { Length: > 0 } ? FanCurveSource.CustomPreset : FanCurveSource.Firmware,
            table is { Length: > 0 } ? table : null,
            offsets);
    }

    /// <summary>
    /// Every value either profile has, in a fixed order, with its before and after (null when not provided).
    /// With no previous profile, everything counts as unchanged.
    /// </summary>
    public static IReadOnlyList<PowerModeValueChange> Compare(PowerModeProfile? previous, PowerModeProfile current)
    {
        var kinds = current.Values.Keys.Concat(previous?.Values.Keys ?? []).Distinct().Order();
        return kinds.Select(k =>
        {
            int? now = current.Values.TryGetValue(k, out var c) ? c : null;
            int? before = previous is null ? now : previous.Values.TryGetValue(k, out var p) ? p : null;
            return new PowerModeValueChange(k, before, now);
        }).ToList();
    }

    /// <summary>
    /// The values the laptop reports right now (CPU/GPU limits), for checking that a mode really applied.
    /// Values the firmware doesn't answer are left out.
    /// </summary>
    public static async Task<IReadOnlyDictionary<PowerModeValueKind, int>> ReadLiveValuesAsync()
    {
        var result = new Dictionary<PowerModeValueKind, int>();

        async Task TryRead(PowerModeValueKind kind, Func<Task<int>> read)
        {
            try
            {
                var value = await read().ConfigureAwait(false);
                if (value > 0)
                    result[kind] = value;
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Live value {kind} not available.", ex);
            }
        }

        await TryRead(PowerModeValueKind.CpuLongTermPowerLimit, async () => (await WMI.LenovoCpuMethod.CPUGetLongTermPowerLimitAsync().ConfigureAwait(false)).value).ConfigureAwait(false);
        await TryRead(PowerModeValueKind.CpuShortTermPowerLimit, async () => (await WMI.LenovoCpuMethod.CPUGetShortTermPowerLimitAsync().ConfigureAwait(false)).value).ConfigureAwait(false);
        await TryRead(PowerModeValueKind.CpuPeakPowerLimit, async () => (await WMI.LenovoCpuMethod.CPUGetPeakPowerLimitAsync().ConfigureAwait(false)).value).ConfigureAwait(false);
        await TryRead(PowerModeValueKind.CpuTemperatureLimit, async () => (await WMI.LenovoCpuMethod.CPUGetTemperatureControlAsync().ConfigureAwait(false)).value).ConfigureAwait(false);
        await TryRead(PowerModeValueKind.GpuConfigurableTgp, async () => (await WMI.LenovoGpuMethod.GPUGetCTGPPowerLimitAsync().ConfigureAwait(false)).value).ConfigureAwait(false);
        await TryRead(PowerModeValueKind.GpuPowerBoost, async () => (await WMI.LenovoGpuMethod.GPUGetPPABPowerLimitAsync().ConfigureAwait(false)).value).ConfigureAwait(false);
        await TryRead(PowerModeValueKind.GpuTemperatureLimit, async () => (await WMI.LenovoGpuMethod.GPUGetTemperatureLimitAsync().ConfigureAwait(false)).value).ConfigureAwait(false);

        return result;
    }

    private static void Add(Dictionary<PowerModeValueKind, int> values, PowerModeValueKind kind, int? value)
    {
        if (value is > 0)
            values[kind] = value.Value;
    }
}
