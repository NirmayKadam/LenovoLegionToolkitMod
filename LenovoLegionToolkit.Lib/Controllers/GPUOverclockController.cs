using System;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Features.Hybrid;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.SoftwareDisabler;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;
using NvAPIWrapper.GPU;
using NvAPIWrapper.Native;
using NvAPIWrapper.Native.GPU;
using NvAPIWrapper.Native.GPU.Structures;

namespace LenovoLegionToolkit.Lib.Controllers;

/// <summary>What an attempt to apply the GPU overclock came to.</summary>
public enum GPUOverclockApplyResult
{
    /// <summary>The driver accepted the values.</summary>
    Applied,

    /// <summary>The clock offsets were applied, but no voltage lock: the GPU's own voltage range couldn't be read.</summary>
    AppliedWithoutVoltageLock,

    /// <summary>Nothing was sent (overclock off, GPU asleep or missing, or Lenovo's own software running).</summary>
    NotApplied,

    /// <summary>The driver refused the values, or part of them.</summary>
    Failed,

    /// <summary>The previous run's apply was never confirmed, so the overclock was switched off instead.</summary>
    DisabledAfterInterruptedApply,
}

public class GPUOverclockController
{
    private readonly OverclockApplyGuard _guard;
    private readonly GPUOverclockSettings _settings;
    private readonly VantageDisabler _vantageDisabler;
    private readonly LegionSpaceDisabler _legionSpaceDisabler;
    private readonly LegionZoneDisabler _legionZoneDisabler;
    private readonly NativeWindowsMessageListener _nativeWindowsMessageListener;

    public event EventHandler? Changed;

    public GPUOverclockController(GPUOverclockSettings settings,
        OverclockApplyGuard guard,
        VantageDisabler vantageDisabler,
        LegionSpaceDisabler legionSpaceDisabler,
        LegionZoneDisabler legionZoneDisabler,
        NativeWindowsMessageListener nativeWindowsMessageListener)
    {
        _settings = settings;
        _guard = guard;
        _vantageDisabler = vantageDisabler;
        _legionSpaceDisabler = legionSpaceDisabler;
        _legionZoneDisabler = legionZoneDisabler;
        _nativeWindowsMessageListener = nativeWindowsMessageListener;
        _nativeWindowsMessageListener.Changed += NativeWindowsMessageListenerOnChanged;
    }

    public async Task<bool> IsSupportedAsync()
    {
        bool isSupported;

        try
        {
            if (AppFlags.Instance.Debug)
            {
                return true;
            }

            NVAPI.Initialize();
            isSupported = NVAPI.GetGPU() is not null;
        }
        catch
        {
            isSupported = false;
        }

        Log.Instance.Trace($"NVAPI status: {isSupported}.");

        if (!isSupported)
            return isSupported;

        try
        {
            isSupported = await WMI.LenovoGameZoneData.IsSupportGpuOCAsync().ConfigureAwait(false) > 0;

            if (!isSupported)
            {
                Log.Instance.Trace($"Clearing settings...");

                _settings.Store.Enabled = false;
                _settings.Store.Info = GPUOverclockInfo.Zero;
                _settings.SynchronizeStore();

                // Nothing will be applied, so a pending "apply in progress" note would only cause a false warning.
                _guard.Abandon();
            }
        }
        catch
        {
            isSupported = false;
        }

        Log.Instance.Trace($"Supports GPU OC status: {isSupported}");

        return isSupported;
    }

    /// <summary>Whether the overclock is off because a previous run's apply was never confirmed stable.</summary>
    public bool WasDisabledAfterInterruptedApply => _guard.DisabledAfterInterruptedApply;

    public (bool, GPUOverclockInfo) GetState()
    {
        // Before anything in this run reads "enabled": an apply the previous run never confirmed turns it off.
        _guard.RecoverFromInterruptedApply();
        return (_settings.Store.Enabled, _settings.Store.Info);
    }

    public void SaveState(bool enabled, GPUOverclockInfo info)
    {
        Log.Instance.Trace($"Saved GPU overclock settings: [enabled={enabled}, info={info}].");

        _settings.Store.Enabled = enabled;
        _settings.Store.Info = info;
        _settings.SynchronizeStore();

        if (enabled)
            _guard.AcknowledgeDisabled();
    }

