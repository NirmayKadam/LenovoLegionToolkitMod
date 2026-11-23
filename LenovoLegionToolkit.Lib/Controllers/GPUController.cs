using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Features.Hybrid;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;
using NeoSmart.AsyncLock;
using NvAPIWrapper.GPU;
using NvAPIWrapper.Native.Exceptions;
using NvAPIWrapper.Native.General;
using NvAPIWrapper.Native.GPU;
using NvAPIWrapper.Native.GPU.Structures;
using Resource = LenovoLegionToolkit.Lib.Resources.Resource;

namespace LenovoLegionToolkit.Lib.Controllers;

public class GPUController
{
    private readonly AsyncLock _stateLock = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly Func<Task> _initialize;
    private readonly Func<CancellationToken, Task> _refresh;
    private readonly Action<string, Exception> _reportError;
    private readonly Func<int> _interval;
    private readonly Func<int> _startupDelay;
    private readonly TimeSpan? _readerHold;
    private readonly Func<bool> _isAfterburnerRunning;

    public GPUController()
    {
        _initialize = InitializeAsync;
        _refresh = DoRefreshAsync;
        _reportError = Log.Instance.ErrorReport;
        _interval = () => IoCContainer.Resolve<ApplicationSettings>().Store.GPUMonitoringInterval;
        _startupDelay = () => IoCContainer.Resolve<ApplicationSettings>().Store.GPUMonitoringStartupDelay;
        _isAfterburnerRunning = AfterburnerDetector.IsRunning;
    }

    /// <summary>
    /// For tests: the start-up step, the per-round refresh and the error report are supplied by the caller, since
    /// NVIDIA's API can't be driven in a test; so are the timings, so a test runs in milliseconds, and whether MSI
    /// Afterburner runs (not running when not given).
    /// </summary>
    internal GPUController(Func<Task> initialize, Func<CancellationToken, Task> refresh, Action<string, Exception> reportError, Func<int> interval, int startupDelayMs, TimeSpan readerHold, Func<bool>? isAfterburnerRunning = null)
    {
        _initialize = initialize;
        _refresh = refresh;
        _reportError = reportError;
        _interval = interval;
        _startupDelay = () => startupDelayMs;
        _readerHold = readerHold;
        _isAfterburnerRunning = isAfterburnerRunning ?? (() => false);
    }

    /// <summary>Time between rounds: the "GPU monitoring interval" setting, read every round.</summary>
    private int Interval => _interval();

    /// <summary>How long one read keeps the check running: three rounds, and at least 15 s.</summary>
    private TimeSpan ReaderHold => _readerHold ?? TimeSpan.FromMilliseconds(Math.Max(3L * Interval, 15_000));

    // ---- Who needs the check ----
    // The check runs while at least one holder holds it, or a reader has read within ReaderHold. This lock guards
    // the holders, the readers and starting/stopping the loop; it is never held across an NVIDIA call or an await.

    private readonly object _holdersLock = new();
    private readonly HashSet<object> _holders = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, long> _readerLastRead = new(ReferenceEqualityComparer.Instance);
    private bool _startedBefore;

    private CancellationTokenSource? _refreshCancellationTokenSource;

    /// <summary>
    /// Keeps the check running until <see cref="Release"/> with the same holder (the dGPU card while visible, the
    /// dGPU window while open, game detection while it runs). Holding twice is the same as once.
    /// </summary>
    public void Hold(object holder)
    {
        lock (_holdersLock)
        {
            _holders.Add(holder);
            // The start-up delay only applies to the first start after LLT launches.
            StartLoopIfStopped(_startedBefore ? 0 : _startupDelay());
        }
    }

    /// <summary>Lets go of a hold; the check stops at once if nothing else needs it.</summary>
    public void Release(object holder)
    {
        CancellationTokenSource? stop;
        lock (_holdersLock)
        {
            _holders.Remove(holder);
            stop = TakeLoopIfUnneeded();
        }

        if (stop is null)
            return;

        Log.Instance.Trace($"GPU check stopped: nothing needs it.");
        stop.Cancel();
    }

