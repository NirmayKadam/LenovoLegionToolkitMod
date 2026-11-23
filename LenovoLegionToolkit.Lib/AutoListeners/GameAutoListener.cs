using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.GameDetection;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.AutoListeners;

/// <summary>
/// Whether a game is running, for AI mode and automations. Games are found from process-start events, the process
/// list, the known-games list, the processes on the discrete GPU and Windows Game Mode; they end when Windows
/// reports the process stopped or the process list no longer shows them.
/// <para>
/// No game's process is ever opened (not even to wait for it to end or to read its path): kernel anti-cheats watch
/// for other programs holding handles to the game. Paths come from <see cref="ProcessImagePath"/>.
/// </para>
/// </summary>
public class GameAutoListener : AbstractAutoListener<GameAutoListener.ChangedEventArgs>
{
    public class ChangedEventArgs(bool running) : EventArgs
    {
        public bool Running { get; } = running;
    }

    /// <summary>How often the process list is checked while a game counts as running, in case its end wasn't reported.</summary>
    private static readonly TimeSpan ReconcileInterval = TimeSpan.FromSeconds(10);

    private static readonly Lock Lock = new();

    private readonly InstanceStartedEventAutoAutoListener _instanceStartedEventAutoAutoListener;
    private readonly InstanceStoppedEventAutoAutoListener _instanceStoppedEventAutoAutoListener;

    private readonly GameConfigStoreDetector _gameConfigStoreDetector;
    private readonly EffectiveGameModeDetector _effectiveGameModeDetector;
    private readonly GPUController _gpuController;
    private readonly ApplicationSettings _settings;

    private readonly HashSet<ProcessInfo> _detectedGamePathsCache = [];
    private readonly GameTracker _games = new();

    private Timer? _reconcileTimer;
    private bool _lastState;
    private bool _preserveStateOnNextStart;

    public GameAutoListener(InstanceStartedEventAutoAutoListener instanceStartedEventAutoAutoListener,
        InstanceStoppedEventAutoAutoListener instanceStoppedEventAutoAutoListener,
        GPUController gpuController, ApplicationSettings settings)
    {
        _instanceStartedEventAutoAutoListener = instanceStartedEventAutoAutoListener;
        _instanceStoppedEventAutoAutoListener = instanceStoppedEventAutoAutoListener;
        _gpuController = gpuController;
        _settings = settings;

        _gameConfigStoreDetector = new GameConfigStoreDetector();
        _gameConfigStoreDetector.GamesDetected += GameConfigStoreDetectorGamesConfigStoreDetected;

        _effectiveGameModeDetector = new EffectiveGameModeDetector();
        _effectiveGameModeDetector.Changed += EffectiveGameModeDetectorChanged;
    }

    protected override async Task StartAsync()
    {
        var running = RunningProcesses();

        lock (Lock)
        {
            var checkIncluded = _settings.Store.IncludedProcesses.Count > 0;
            var checkGameConfigStore = _settings.Store.GameDetection.UseGameConfigStore;

            if (checkGameConfigStore)
            {
                foreach (var gamePath in GameConfigStoreDetector.GetDetectedGamePaths())
                    _detectedGamePathsCache.Add(gamePath);
            }

            if (_preserveStateOnNextStart)
            {
                _preserveStateOnNextStart = false;
                Log.Instance.Trace($"Validating preserved games against current rules ({_games.Count} game(s))...");

                var ended = _games.Reconcile(running);
                var blacklisted = _games.Games.Where(g => IsBlacklisted(g.Name)).ToList();
                foreach (var game in blacklisted)
                {
                    Log.Instance.Trace($"Preserved game is now blacklisted: {game.Name} [pid={game.Id}].");
                    _games.Remove(game.Id);
                }

                if (ended.Count + blacklisted.Count > 0)
                    Log.Instance.Trace($"Dropped {ended.Count + blacklisted.Count} game(s) during re-validation. Remaining: {_games.Count}.");

                _lastState = !_games.IsEmpty;
                if (_games.IsEmpty)
                {
                    Log.Instance.Trace($"All active games disqualified during restart.");
                    RaiseChanged(new ChangedEventArgs(false));
                }
                else
                {
                    StartReconcilingIfNeeded();
                }
            }

            if (checkIncluded || checkGameConfigStore)
            {
                var included = checkIncluded
                    ? _settings.Store.IncludedProcesses.ToHashSet(StringComparer.OrdinalIgnoreCase)
                    : null;

                foreach (var (id, name) in running)
                {
                    try
                    {
                        if (id <= 4 || _games.Contains(id))
                            continue;

                        var isIncludedGame = included?.Contains(name) ?? false;

                        string? processPath = null;
                        var isConfigStoreGame = false;
                        if (!isIncludedGame && checkGameConfigStore && !IsBlacklisted(name))
                        {
                            processPath = ProcessImagePath.Get(id);
                            if (!string.IsNullOrEmpty(processPath))
                                isConfigStoreGame = _detectedGamePathsCache.Contains(ProcessInfo.FromPath(processPath));
                        }

                        if (!isIncludedGame && !isConfigStoreGame)
                            continue;

                        processPath ??= ProcessImagePath.Get(id);
                        Track(id, name, isIncludedGame ? "Included" : "Known Game List", processPath, "Found already running game");
                    }
                    catch (Exception ex)
                    {
                        Log.Instance.Trace($"Failed to check process {id}.", ex);
                    }
                }
            }
        }

        if (_settings.Store.GameDetection.UseDiscreteGPU)
        {
            // The GPU check runs while game detection uses it; released in StopAsync.
            _gpuController.Hold(this);
            _gpuController.Refreshed += GpuController_Refreshed;

            try
            {
                var status = await _gpuController.RefreshNowAsync().ConfigureAwait(false);
                lock (Lock)
                    TrackGpuProcesses(status.Processes, "Found already running game");
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to check for already running GPU processes.", ex);
            }
        }

        if (_settings.Store.GameDetection.UseGameConfigStore)
            await _gameConfigStoreDetector.StartAsync(_detectedGamePathsCache).ConfigureAwait(false);

        if (_settings.Store.GameDetection.UseEffectiveGameMode)
        {
            await _effectiveGameModeDetector.StartAsync().ConfigureAwait(false);

            lock (Lock)
            {
                if (_effectiveGameModeDetector.IsActive)
                {
                    Log.Instance.Trace($"Game Mode already active on startup, checking foreground process...");
                    TryPinForegroundProcess();
                }
            }
        }

        await _instanceStartedEventAutoAutoListener.SubscribeChangedAsync(InstanceStartedEventAutoAutoListener_Changed)
            .ConfigureAwait(false);
        await _instanceStoppedEventAutoAutoListener.SubscribeChangedAsync(InstanceStoppedEventAutoAutoListener_Changed)
            .ConfigureAwait(false);
    }