    /// <summary>The app is exiting normally: an apply still waiting to be confirmed is not held against the values.</summary>
    public void OnCleanExit() => _guard.Abandon();

    /// <summary>
    /// The highest voltage lock that may be sent to this GPU (the top of its own voltage/frequency curve, within
    /// LLT's fixed limit), or null when the curve can't be read right now.
    /// </summary>
    public int? GetVoltageLockLimitMv()
    {
        try
        {
            NVAPI.Initialize();
            return NVAPI.GetGPU() is { } gpu
                ? OverclockApplyGuard.ClampVoltageLockMv(GetMaxVoltageLockMv(), GetMinVoltageLockMv(), GetMaxVoltageLockMv(), ReadCurveMaxMv(gpu))
                : null;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't read the GPU's voltage range.", ex);
            return null;
        }
    }

    public async Task<GPUOverclockApplyResult> ApplyStateAsync(bool force = false)
    {
        if (_guard.RecoverFromInterruptedApply())
        {
            Changed?.Invoke(this, EventArgs.Empty);
            return GPUOverclockApplyResult.DisabledAfterInterruptedApply;
        }

        if (await _vantageDisabler.GetStatusAsync().ConfigureAwait(false) == SoftwareStatus.Enabled)
        {
            Log.Instance.Trace($"Can't correctly apply state when Vantage is running.");

            Changed?.Invoke(this, EventArgs.Empty);
            return GPUOverclockApplyResult.NotApplied;
        }

        if (await _legionSpaceDisabler.GetStatusAsync().ConfigureAwait(false) == SoftwareStatus.Enabled)
        {
            Log.Instance.Trace($"Can't correctly apply state when Legion Space is running.");

            Changed?.Invoke(this, EventArgs.Empty);
            return GPUOverclockApplyResult.NotApplied;
        }

        if (await _legionZoneDisabler.GetStatusAsync().ConfigureAwait(false) == SoftwareStatus.Enabled)
        {
            Log.Instance.Trace($"Can't correctly apply state when Legion Zone is running.");

            Changed?.Invoke(this, EventArgs.Empty);
            return GPUOverclockApplyResult.NotApplied;
        }

        if (IoCContainer.Resolve<HybridModeFeature>().ShouldKeepDGPUAsleep())
        {
            Log.Instance.Trace($"dGPU eject is being ensured — skipping overclock apply.");

            Changed?.Invoke(this, EventArgs.Empty);
            return GPUOverclockApplyResult.NotApplied;
        }

        var enabled = _settings.Store.Enabled;
        var info = _settings.Store.Info;

        if (force)
        {
            info = enabled ? info : GPUOverclockInfo.Zero;
            enabled = true;

            Log.Instance.Trace($"Forcing... [enabled=true, info={info}]");
        }

        if (!enabled)
        {
            Log.Instance.Trace($"Not enabled.");

            Changed?.Invoke(this, EventArgs.Empty);

            return GPUOverclockApplyResult.NotApplied;
        }

        Log.Instance.Trace($"Applying overclock: {info}.");

        try
        {
            NVAPI.Initialize();

            var gpu = NVAPI.GetGPU();
            if (gpu is null)
            {
                Log.Instance.Trace($"dGPU not found.");

                return GPUOverclockApplyResult.NotApplied;
            }

            // Saved before the driver is touched, so a freeze during or right after the apply is noticed at the
            // next start (see OverclockApplyGuard). Stock values and values already proven stable need no note.
            var unproven = _guard.Begin(info);

            var (clocksApplied, voltage) = SetOverclockInfo(gpu, info);

            if (!clocksApplied)
            {
                // Nothing took effect, so there is nothing to hold against these values.
                _guard.Abandon();
                Log.Instance.Trace($"The driver refused the overclock: {info}.");
                return GPUOverclockApplyResult.Failed;
            }

            if (unproven)
                _ = ConfirmStableLaterAsync(info);

            Log.Instance.Trace($"Applied overclock: {info}, voltage: {voltage}, current: {GetOverclockInfo(gpu)}.");

            return voltage switch
            {
                VoltageOutcome.Failed => GPUOverclockApplyResult.Failed,
                VoltageOutcome.SkippedNoCurve => GPUOverclockApplyResult.AppliedWithoutVoltageLock,
                _ => GPUOverclockApplyResult.Applied,
            };
        }
        catch (Exception ex)
        {
            // The saved values are kept: this may be the GPU waking up or the driver restarting, not bad values.
            Log.Instance.Trace($"Failed to apply overclock: {info}.", ex);
            return GPUOverclockApplyResult.Failed;
        }
        finally
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task ConfirmStableLaterAsync(GPUOverclockInfo info)
    {
        try
        {
            await Task.Delay(OverclockApplyGuard.StabilityWindow).ConfigureAwait(false);
            _guard.ConfirmStable(info);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't record the overclock as stable.", ex);
        }
    }

    public async Task<bool> EnsureOverclockIsAppliedAsync()
    {
        var (enabled, _) = GetState();
        if (!enabled)
            return false;

        await ApplyStateAsync().ConfigureAwait(false);
        return true;
    }

    private async void NativeWindowsMessageListenerOnChanged(object? sender, NativeWindowsMessageListener.ChangedEventArgs e)
    {
        if (e.Message is not NativeWindowsMessage.DisplayDeviceChanged and not NativeWindowsMessage.MonitorOn)
            return;

        if (await IsSupportedAsync().ConfigureAwait(false))
            await ApplyStateAsync().ConfigureAwait(false);
    }

    public static int GetMinCoreDeltaMhz() => -500;

    public static int GetMaxCoreDeltaMhz() => 500;

    public static int GetMinMemoryDeltaMhz() => -3000;

    public static int GetMaxMemoryDeltaMhz() => 3000;

    public static int GetMinVoltageLockMv() => 700;

    public static int GetMaxVoltageLockMv() => 1200;

    public static int GetMinVoltageCapMv() => 700;

    public static int GetMaxVoltageCapMv() => 1200;

    private enum VoltageOutcome
    {
        /// <summary>Voltage lock/cap set as asked, or reset to stock because none was asked for.</summary>
        Applied,

        /// <summary>A voltage lock was asked for but not sent: the GPU's own voltage range couldn't be read.</summary>
        SkippedNoCurve,

        /// <summary>The driver refused the voltage part.</summary>
        Failed,
    }

    /// <summary>
    /// The range of clock offsets the driver reports for one clock (graphics or memory) of this GPU, in kHz; null
    /// when it can't be read. The entry is picked by its clock domain, not by its position in the list.
    /// </summary>
    private static (int? Min, int? Max) ReadDriverDeltaRangeKhz(PhysicalGPU gpu, PublicClockDomain domain)
    {
        try
        {
            var entry = GPUApi.GetPerformanceStates20(gpu.Handle).Clocks[PerformanceStateId.P0_3DPerformance]
                .FirstOrDefault(c => c.DomainId == domain);
            if (entry is null)
                return (null, null);

            var range = entry.FrequencyDeltaInkHz.DeltaRange;
            return (range.Minimum, range.Maximum);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't read the driver's clock offset range.", ex);
            return (null, null);
        }
    }

    /// <summary>The highest voltage on the GPU's own voltage/frequency curve, in mV; null when it can't be read.</summary>
    private static int? ReadCurveMaxMv(PhysicalGPU gpu)
    {
        try
        {
            var max = GPUApi.GetClientClkVFPointsStatus(gpu.Handle).Points
                .Where(p => p.VoltageInMicroV > 0)
                .Select(p => (int)p.VoltageInMilliV)
                .DefaultIfEmpty(0)
                .Max();
            return max > 0 ? max : null;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't read the GPU's voltage/frequency curve.", ex);
            return null;
        }
    }

    private static (bool ClocksApplied, VoltageOutcome Voltage) SetOverclockInfo(PhysicalGPU gpu, GPUOverclockInfo info)
    {
        var (coreMinKhz, coreMaxKhz) = ReadDriverDeltaRangeKhz(gpu, PublicClockDomain.Graphics);
        var (memoryMinKhz, memoryMaxKhz) = ReadDriverDeltaRangeKhz(gpu, PublicClockDomain.Memory);

        var coreDelta = OverclockApplyGuard.ClampDeltaMhz(info.CoreDeltaMhz, GetMinCoreDeltaMhz(), GetMaxCoreDeltaMhz(), coreMinKhz, coreMaxKhz);
        var memoryDelta = OverclockApplyGuard.ClampDeltaMhz(info.MemoryDeltaMhz, GetMinMemoryDeltaMhz(), GetMaxMemoryDeltaMhz(), memoryMinKhz, memoryMaxKhz);

        if (coreDelta != info.CoreDeltaMhz || memoryDelta != info.MemoryDeltaMhz)
            Log.Instance.Trace($"Clock offsets limited to the allowed range. [core={info.CoreDeltaMhz}->{coreDelta}, memory={info.MemoryDeltaMhz}->{memoryDelta}]");

        var clocksApplied = true;
        var voltageOutcome = VoltageOutcome.Applied;

        var clockEntries = new[]
        {
            new PerformanceStates20ClockEntryV1(PublicClockDomain.Graphics, new PerformanceStates20ParameterDelta(coreDelta * 1000)),
            new PerformanceStates20ClockEntryV1(PublicClockDomain.Memory, new PerformanceStates20ParameterDelta(memoryDelta * 1000))
        };
        var voltageEntries = Array.Empty<PerformanceStates20BaseVoltageEntryV1>();
        var performanceStateInfo = new[] { new PerformanceStates20InfoV1.PerformanceState20(PerformanceStateId.P0_3DPerformance, clockEntries, voltageEntries) };

        try
        {
            var overclock = new PerformanceStates20InfoV1(performanceStateInfo, 2, 0);
            GPUApi.SetPerformanceStates20(gpu.Handle, overclock);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to apply performance states.", ex);
            clocksApplied = false;
        }

        try
        {
            if (info.VoltageCapMv > 0)
            {
                var voltageCap = Math.Clamp(info.VoltageCapMv, GetMinVoltageCapMv(), GetMaxVoltageCapMv());

                var voltageResetEntry = new PrivateClockBoostLockV2.ClockBoostLock(
                    PublicClockDomain.Voltage,
                    ClockLockMode.None,
                    0
                );
                var graphicsResetEntry = new PrivateClockBoostLockV2.ClockBoostLock(
                    PublicClockDomain.Graphics,
                    ClockLockMode.None,
                    0
                );

                GPUApi.SetClockBoostLock(gpu.Handle, new PrivateClockBoostLockV2(new[] { voltageResetEntry }));
                GPUApi.SetClockBoostLock(gpu.Handle, new PrivateClockBoostLockV2(new[] { graphicsResetEntry }));

                var pointsStatus = GPUApi.GetClientClkVFPointsStatus(gpu.Handle);
                var points = pointsStatus.Points;

                var targetIndex = -1;
                for (var i = 0; i < points.Length; i++)
                {
                    if (points[i].VoltageInMicroV > 0 && points[i].VoltageInMilliV >= voltageCap)
                    {
                        targetIndex = i;
                        break;
                    }
                }

                if (targetIndex == -1)
                {
                    for (var i = points.Length - 1; i >= 0; i--)
                    {
                        if (points[i].VoltageInMicroV > 0)
                        {
                            targetIndex = i;
                            break;
                        }
                    }
                }

                if (targetIndex >= 0)
                {
                    var targetPoint = points[targetIndex];
                    Log.Instance.Trace($"Voltage cap {voltageCap} mV matched curve point #{targetIndex} ({targetPoint.VoltageInMilliV} mV @ {targetPoint.FrequencyInkHz / 1000} MHz).");

                    var gpuDeltas = new PrivateClockBoostTableV1.GPUDelta[points.Length];
                    for (var i = 0; i < points.Length; i++)
                    {
                        var p = points[i];
                        var delta = (p.VoltageInMicroV > targetPoint.VoltageInMicroV && p.FrequencyInkHz > targetPoint.FrequencyInkHz)
                            ? (int)targetPoint.FrequencyInkHz - (int)p.FrequencyInkHz
                            : 0;
                        gpuDeltas[i] = new PrivateClockBoostTableV1.GPUDelta(delta);
                    }

                    GPUApi.SetClockBoostTable(gpu.Handle, new PrivateClockBoostTableV1(gpuDeltas));
                }
                else
                {
                    Log.Instance.Trace($"No matching V/F curve point found for voltage cap {voltageCap} mV.");
                }
            }
            else if (info.VoltageLockMv > 0
                     && OverclockApplyGuard.ClampVoltageLockMv(info.VoltageLockMv, GetMinVoltageLockMv(), GetMaxVoltageLockMv(), ReadCurveMaxMv(gpu)) is { } voltageLock)
            {
                // Never above the top of the GPU's own voltage/frequency curve.
                if (voltageLock != info.VoltageLockMv)
                    Log.Instance.Trace($"Voltage lock limited to the GPU's own range. [{info.VoltageLockMv}->{voltageLock} mV]");

                var voltageLockEntry = new PrivateClockBoostLockV2.ClockBoostLock(
                    PublicClockDomain.Voltage,
                    ClockLockMode.Manual,
                    (uint)(voltageLock * 1000)
                );
                var graphicsResetEntry = new PrivateClockBoostLockV2.ClockBoostLock(
                    PublicClockDomain.Graphics,
                    ClockLockMode.None,
                    0
                );

                GPUApi.SetClockBoostLock(gpu.Handle, new PrivateClockBoostLockV2(new[] { voltageLockEntry }));
                GPUApi.SetClockBoostLock(gpu.Handle, new PrivateClockBoostLockV2(new[] { graphicsResetEntry }));

                GPUApi.SetClockBoostTable(gpu.Handle, new PrivateClockBoostTableV1(Array.Empty<PrivateClockBoostTableV1.GPUDelta>()));
            }
            else
            {
                // No voltage setting asked for, or a lock was asked for but the GPU's own range couldn't be
                // read: in both cases the voltage goes back to stock rather than to an unchecked number.
                if (info.VoltageLockMv > 0)
                {
                    Log.Instance.Trace($"Voltage lock {info.VoltageLockMv} mV not applied: the GPU's voltage range couldn't be read.");
                    voltageOutcome = VoltageOutcome.SkippedNoCurve;
                }

                var voltageResetEntry = new PrivateClockBoostLockV2.ClockBoostLock(
                    PublicClockDomain.Voltage,
                    ClockLockMode.None,
                    0
                );
                var graphicsResetEntry = new PrivateClockBoostLockV2.ClockBoostLock(
                    PublicClockDomain.Graphics,
                    ClockLockMode.None,
                    0
                );

                GPUApi.SetClockBoostLock(gpu.Handle, new PrivateClockBoostLockV2(new[] { voltageResetEntry }));
                GPUApi.SetClockBoostLock(gpu.Handle, new PrivateClockBoostLockV2(new[] { graphicsResetEntry }));

                GPUApi.SetClockBoostTable(gpu.Handle, new PrivateClockBoostTableV1(Array.Empty<PrivateClockBoostTableV1.GPUDelta>()));
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to apply undervolting settings.", ex);
            voltageOutcome = VoltageOutcome.Failed;
        }

        return (clocksApplied, voltageOutcome);
    }

    private static GPUOverclockInfo GetOverclockInfo(PhysicalGPU gpu)
    {
        var states = GPUApi.GetPerformanceStates20(gpu.Handle);
        var core = states.Clocks[PerformanceStateId.P0_3DPerformance][0].FrequencyDeltaInkHz.DeltaValue / 1000;
        var memory = states.Clocks[PerformanceStateId.P0_3DPerformance][1].FrequencyDeltaInkHz.DeltaValue / 1000;

        int voltageLock = 0;
        try
        {
            var clockLock = GPUApi.GetClockBoostLock(gpu.Handle, PublicClockDomain.Voltage);
            if (clockLock.ClockBoostLocks.Length > 0 && clockLock.ClockBoostLocks[0].LockMode == ClockLockMode.Manual)
            {
                voltageLock = (int)(clockLock.ClockBoostLocks[0].VoltageInMicroV / 1000);
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to read active voltage lock.", ex);
        }

        int voltageCap = 0;
        try
        {
            var boostTable = GPUApi.GetClockBoostTable(gpu.Handle);
            var deltas = boostTable.GPUDeltas;
            if (deltas != null && deltas.Any(d => d.FrequencyDeltaInkHz < 0))
            {
                var pointsStatus = GPUApi.GetClientClkVFPointsStatus(gpu.Handle);
                var points = pointsStatus.Points;

                for (var i = 0; i < Math.Min(points.Length, deltas.Length); i++)
                {
                    if (deltas[i].FrequencyDeltaInkHz < 0 && i > 0 && points[i - 1].VoltageInMicroV > 0)
                    {
                        voltageCap = (int)points[i - 1].VoltageInMilliV;
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to read active voltage cap.", ex);
        }

        return new(core, memory, voltageLock, voltageCap);
    }
}