    /// <summary>
    /// The GPU's current state, for code that reads it before touching the GPU (the sensors). Each read keeps the
    /// check running for <see cref="ReaderHold"/>. If the check wasn't running, the state is refreshed first, so a
    /// reader never acts on a state from before. Never throws: on failure the state is <see cref="GPUState.Unknown"/>.
    /// </summary>
    public async Task<GPUState> GetFreshStateAsync(object reader)
    {
        try
        {
            bool running;
            lock (_holdersLock)
            {
                _readerLastRead[reader] = Environment.TickCount64;
                running = _refreshCancellationTokenSource is not null;
            }

            if (!running)
            {
                try
                {
                    // The loop's own start-up step first (nothing to do when NVIDIA's API is already set up).
                    await _initialize().ConfigureAwait(false);
                    await RefreshNowAsync().ConfigureAwait(false);
                }
                finally
                {
                    // Started even if the refresh failed; the loop reports its own failures. Its first round is
                    // one interval away, since this read just refreshed.
                    lock (_holdersLock)
                    {
                        _startedBefore = true;
                        StartLoopIfStopped(Interval);
                    }
                }
            }

            using (await _stateLock.LockAsync().ConfigureAwait(false))
            {
                return _state;
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't read the GPU state.", ex);
            return GPUState.Unknown;
        }
    }

    /// <summary>Starts the loop unless it runs. Caller holds <see cref="_holdersLock"/>.</summary>
    private void StartLoopIfStopped(int delayMs)
    {
        if (_refreshCancellationTokenSource is not null)
            return;

        var cancellationTokenSource = new CancellationTokenSource();
        _refreshCancellationTokenSource = cancellationTokenSource;
        _startedBefore = true;

        // On a pool thread, so the start-up step's NVIDIA calls never run under the lock or on the caller's thread.
        _ = Task.Run(() => RefreshLoopAsync(delayMs, cancellationTokenSource));
    }

    /// <summary>
    /// If nothing needs the check any more, marks the loop as stopped and returns its token source for the caller
    /// to cancel outside the lock; otherwise null. Caller holds <see cref="_holdersLock"/>.
    /// </summary>
    private CancellationTokenSource? TakeLoopIfUnneeded()
    {
        var now = Environment.TickCount64;
        var holdMs = (long)ReaderHold.TotalMilliseconds;
        foreach (var expired in _readerLastRead.Where(r => now - r.Value > holdMs).Select(r => r.Key).ToList())
            _readerLastRead.Remove(expired);

        if (_refreshCancellationTokenSource is null || _holders.Count > 0 || _readerLastRead.Count > 0)
            return null;

        var cancellationTokenSource = _refreshCancellationTokenSource;
        _refreshCancellationTokenSource = null;
        return cancellationTokenSource;
    }

    private GPUState _state = GPUState.Unknown;
    private List<GpuProcess> _processes = [];
    private List<GpuProcess> _allProcesses = [];
    private string? _gpuInstanceId;
    private string? _performanceState;
    private string? _sleepBlockerReason;

    public PerformanceStateId? CurrentPerformanceState { get; private set; }

    public event EventHandler<GPUStatus>? Refreshed;
    public bool IsStarted => _refreshCancellationTokenSource is not null;

    public bool IsSupported()
    {
        try
        {
            NVAPI.Initialize();
            PhysicalGPU? gpu = NVAPI.GetGPU();
            return gpu is not null;
        }
        catch
        {
            return false;
        }
    }

    public async Task<GPUStatus> RefreshNowAsync()
    {
        await _refreshGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _refresh(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _refreshGate.Release();
        }

        using (await _stateLock.LockAsync().ConfigureAwait(false))
        {
            return new GPUStatus(_state, _performanceState, _processes, _sleepBlockerReason);
        }
    }

    public async Task RestartGPUAsync()
    {
        string? gpuInstanceId;

        using (await _stateLock.LockAsync().ConfigureAwait(false))
        {
            if (_state is not GPUState.Active and not GPUState.Inactive)
            {
                return;
            }

            gpuInstanceId = _gpuInstanceId;
        }

        if (string.IsNullOrEmpty(gpuInstanceId))
        {
            return;
        }

        Log.Instance.Trace($"Restarting GPU... [gpuInstanceId={gpuInstanceId}]");

        await CMD.RunAsync("pnputil", $"/restart-device \"{gpuInstanceId}\"").ConfigureAwait(false);
    }

    public async Task KillGPUProcessesAsync()
    {
        List<GpuProcess> processes;
        string? gpuInstanceId;

        using (await _stateLock.LockAsync().ConfigureAwait(false))
        {
            if (_state is not GPUState.Active)
            {
                return;
            }

            processes = [.. _processes];
            gpuInstanceId = _gpuInstanceId;
        }

        if (string.IsNullOrEmpty(gpuInstanceId) || processes.Count == 0)
        {
            return;
        }

        Log.Instance.Trace($"Killing GPU processes... [gpuInstanceId={gpuInstanceId}]");

        foreach (var gpuProcess in processes)
        {
            try
            {
                // Opened only now, because the user asked to end it; and only if the id is still that program's.
                using var process = Process.GetProcessById(gpuProcess.Id);
                if (!string.Equals(process.ProcessName, gpuProcess.Name, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Instance.Trace($"Not killing: the id now belongs to another program. [pid={gpuProcess.Id}, was={gpuProcess.Name}, now={process.ProcessName}]");
                    continue;
                }

                process.Kill(true);
                // A process stuck in the graphics driver may never exit; don't hang the action on it.
                using var exitWait = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await process.WaitForExitAsync(exitWait.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Couldn't kill process. [pid={gpuProcess.Id}, name={gpuProcess.Name}]", ex);
            }
        }
    }

    private async Task InitializeAsync()
    {
        Log.Instance.Trace($"Initializing NVAPI...");

        NVAPI.Initialize();

        Log.Instance.Trace($"Initialized NVAPI");

        var settings = IoCContainer.Resolve<ApplicationSettings>();
        if (settings.Store.LockedPStateId >= 0)
        {
            await ApplyPStateAsync(settings.Store.LockedPStateId).ConfigureAwait(false);
        }
    }

    /// <summary>How long to wait after a failed round when no interval is set, so failures can't spin.</summary>
    private const int RetryAfterFailureMs = 5_000;

    /// <summary>
    /// Refreshes the GPU state every <see cref="Interval"/> while something needs it (see <see cref="Hold"/> and
    /// <see cref="GetFreshStateAsync"/>), then stops by itself. A failed round is logged (and reported once per run)
    /// and the loop carries on. If the start-up step fails, the loop ends and marks itself as not started, so a
    /// later hold or read can try again.
    /// </summary>
    private async Task RefreshLoopAsync(int delay, CancellationTokenSource cancellationTokenSource)
    {
        var token = cancellationTokenSource.Token;

        try
        {
            await _initialize().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"GPU monitoring couldn't start.", ex);
            lock (_holdersLock)
            {
                if (ReferenceEquals(_refreshCancellationTokenSource, cancellationTokenSource))
                    _refreshCancellationTokenSource = null;
            }
            return;
        }

        try
        {
            await Task.Delay(delay, token).ConfigureAwait(false);

            var reported = false;

            while (true)
            {
                token.ThrowIfCancellationRequested();

                var failed = false;

                await _refreshGate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    await _refresh(token).ConfigureAwait(false);

                    using (await _stateLock.LockAsync(token).ConfigureAwait(false))
                    {
                        Refreshed?.Invoke(this, new GPUStatus(_state, _performanceState, _processes, _sleepBlockerReason));
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !token.IsCancellationRequested)
                {
                    failed = true;
                    Log.Instance.Trace($"GPU refresh failed; trying again next round.", ex);

                    if (!reported)
                    {
                        reported = true;
                        _reportError("GPU refresh failed; LLT keeps trying.", ex);
                    }
                }
                finally
                {
                    _refreshGate.Release();
                }

                // Stop once nothing needs the check (the last reader's hold ran out), or if it was stopped already.
                CancellationTokenSource? stop;
                lock (_holdersLock)
                {
                    if (!ReferenceEquals(_refreshCancellationTokenSource, cancellationTokenSource))
                        return;
                    stop = TakeLoopIfUnneeded();
                }

                if (stop is not null)
                {
                    Log.Instance.Trace($"GPU check stopped: nothing needs it.");
                    return;
                }

                var interval = Interval;
                var wait = failed && interval <= 0 ? RetryAfterFailureMs : interval;
                if (wait > 0)
                {
                    await Task.Delay(wait, token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task DoRefreshAsync(CancellationToken token)
    {
        var hybridModeFeature = IoCContainer.Resolve<HybridModeFeature>();
        if (hybridModeFeature.ShouldKeepDGPUAsleep())
        {
            using (await _stateLock.LockAsync(token).ConfigureAwait(false))
            {
                SetPoweredOffState();
            }
            Log.Instance.Trace($"dGPU eject is being ensured — skipping NVAPI refresh.");

            return;
        }

        string? cachedGpuInstanceId;
        GPUState oldState;
        using (await _stateLock.LockAsync(token).ConfigureAwait(false))
        {
            oldState = _state;
            cachedGpuInstanceId = _gpuInstanceId;
        }

        var gpu = NVAPI.GetGPU();
        if (gpu is null)
        {
            using (await _stateLock.LockAsync(token).ConfigureAwait(false))
            {
                _state = GPUState.NvidiaGpuNotFound;
                _performanceState = null;
                _processes = [];
                _allProcesses = [];
                _gpuInstanceId = null;
                _sleepBlockerReason = null;
            }

            return;
        }

        var coprocInfo = NVAPI.GetCoprocInfo(gpu);
        if (coprocInfo != null && coprocInfo.PowerState is CoprocPowerState.Gc6 or CoprocPowerState.GcOff)
        {
            CurrentPerformanceState = null;
            using (await _stateLock.LockAsync(token).ConfigureAwait(false))
            {
                SetPoweredOffState();
            }

            return;
        }

        GPUState newState;
        List<GpuProcess> newProcesses;
        List<GpuProcess> newAllProcesses;
        string? newGpuInstanceId;
        string? newPerformanceState;

        try
        {
            CurrentPerformanceState = NVAPI.GetCurrentPerformanceState(gpu);
            var pState = CurrentPerformanceState ?? gpu.PerformanceStatesInfo?.CurrentPerformanceState?.StateId;
            var stateId = pState != null ? $"P{(uint)pState}" : null;
            newPerformanceState = Resource.GPUController_PoweredOn;
            if (!string.IsNullOrWhiteSpace(stateId))
            {
                newPerformanceState += $", {stateId}";
            }
        }
        catch (NVIDIAApiException ex) when (ex.Status == Status.PortIdNotFound || ex.Status == Status.GpuNotPowered)
        {
            CurrentPerformanceState = null;
            using (await _stateLock.LockAsync(token).ConfigureAwait(false))
            {
                SetPoweredOffState();
            }

            return;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"GPU status exception.", ex);
            newPerformanceState = null;
        }

        newGpuInstanceId = cachedGpuInstanceId;
        if (string.IsNullOrEmpty(newGpuInstanceId))
        {
            var pnpDeviceIdPart = NVAPI.GetGPUId(gpu);
            if (!string.IsNullOrEmpty(pnpDeviceIdPart))
            {
                newGpuInstanceId = await WMI.Win32.PnpEntity.GetDeviceIDAsync(pnpDeviceIdPart).ConfigureAwait(false);
            }

        }

        List<GpuProcess> processNames;
        try
        {
            var (allProcessNames, filteredProcessNames) = NVAPIExtensions.GetActiveProcesses(gpu);
            newAllProcesses = allProcessNames;
            processNames = filteredProcessNames;
        }
        catch (NVIDIAApiException ex)
        {
            Log.Instance.Trace($"NVAPI exception in GetActiveProcesses.", ex);
            NVAPI.IsInitialized = false;
            using (await _stateLock.LockAsync(token).ConfigureAwait(false))
            {
                _state = GPUState.Unknown;
                _performanceState = null;
                _processes = [];
                _allProcesses = [];
                _gpuInstanceId = null;
            }

            return;
        }

        var feature = IoCContainer.Resolve<HybridModeFeature>();

        if (await feature.GetStateAsync().ConfigureAwait(false) == HybridModeState.Off)
        {
            newProcesses = processNames;
            newState = GPUState.MonitorConnected;
        }
        else if (processNames.Count != 0)
        {
            newProcesses = processNames;
            newState = GPUState.Active;
        }
        else if (NVAPI.IsDisplayConnected(gpu))
        {
            newProcesses = processNames;
            newState = GPUState.MonitorConnected;
        }
        else
        {
            newProcesses = [];
            newState = GPUState.Inactive;
        }

        string? newSleepBlockerReason = null;
        if (newState == GPUState.Inactive)
        {
            var gc6Debug = NVAPI.GetGC6DebugInfo(gpu);
            if (gc6Debug.HasValue)
            {
                var reason = FormatBlockerReason(gc6Debug.Value);
                if (!string.IsNullOrWhiteSpace(reason))
                {
                    newSleepBlockerReason = reason;
                }
            }
        }

        using (await _stateLock.LockAsync(token).ConfigureAwait(false))
        {
            _state = newState;
            _performanceState = newPerformanceState;
            _processes = newProcesses;
            _allProcesses = newAllProcesses;
            _gpuInstanceId = newGpuInstanceId;
            _sleepBlockerReason = newSleepBlockerReason;
        }

        if ((oldState is GPUState.PoweredOff or GPUState.Unknown or GPUState.NvidiaGpuNotFound) &&
            (newState is GPUState.Active or GPUState.Inactive or GPUState.MonitorConnected))
        {
            var lockedPState = IoCContainer.Resolve<ApplicationSettings>().Store.LockedPStateId;
            if (lockedPState >= 0)
            {
                _ = ApplyPStateAsync(lockedPState);
            }
        }
    }

    public IReadOnlyList<GpuProcess> ActiveProcesses => _processes;
    public IReadOnlyList<GpuProcess> AllActiveProcesses => _allProcesses;

    public GpuPreference GetGpuPreference(string exePath)
    {
        var prefString = Registry.GetValue("HKEY_CURRENT_USER", @"SOFTWARE\Microsoft\DirectX\UserGpuPreferences", exePath, string.Empty);

        var isOurApp = string.Equals(exePath, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase);
        var aumid = isOurApp ? GetAppUserModelId() : null;

        if (string.IsNullOrEmpty(prefString) && !string.IsNullOrEmpty(aumid))
            prefString = Registry.GetValue("HKEY_CURRENT_USER", @"SOFTWARE\Microsoft\DirectX\UserGpuPreferences", aumid, string.Empty);

        if (prefString.Contains("GpuPreference=1")) return GpuPreference.Integrated;
        if (prefString.Contains("GpuPreference=2")) return GpuPreference.Discrete;

        return GpuPreference.Default;
    }

    public void SetGpuPreference(string exePath, GpuPreference preference)
    {
        try
        {
            var isOurApp = string.Equals(exePath, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase);
            var aumid = isOurApp ? GetAppUserModelId() : null;

            if (preference == GpuPreference.Default)
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\DirectX\UserGpuPreferences", true);
                key?.DeleteValue(exePath, false);

                if (!string.IsNullOrEmpty(aumid))
                {
                    key?.DeleteValue(aumid, false);
                }
            }
            else
            {
                var value = preference == GpuPreference.Integrated ? "GpuPreference=1;" : "GpuPreference=2;";
                Registry.SetValue("HKEY_CURRENT_USER", @"SOFTWARE\Microsoft\DirectX\UserGpuPreferences", exePath, value, false, Microsoft.Win32.RegistryValueKind.String);

                if (!string.IsNullOrEmpty(aumid))
                {
                    Registry.SetValue("HKEY_CURRENT_USER", @"SOFTWARE\Microsoft\DirectX\UserGpuPreferences", aumid, value, false, Microsoft.Win32.RegistryValueKind.String);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to set GPU preference for {exePath}.", ex);
        }
    }

    private List<PerformanceStateId>? _cachedSupportedPerformanceStates;

    public List<PerformanceStateId> GetSupportedPerformanceStates()
    {
        if (_cachedSupportedPerformanceStates is { Count: > 0 } cached)
            return cached;

        try
        {
            var gpu = NVAPI.GetGPU();
            if (gpu is null)
                return [];

            var states = NVAPI.GetSupportedPerformanceStates(gpu);
            if (states.Count > 0)
            {
                _cachedSupportedPerformanceStates = states;
            }

            return states;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to get supported performance states.", ex);
            return [];
        }
    }

    public async Task<bool> SetPStateAsync(int pStateId)
    {
        var settings = IoCContainer.Resolve<ApplicationSettings>();
        settings.Store.LockedPStateId = pStateId;
        settings.SynchronizeStore();

        return await ApplyPStateAsync(pStateId).ConfigureAwait(false);
    }

    /// <summary>
    /// Locks the GPU to a P-state (or back to dynamic for a negative id). Not while MSI Afterburner runs: it manages
    /// the clocks then, and the saved choice is applied at the next start or dGPU wake when it doesn't run.
    /// </summary>
    public Task<bool> ApplyPStateAsync(int pStateId)
    {
        if (_isAfterburnerRunning())
        {
            Log.Instance.Trace($"MSI Afterburner is running; not changing the GPU P-state. [pState={pStateId}]");
            return Task.FromResult(false);
        }

        try
        {
            var gpu = NVAPI.GetGPU();
            if (gpu is null)
                return Task.FromResult(false);

            if (pStateId < 0)
            {
                Log.Instance.Trace($"Resetting GPU to dynamic P-State mode.");
                return Task.FromResult(NVAPI.ResetDynamicPerformanceStates(gpu));
            }

            var targetState = (PerformanceStateId)(uint)pStateId;
            Log.Instance.Trace($"Setting GPU P-State lock to: {targetState}");
            return Task.FromResult(NVAPI.SetPerformanceState(gpu, targetState));
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to apply P-State {pStateId}.", ex);
            return Task.FromResult(false);
        }
    }

    private void SetPoweredOffState()
    {
        _state = GPUState.PoweredOff;
        _performanceState = Resource.GPUController_PoweredOff;
        _processes = [];
        _allProcesses = [];
        _gpuInstanceId = null;
        _sleepBlockerReason = null;
    }

    private static string FormatBlockerReason(GC6DebugInfoV2 debugInfo)
    {
        if (!debugInfo.HasVbiosSupport)
        {
            return Resource.GPUController_GC6_NoVbiosSupport;
        }

        if (!debugInfo.HasSbiosSupport)
        {
            return Resource.GPUController_GC6_NoSbiosSupport;
        }

        var flags = debugInfo.BlockerReasons;
        if (flags == GC6BlockerReason.None)
        {
            return string.Empty;
        }

        var reasons = new List<string>();
        if (flags.HasFlag(GC6BlockerReason.AudioMonitor))
            reasons.Add(Resource.GPUController_GC6_Blocker_Audio);
        if (flags.HasFlag(GC6BlockerReason.RefMsHybrid))
            reasons.Add(Resource.GPUController_GC6_Blocker_HybridGraphics);
        if (flags.HasFlag(GC6BlockerReason.CpuVisibleSurface))
            reasons.Add(Resource.GPUController_GC6_Blocker_CpuSharedSurface);
        if (flags.HasFlag(GC6BlockerReason.OutstandingLock))
            reasons.Add(Resource.GPUController_GC6_Blocker_ResourceLock);
        if (flags.HasFlag(GC6BlockerReason.DdiAcquire) || flags.HasFlag(GC6BlockerReason.SrlessMonitor))
            reasons.Add(Resource.GPUController_GC6_Blocker_Display);
        if (flags.HasFlag(GC6BlockerReason.SoftwareDisabled))
            reasons.Add(Resource.GPUController_GC6_Blocker_SoftwareDisabled);
        if (flags.HasFlag(GC6BlockerReason.BusScan))
            reasons.Add(Resource.GPUController_GC6_Blocker_BusScan);
        if (flags.HasFlag(GC6BlockerReason.PowerEvent))
            reasons.Add(Resource.GPUController_GC6_Blocker_PowerEvent);
        if (flags.HasFlag(GC6BlockerReason.Acpi))
            reasons.Add(Resource.GPUController_GC6_Blocker_Acpi);
        if (flags.HasFlag(GC6BlockerReason.Device) || flags.HasFlag(GC6BlockerReason.RefResourceManager) || flags.HasFlag(GC6BlockerReason.Misc))
            reasons.Add(Resource.GPUController_GC6_Blocker_DriverHold);

        var reasonText = reasons.Count > 0
            ? string.Join(", ", reasons.Distinct())
            : $"0x{debugInfo.BlockerFlags:X}";

        return string.Format(Resource.GPUController_GC6_SleepBlocked, reasonText);
    }

    private static string? GetAppUserModelId()
    {
        try
        {
            return Windows.ApplicationModel.Package.Current.Id.FamilyName + "!App";
        }
        catch
        {
            return null;
        }
    }
}