    protected override async Task StopAsync()
    {
        await _instanceStartedEventAutoAutoListener
            .UnsubscribeChangedAsync(InstanceStartedEventAutoAutoListener_Changed).ConfigureAwait(false);
        await _instanceStoppedEventAutoAutoListener
            .UnsubscribeChangedAsync(InstanceStoppedEventAutoAutoListener_Changed).ConfigureAwait(false);

        await _gameConfigStoreDetector.StopAsync().ConfigureAwait(false);
        await _effectiveGameModeDetector.StopAsync().ConfigureAwait(false);

        _gpuController.Refreshed -= GpuController_Refreshed;
        _gpuController.Release(this);

        lock (Lock)
        {
            StopReconciling();

            if (!_preserveStateOnNextStart)
            {
                _games.Clear();
                _detectedGamePathsCache.Clear();
                if (_lastState)
                {
                    _lastState = false;
                    RaiseChanged(new ChangedEventArgs(false));
                }
            }
            else
            {
                _detectedGamePathsCache.Clear();
                Log.Instance.Trace($"Preserving games during restart: {_games.Count} game(s).");
            }
        }
    }

    public bool AreGamesRunning()
    {
        lock (Lock)
        {
            return _lastState;
        }
    }

    public void PreserveStateOnRestart()
    {
        lock (Lock)
        {
            _preserveStateOnNextStart = _lastState;
            Log.Instance.Trace($"Will preserve state on next start: {_preserveStateOnNextStart}");
        }
    }

