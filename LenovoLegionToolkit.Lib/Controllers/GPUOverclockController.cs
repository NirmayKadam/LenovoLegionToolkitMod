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
using NvAPIWrapper.Native.Exceptions;
using NvAPIWrapper.Native.General;
using NvAPIWrapper.Native.GPU;
using NvAPIWrapper.Native.GPU.Structures;
using NvAPIWrapper.Native.Interfaces.GPU;
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

    /// <summary>Nothing was sent because MSI Afterburner is running and manages the GPU clocks. The settings are kept.</summary>
    SkippedForAfterburner,
}

public class GPUOverclockController
{
    private readonly OverclockApplyGuard _guard;
    private readonly GPUOverclockSettings _settings;
    private readonly VantageDisabler _vantageDisabler;
    private readonly LegionSpaceDisabler _legionSpaceDisabler;
    private readonly LegionZoneDisabler _legionZoneDisabler;
    private readonly Func<bool> _isAfterburnerRunning;

    public event EventHandler? Changed;

    public GPUOverclockController(GPUOverclockSettings settings,
        OverclockApplyGuard guard,
        VantageDisabler vantageDisabler,
        LegionSpaceDisabler legionSpaceDisabler,
        LegionZoneDisabler legionZoneDisabler,
        NativeWindowsMessageListener nativeWindowsMessageListener)
        : this(settings, guard, vantageDisabler, legionSpaceDisabler, legionZoneDisabler, AfterburnerDetector.IsRunning)
    {
        nativeWindowsMessageListener.Changed += NativeWindowsMessageListenerOnChanged;
    }

    /// <summary>For tests: whether MSI Afterburner runs is supplied by the caller; display changes aren't listened to.</summary>
    internal GPUOverclockController(GPUOverclockSettings settings,
        OverclockApplyGuard guard,
        VantageDisabler vantageDisabler,
        LegionSpaceDisabler legionSpaceDisabler,
        LegionZoneDisabler legionZoneDisabler,
        Func<bool> isAfterburnerRunning)
    {
        _settings = settings;
        _guard = guard;
        _vantageDisabler = vantageDisabler;
        _legionSpaceDisabler = legionSpaceDisabler;
        _legionZoneDisabler = legionZoneDisabler;
        _isAfterburnerRunning = isAfterburnerRunning;
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
                ClearBecauseUnsupported();
        }
        catch
        {
            isSupported = false;
        }

        Log.Instance.Trace($"Supports GPU OC status: {isSupported}");