    /// <summary>Every running process's id and name, from one snapshot of the process list (nothing is opened).</summary>
    private static List<(int Id, string Name)> RunningProcesses()
    {
        var processes = Process.GetProcesses();
        try
        {
            return processes.Select(p => (p.Id, p.ProcessName)).ToList();
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    /// <summary>Starts counting a game. Called under <see cref="Lock"/>.</summary>
    private void Track(int processId, string name, string source, string? path, string message = "Game detected", bool pinnedByGameMode = false)
    {
        if (!_games.Add(processId, name, source, pinnedByGameMode))
            return;

        Log.Instance.Trace($"{message}: {name} [Source: {source}] [pid={processId}, path={path ?? "Unknown"}].");
        StartReconcilingIfNeeded();
        RaiseChangedIfNeeded(true);
    }

    /// <summary>Called under <see cref="Lock"/>.</summary>
    private void TrackGpuProcesses(IEnumerable<GpuProcess> processes, string message)
    {
        foreach (var process in processes)
        {
            if (_games.Contains(process.Id) || IsBlacklisted(process.Name))
                continue;

            Track(process.Id, process.Name, "Discrete GPU", ProcessImagePath.Get(process.Id), message);
        }
    }

    private void GpuController_Refreshed(object? sender, GPUStatus e)
    {
        try
        {
            lock (Lock)
                TrackGpuProcesses(e.Processes, "Game detected");
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to check GPU processes.", ex);
        }
    }

    private void GameConfigStoreDetectorGamesConfigStoreDetected(object? sender,
        GameConfigStoreDetector.GameDetectedEventArgs e)
    {
        var running = RunningProcesses();

        lock (Lock)
        {
            _detectedGamePathsCache.Clear();

            foreach (var game in e.Games)
            {
                _detectedGamePathsCache.Add(game);

                foreach (var (id, name) in running.Where(p => string.Equals(p.Name, game.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        if (IsBlacklisted(name))
                            continue;

                        var processPath = ProcessImagePath.Get(id);
                        if (processPath is not null && game.ExecutablePath is not null &&
                            !game.ExecutablePath.Equals(processPath, StringComparison.OrdinalIgnoreCase))
                            continue;

                        Track(id, name, "Known Game List", processPath ?? game.ExecutablePath);
                        RaiseChangedIfNeeded(true);
                    }
                    catch (Exception)
                    {
                        Log.Instance.Trace($"Can't get game \"{game}\" details.");
                    }
                }
            }
        }
    }

    private void EffectiveGameModeDetectorChanged(object? sender, bool e)
    {
        try
        {
            if (e)
            {
                lock (Lock)
                    TryPinForegroundProcess();
                return;
            }

            var running = RunningProcesses();
            lock (Lock)
            {
                if (!_games.AnyPinnedByGameMode)
                    return;

                foreach (var ended in _games.Reconcile(running))
                    Log.Instance.Trace($"Game Mode ended and process exited: {ended.Name} [Source: {ended.Source}] [pid={ended.Id}].");

                if (_games.IsEmpty)
                {
                    StopReconciling();
                    RaiseChangedIfNeeded(false);
                }
                else
                {
                    Log.Instance.Trace($"Game Mode deactivation ignored: {_games.Count} active game(s).");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to handle a Windows Game Mode change.", ex);
        }
    }

    /// <summary>The program Windows Game Mode turned on for: the foreground window's process. Called under <see cref="Lock"/>.</summary>
    private unsafe void TryPinForegroundProcess()
    {
        try
        {
            var hWnd = Windows.Win32.PInvoke.GetForegroundWindow();
            if (hWnd == Windows.Win32.Foundation.HWND.Null)
                return;

            uint foregroundId = 0;
            Windows.Win32.PInvoke.GetWindowThreadProcessId(hWnd, &foregroundId);
            var processId = (int)foregroundId;
            if (processId == 0 || _games.Contains(processId))
                return;

            var name = RunningProcesses().Where(p => p.Id == processId).Select(p => p.Name).FirstOrDefault();
            if (name is null)
                return;

            var processPath = ProcessImagePath.Get(processId);
            if (IsBlacklisted(name))
            {
                Log.Instance.Trace($"Ignoring blacklisted process: {name} [Source: Windows Game Mode] [pid={processId}, path={processPath ?? "Unknown"}].");
                return;
            }

            Track(processId, name, "Windows Game Mode", processPath, pinnedByGameMode: true);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to pin process on Game Mode detection.", ex);
        }
    }

    private bool IsBlacklisted(string processName)
    {
        if (_settings.Store.IncludedProcesses.Contains(processName, StringComparer.OrdinalIgnoreCase))
            return false;

        if (_settings.Store.ExcludedProcesses.Contains(processName, StringComparer.OrdinalIgnoreCase))
            return true;

        return processName.Equals("explorer", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("Lenovo Legion Toolkit", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("SearchUI", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("LockApp", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("TextInputHost", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("SearchHost", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("dwm", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("csrss", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("WmiApSrv", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("HWiNFO64", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("HWiNFO32", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("nvidia-smi", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("steamwebhelper", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("EpicWebHelper", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("GalaxyCommunicationService", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("GalaxyClientHelper", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("EABackgroundService", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("Link2EA", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("RiotClientCrashHandler", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("CrashReportClient", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("UnityCrashHandler64", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("UnityCrashHandler32", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("UE4-CrashTracker", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("crs-handler", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("cefsharp.browsersubprocess", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("conhost", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("nvngx_update", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("GameGuard", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("GameGuard.des", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("GameMon", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("GameMon64", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("EasyAntiCheat", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("EasyAntiCheat_EOS", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("BEService", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("BEService_x64", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("vgc", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("vgtray", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("EAAntiCheat.GameService", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("EAAntiCheat.GameServiceLauncher", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("ACE-Base", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("UbisoftConnect", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("UbisoftGameLauncher", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("Battle.net Helper", StringComparison.OrdinalIgnoreCase)
               || processName.Equals("Agent", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether a started process, as Windows names it in the start event, is one of <paramref name="names"/>
    /// (names without ".exe"). The event carries only the first 15 characters of the file name, so a long name is
    /// compared the same way game profiles do (<see cref="GameProfiles.GameProfileController.IsGame"/>). The full
    /// path is still checked against the known-games list afterwards.
    /// </summary>
    internal static bool MatchesStartedName(string reportedName, IEnumerable<string> names) =>
        names.Any(name => GameProfiles.GameProfileController.IsGame(reportedName, name));

    private void InstanceStartedEventAutoAutoListener_Changed(object? sender,
        InstanceStartedEventAutoAutoListener.ChangedEventArgs e)
    {
        lock (Lock)
        {
            try
            {
                if (e.ProcessId <= 0 || _games.Contains(e.ProcessId))
                    return;

                var isIncluded = MatchesStartedName(e.ProcessName, _settings.Store.IncludedProcesses);
                var isGameConfigStore = _settings.Store.GameDetection.UseGameConfigStore &&
                                        MatchesStartedName(e.ProcessName, _detectedGamePathsCache.Select(p => p.Name));

                if (!isIncluded && !isGameConfigStore)
                    return;

                var processPath = ProcessImagePath.Get(e.ProcessId);
                var source = isIncluded ? "Included" : "Known Game List";

                if (!isIncluded && string.IsNullOrEmpty(processPath))
                {
                    Log.Instance.Trace($"Can't get path for {e.ProcessName} [Source: {source}] [pid={e.ProcessId}].");
                    return;
                }

                var processInfo = string.IsNullOrEmpty(processPath)
                    ? new ProcessInfo(e.ProcessName, null)
                    : ProcessInfo.FromPath(processPath);

                if (!isIncluded && !_detectedGamePathsCache.Contains(processInfo))
                    return;

                if (IsBlacklisted(e.ProcessName))
                {
                    Log.Instance.Trace($"Ignoring blacklisted process: {e.ProcessName} [Source: {source}] [pid={e.ProcessId}, path={processPath ?? "Unknown"}].");
                    return;
                }

                Track(e.ProcessId, e.ProcessName, source, processPath);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to handle a started game {e.ProcessName} [pid={e.ProcessId}].", ex);
            }
        }
    }

    /// <summary>Runs on the process-stop listener's thread, where an exception would end the app: nothing may escape.</summary>
    private void InstanceStoppedEventAutoAutoListener_Changed(object? sender,
        InstanceStoppedEventAutoAutoListener.ChangedEventArgs e)
    {
        try
        {
            lock (Lock)
            {
                if (!_games.Remove(e.ProcessId))
                    return;

                Log.Instance.Trace($"Process exited: {e.ProcessName} [pid={e.ProcessId}].");
                EndIfNoGamesLeft();
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to handle a game exiting.", ex);
            Log.Instance.ErrorReport("Game listener: failed to handle a game exiting (LLT kept running).", ex);
        }
    }

    /// <summary>Called under <see cref="Lock"/>.</summary>
    private void EndIfNoGamesLeft()
    {
        if (!_games.IsEmpty)
        {
            Log.Instance.Trace($"Active games remaining: {_games.Count}.");
            return;
        }

        Log.Instance.Trace($"No more games running.");
        StopReconciling();
        RaiseChangedIfNeeded(false);
    }

    /// <summary>Called under <see cref="Lock"/>.</summary>
    private void StartReconcilingIfNeeded()
    {
        if (_reconcileTimer is not null || _games.IsEmpty)
            return;

        _reconcileTimer = new Timer(_ => Reconcile(), null, ReconcileInterval, ReconcileInterval);
    }

    /// <summary>Called under <see cref="Lock"/>.</summary>
    private void StopReconciling()
    {
        _reconcileTimer?.Dispose();
        _reconcileTimer = null;
    }

    /// <summary>
    /// Drops games the process list no longer shows, in case Windows' stop event was missed. Runs on a timer thread:
    /// nothing may escape.
    /// </summary>
    private void Reconcile()
    {
        try
        {
            var running = RunningProcesses();
            lock (Lock)
            {
                var gone = _games.Reconcile(running);
                if (gone.Count == 0)
                    return;

                Log.Instance.Trace($"Games that ended without being reported were dropped. [pids={string.Join(",", gone.Select(g => g.Id))}]");
                EndIfNoGamesLeft();
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Checking running games failed.", ex);
        }
    }

    /// <summary>Called under <see cref="Lock"/>.</summary>
    private void RaiseChangedIfNeeded(bool newState)
    {
        if (newState == _lastState)
            return;

        _lastState = newState;
        Log.Instance.Trace($"Game running state changed: Running={newState} [activeGamesCount={_games.Count}].");

        RaiseChanged(new ChangedEventArgs(newState));
    }
}