        return isSupported;
    }

    /// <summary>
    /// The firmware says this laptop can't overclock its GPU: the overclock is switched off and its values cleared.
    /// Nothing will be applied, so a pending "apply in progress" note would only cause a false warning at the next start.
    /// </summary>
    internal void ClearBecauseUnsupported()
    {
        Log.Instance.Trace($"Clearing settings...");

        _settings.Store.Enabled = false;
        _settings.Store.Info = GPUOverclockInfo.Zero;
        _settings.SynchronizeStore();

        _guard.Abandon();
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


    public async Task<GPUOverclockApplyResult> ApplyStateAsync(bool force = false)
    {
        if (_guard.RecoverFromInterruptedApply())
        {
            Changed?.Invoke(this, EventArgs.Empty);
            return GPUOverclockApplyResult.DisabledAfterInterruptedApply;
        }

        // MSI Afterburner manages the clocks while it runs: LLT sends nothing, not even the zero offsets of
        // "switch off", which would undo Afterburner's own settings. The saved values stay for later.
        if (_isAfterburnerRunning())
        {
            Log.Instance.Trace($"MSI Afterburner is running; not sending the GPU overclock. [force={force}]");

            Changed?.Invoke(this, EventArgs.Empty);
            return GPUOverclockApplyResult.SkippedForAfterburner;
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

            var result = SetOverclockInfo(gpu, info);

            Log.Instance.Trace($"Applied overclock: {info}, result: {result}, current: {GetOverclockInfo(gpu)}.");

            if (result is GPUOverclockApplyResult.Applied or GPUOverclockApplyResult.AppliedWithoutVoltageLock)
            {
                if (unproven)
                    _ = ConfirmStableLaterAsync(info);
            }
            else
            {
                // Part of the values may have taken effect, so the note stays until a clean exit: a freeze in this
                // run still switches the overclock off at the next start.
                Log.Instance.Trace($"The driver refused the overclock, or part of it: {info}.");
            }

            return result;
        }
        catch (NVIDIAApiException ex) when (ex.Status is Status.GpuNotPowered
            or Status.PortIdNotFound
            or Status.NvidiaDeviceNotFound
            or Status.HandleInvalidated)
        {
            // The dGPU is asleep or gone: nothing was sent.
            _guard.Abandon();
            Log.Instance.Trace($"dGPU is not available, overclock not applied, keeping settings: {info}.", ex);
            return GPUOverclockApplyResult.NotApplied;
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

        try
        {
            if (await IsSupportedAsync().ConfigureAwait(false))
                await ApplyStateAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Raised from the Windows message listener: an exception escaping here would end the app.
            Log.Instance.Trace($"Reapplying the GPU overclock after a display change failed. [message={e.Message}]", ex);
        }
    }

    // LLT's fixed limits. The driver's own range for this GPU narrows them further when it can be read.
    public static int GetMinCoreDeltaMhz() => -500;

    public static int GetMaxCoreDeltaMhz() => 500;

    public static int GetMinMemoryDeltaMhz() => -3000;

    public static int GetMaxMemoryDeltaMhz() => 3000;

    public static int GetMinVoltageLockMv() => 700;

    public static int GetMaxVoltageLockMv() => 1200;

    public static int GetMinVoltageCapMv() => 700;

    public static int GetMaxVoltageCapMv() => 1200;

    /// <summary>
    /// The core and memory offsets (MHz) and the voltage range (mV) the settings window offers: LLT's fixed limits,
    /// narrowed to what this GPU's driver reports. Reads the driver, so it is called off the UI thread.
    /// </summary>
    public ((int Min, int Max) Core, (int Min, int Max) Memory, (int Min, int Max) Voltage) GetRanges()
    {
        var (core, memory) = GetClockDeltaRangesMhz();

        return (core, memory, GetVoltageRangeMv());
    }

    /// <summary>Intersects a driver range with a fixed one; the fixed range when they don't overlap.</summary>
    internal static (int Min, int Max) Narrow((int Min, int Max) driver, int fixedMin, int fixedMax)
    {
        var min = Math.Max(driver.Min, fixedMin);
        var max = Math.Min(driver.Max, fixedMax);
        return min > max ? (fixedMin, fixedMax) : (min, max);
    }

    private static (int Min, int Max) GetVoltageRangeMv()
    {
        var fallback = (GetMinVoltageLockMv(), GetMaxVoltageLockMv());

        try
        {
            NVAPI.Initialize();

            var gpu = NVAPI.GetGPU();
            if (gpu is null)
                return fallback;

            var graphicsRange = GetCurveRanges(gpu).Graphics;
            if (graphicsRange is null)
                return fallback;

            var points = ReadCurvePoints(gpu).Points;

            var minMv = int.MaxValue;
            var maxMv = int.MinValue;
            for (var i = graphicsRange.Value.FirstPointIndex; i <= graphicsRange.Value.LastPointIndex && i < points.Length; i++)
            {
                if (points[i].VoltageInMicroV == 0)
                    continue;

                minMv = Math.Min(minMv, (int)points[i].VoltageInMilliV);
                maxMv = Math.Max(maxMv, (int)points[i].VoltageInMilliV);
            }

            return minMv > maxMv ? fallback : Narrow((minMv, maxMv), GetMinVoltageLockMv(), GetMaxVoltageLockMv());
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to read the V/F curve voltage range.", ex);
            return fallback;
        }
    }

    private static ((int Min, int Max) Core, (int Min, int Max) Memory) GetClockDeltaRangesMhz()
    {
        var coreFallback = (GetMinCoreDeltaMhz(), GetMaxCoreDeltaMhz());
        var memoryFallback = (GetMinMemoryDeltaMhz(), GetMaxMemoryDeltaMhz());

        try
        {
            NVAPI.Initialize();

            var gpu = NVAPI.GetGPU();
            if (gpu is null)
                return (coreFallback, memoryFallback);

            var states = GPUApi.GetPerformanceStates20(gpu.Handle);
            if (!states.Clocks.TryGetValue(PerformanceStateId.P0_3DPerformance, out var clocks))
                return (coreFallback, memoryFallback);

            return (GetClockDeltaRangeMhz(clocks, PublicClockDomain.Graphics, GetMinCoreDeltaMhz(), GetMaxCoreDeltaMhz()),
                GetClockDeltaRangeMhz(clocks, PublicClockDomain.Memory, GetMinMemoryDeltaMhz(), GetMaxMemoryDeltaMhz()));
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to read the clock offset ranges.", ex);
            return (coreFallback, memoryFallback);
        }
    }

    private static (int Min, int Max) GetClockDeltaRangeMhz(IPerformanceStates20ClockEntry[] clocks, PublicClockDomain domain, int fixedMinMhz, int fixedMaxMhz)
    {
        var clock = clocks.FirstOrDefault(c => c.DomainId == domain);
        if (clock is null)
            return (fixedMinMhz, fixedMaxMhz);

        var range = clock.FrequencyDeltaInkHz.DeltaRange;
        if (range.Minimum == 0 && range.Maximum == 0)
            return (fixedMinMhz, fixedMaxMhz);

        // Rounded inwards, so a value in this range is never outside the driver's.
        var minimumMhz = (int)Math.Ceiling(range.Minimum / 1000.0);
        var maximumMhz = (int)Math.Floor(range.Maximum / 1000.0);

        return minimumMhz > maximumMhz ? (fixedMinMhz, fixedMaxMhz) : Narrow((minimumMhz, maximumMhz), fixedMinMhz, fixedMaxMhz);
    }

    private static GPUOverclockApplyResult SetOverclockInfo(PhysicalGPU gpu, GPUOverclockInfo info)
    {
        var ((minCoreDeltaMhz, maxCoreDeltaMhz), (minMemoryDeltaMhz, maxMemoryDeltaMhz)) = GetClockDeltaRangesMhz();

        var coreDeltaMhz = Math.Clamp(info.CoreDeltaMhz, minCoreDeltaMhz, maxCoreDeltaMhz);
        var memoryDeltaMhz = Math.Clamp(info.MemoryDeltaMhz, minMemoryDeltaMhz, maxMemoryDeltaMhz);

        if (coreDeltaMhz != info.CoreDeltaMhz)
            Log.Instance.Trace($"Clamped core offset {info.CoreDeltaMhz} MHz to {coreDeltaMhz} MHz (allowed {minCoreDeltaMhz}..{maxCoreDeltaMhz} MHz).");
        if (memoryDeltaMhz != info.MemoryDeltaMhz)
            Log.Instance.Trace($"Clamped memory offset {info.MemoryDeltaMhz} MHz to {memoryDeltaMhz} MHz (allowed {minMemoryDeltaMhz}..{maxMemoryDeltaMhz} MHz).");

        // Voltages outside LLT's fixed limits are never sent; inside them, the curve decides which point is used.
        var clampedInfo = new GPUOverclockInfo(info.CoreDeltaMhz, info.MemoryDeltaMhz,
            info.VoltageLockMv > 0 ? Math.Clamp(info.VoltageLockMv, GetMinVoltageLockMv(), GetMaxVoltageLockMv()) : 0,
            info.VoltageCapMv > 0 ? Math.Clamp(info.VoltageCapMv, GetMinVoltageCapMv(), GetMaxVoltageCapMv()) : 0);

        var (graphicsRange, memoryRange) = GetCurveRanges(gpu);
        if (graphicsRange is null)
        {
            Log.Instance.Trace($"V/F curve is not available, applying offsets through performance states.");

            if (!ApplyPerformanceStates(gpu, coreDeltaMhz * 1000, memoryDeltaMhz * 1000))
                return GPUOverclockApplyResult.Failed;

            // Without the curve no voltage lock or cap can be placed safely, so none is sent.
            return clampedInfo.VoltageLockMv > 0 || clampedInfo.VoltageCapMv > 0
                ? GPUOverclockApplyResult.AppliedWithoutVoltageLock
                : GPUOverclockApplyResult.Applied;
        }

        return ApplyClockCurve(gpu, clampedInfo, graphicsRange.Value, memoryRange, coreDeltaMhz * 1000, memoryDeltaMhz * 1000, maxCoreDeltaMhz * 1000)
            ? GPUOverclockApplyResult.Applied
            : GPUOverclockApplyResult.Failed;
    }
    private static bool ApplyPerformanceStates(PhysicalGPU gpu, int coreDeltaKhz, int memoryDeltaKhz)
    {
        try
        {
            try
            {
                GPUApi.EnableOverclockedPStates(gpu.Handle);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to enable overclocked P-states.", ex);
            }

            var clockEntries = new[]
            {
                new PerformanceStates20ClockEntryV1(PublicClockDomain.Graphics, new PerformanceStates20ParameterDelta(coreDeltaKhz)),
                new PerformanceStates20ClockEntryV1(PublicClockDomain.Memory, new PerformanceStates20ParameterDelta(memoryDeltaKhz))
            };
            var voltageEntries = Array.Empty<PerformanceStates20BaseVoltageEntryV1>();
            var performanceStateInfo = new[] { new PerformanceStates20InfoV1.PerformanceState20(PerformanceStateId.P0_3DPerformance, clockEntries, voltageEntries) };
            var overclock = new PerformanceStates20InfoV1(performanceStateInfo, 2, 0);
            GPUApi.SetPerformanceStates20(gpu.Handle, overclock);

            return true;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to apply performance states.", ex);

            return false;
        }
    }

    private static bool ApplyClockCurve(PhysicalGPU gpu, GPUOverclockInfo info, PrivateClockBoostRangesV1.ClockBoostRange graphicsRange,
        PrivateClockBoostRangesV1.ClockBoostRange? memoryRange, int coreDeltaKhz, int memoryDeltaKhz, int maxCoreDeltaKhz)
    {
        var voltageMv = info.VoltageLockMv > 0 ? info.VoltageLockMv : info.VoltageCapMv;
        var offsetsRequested = coreDeltaKhz != 0 || memoryDeltaKhz != 0;

        try
        {
            var firstIndex = graphicsRange.FirstPointIndex;
            var lastIndex = graphicsRange.LastPointIndex;
            var minDeltaKhz = graphicsRange.MinimumInkHz;

            var (points, structVersion) = ReadCurvePoints(gpu);
            var currentDeltas = GPUApi.GetClockBoostTable(gpu.Handle, structVersion).GPUDeltas;

            var coreOffsetApplied = coreDeltaKhz == 0 || ReadSettableDeltaMhz(currentDeltas, graphicsRange) is not null;
            var memoryOffsetApplied = memoryDeltaKhz == 0 || (memoryRange is not null && ReadSettableDeltaMhz(currentDeltas, memoryRange.Value) is not null);
            var offsetsApplied = coreOffsetApplied && memoryOffsetApplied;

            if (!coreOffsetApplied)
                Log.Instance.Trace($"No V/F curve point accepts a core offset on this GPU, core offset not applied.");

            if (memoryDeltaKhz != 0 && memoryRange is null)
                Log.Instance.Trace($"The driver reported no memory V/F curve range, memory offset not applied.");
            else if (!memoryOffsetApplied)
                Log.Instance.Trace($"No V/F curve point accepts a memory offset on this GPU, memory offset not applied.");

            var available = Math.Min(points.Length, currentDeltas.Length);
            if (firstIndex >= available || lastIndex >= available)
            {
                Log.Instance.Trace($"Unexpected V/F curve size: {points.Length} points, {currentDeltas.Length} deltas, graphics range {firstIndex}..{lastIndex}.");
                return !offsetsRequested && voltageMv <= 0;
            }

            var clockMultiplier = structVersion == AdjustedClockCurveStructVersion || graphicsRange.MaximumInkHz != 2 * maxCoreDeltaKhz ? 1 : 2;
            var coreOffsetKhz = coreDeltaKhz * clockMultiplier;

            var anchorIndex = voltageMv > 0 ? FindCurvePoint(points, firstIndex, lastIndex, voltageMv) : -1;
            var anchorStockKhz = anchorIndex >= 0 ? GetStockFrequencyKhz(points, currentDeltas, anchorIndex) : 0;
            var anchorTargetKhz = anchorStockKhz + coreOffsetKhz;

            if (voltageMv > 0 && anchorIndex < 0)
                Log.Instance.Trace($"No V/F curve point at or above {voltageMv} mV in the {firstIndex}..{lastIndex} range, voltage cap/lock not applied.");

            var deltas = new PrivateClockBoostTableV1.GPUDelta[currentDeltas.Length];
            var flattenedCount = 0;
            var flattenDepthKhz = 0;

            for (var i = 0; i < deltas.Length; i++)
            {
                var deltaKhz = currentDeltas[i].FrequencyDeltaInkHz;

                if (memoryRange is not null && i >= memoryRange.Value.FirstPointIndex && i <= memoryRange.Value.LastPointIndex)
                {
                    deltaKhz = memoryDeltaKhz;
                }
                else if (i >= firstIndex && i <= lastIndex)
                {
                    deltaKhz = coreOffsetKhz;

                    if (anchorIndex >= 0)
                    {
                        var stockFrequencyKhz = GetStockFrequencyKhz(points, currentDeltas, i);
                        if (stockFrequencyKhz > anchorStockKhz)
                        {
                            deltaKhz = Math.Max(anchorTargetKhz - stockFrequencyKhz, minDeltaKhz);
                            flattenedCount++;
                            flattenDepthKhz = Math.Min(flattenDepthKhz, deltaKhz - coreOffsetKhz);
                        }
                    }
                }

                deltas[i] = new PrivateClockBoostTableV1.GPUDelta(deltaKhz);
            }

            GPUApi.SetClockBoostTable(gpu.Handle, new PrivateClockBoostTableV1(deltas), structVersion);

            if (anchorIndex >= 0)
                Log.Instance.Trace($"Flattened V/F curve at point #{anchorIndex} ({points[anchorIndex].VoltageInMilliV} mV, {anchorTargetKhz / (1000 * clockMultiplier)} MHz): {flattenedCount} points, depth {flattenDepthKhz / (1000 * clockMultiplier)} MHz.");

            var lockRequested = info.VoltageLockMv > 0 && anchorIndex >= 0;
            var lockApplied = SetVoltageLock(gpu.Handle, lockRequested ? points[anchorIndex].VoltageInMicroV : 0, lockRequested);

            if (voltageMv <= 0)
                return offsetsApplied;

            if (anchorIndex < 0)
                return false;

            return offsetsApplied && (!lockRequested || lockApplied);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to apply the voltage cap/lock.", ex);

            return !offsetsRequested && voltageMv <= 0;
        }
    }

    private const int DefaultCurveStructVersion = 1;
    private const int AdjustedClockCurveStructVersion = 2;

    private static (PrivateClockBoostRangesV1.ClockBoostRange? Graphics, PrivateClockBoostRangesV1.ClockBoostRange? Memory) GetCurveRanges(PhysicalGPU gpu)
    {
        var ranges = GPUApi.GetClockBoostRanges(gpu.Handle).ClockBoostRanges;
        var graphicsRange = ranges?.FirstOrDefault(r => r.ClockDomain == PublicClockDomain.Graphics);

        if (graphicsRange is null || graphicsRange.Value.LastPointIndex == 0)
            return (null, null);

        return (graphicsRange, ranges?.FirstOrDefault(r => r.ClockDomain == PublicClockDomain.Memory));
    }

    private static (PrivateClientClkVFPointsStatusV1.VFPoint[] Points, int StructVersion) ReadCurvePoints(PhysicalGPU gpu)
    {
        try
        {
            return (GPUApi.GetClientClkVFPointsStatus(gpu.Handle, AdjustedClockCurveStructVersion).Points, AdjustedClockCurveStructVersion);
        }
        catch
        {
            return (GPUApi.GetClientClkVFPointsStatus(gpu.Handle, DefaultCurveStructVersion).Points, DefaultCurveStructVersion);
        }
    }

    private static int FindCurvePoint(PrivateClientClkVFPointsStatusV1.VFPoint[] points, int firstIndex, int lastIndex, int voltageMv)
    {
        var last = Math.Min(lastIndex, points.Length - 1);

        for (var i = firstIndex; i <= last; i++)
        {
            if (points[i].VoltageInMicroV > 0 && points[i].VoltageInMilliV >= voltageMv)
                return i;
        }

        return -1;
    }

    private static int GetStockFrequencyKhz(PrivateClientClkVFPointsStatusV1.VFPoint[] points, PrivateClockBoostTableV1.GPUDelta[] deltas, int index)
        => (int)points[index].FrequencyInkHz - deltas[index].FrequencyDeltaInkHz;

    private static int? ReadSettableDeltaMhz(PrivateClockBoostTableV1.GPUDelta[] deltas, PrivateClockBoostRangesV1.ClockBoostRange range)
    {
        var lastIndex = Math.Min(range.LastPointIndex, deltas.Length - 1);

        for (var i = range.FirstPointIndex; i <= lastIndex; i++)
        {
            if (deltas[i].PointMode == 0)
                return deltas[i].FrequencyDeltaInkHz / 1000;
        }

        return null;
    }

    private static bool SetVoltageLock(PhysicalGPUHandle gpuHandle, uint voltageInMicroV, bool lockVoltage)
    {
        try
        {
            var entry = lockVoltage
                ? PrivateClockBoostLockV2.ClockBoostLock.CreateVoltageLock(voltageInMicroV)
                : PrivateClockBoostLockV2.ClockBoostLock.CreateVoltageReset();

            GPUApi.SetClockBoostLock(gpuHandle, new PrivateClockBoostLockV2(new[] { entry }));

            return true;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to set the voltage lock.", ex);

            return false;
        }
    }

    private static GPUOverclockInfo GetOverclockInfo(PhysicalGPU gpu)
    {
        var core = 0;
        var memory = 0;

        try
        {
            var (graphicsRange, memoryRange) = GetCurveRanges(gpu);
            if (graphicsRange is not null)
            {
                var (_, structVersion) = ReadCurvePoints(gpu);
                var deltas = GPUApi.GetClockBoostTable(gpu.Handle, structVersion).GPUDeltas;

                core = ReadSettableDeltaMhz(deltas, graphicsRange.Value) ?? 0;

                if (memoryRange is not null)
                    memory = ReadSettableDeltaMhz(deltas, memoryRange.Value) ?? 0;
            }
            else
            {
                var states = GPUApi.GetPerformanceStates20(gpu.Handle);
                var p0Clocks = states.Clocks.TryGetValue(PerformanceStateId.P0_3DPerformance, out var clocks)
                    ? clocks
                    : Array.Empty<IPerformanceStates20ClockEntry>();

                memory = p0Clocks.FirstOrDefault(c => c.DomainId == PublicClockDomain.Memory)?.FrequencyDeltaInkHz.DeltaValue / 1000 ?? 0;
                core = p0Clocks.FirstOrDefault(c => c.DomainId == PublicClockDomain.Graphics)?.FrequencyDeltaInkHz.DeltaValue / 1000 ?? 0;
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to read the active offsets.", ex);
        }

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
            var (graphicsRange, _) = GetCurveRanges(gpu);
            var (points, structVersion) = ReadCurvePoints(gpu);
            var deltas = GPUApi.GetClockBoostTable(gpu.Handle, structVersion).GPUDeltas;

            if (graphicsRange is not null && deltas is not null)
            {
                var lastIndex = Math.Min(graphicsRange.Value.LastPointIndex, Math.Min(points.Length, deltas.Length) - 1);

                for (var i = graphicsRange.Value.FirstPointIndex + 1; i <= lastIndex; i++)
                {
                    if (deltas[i].FrequencyDeltaInkHz < deltas[i - 1].FrequencyDeltaInkHz && points[i - 1].VoltageInMicroV > 0)
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
