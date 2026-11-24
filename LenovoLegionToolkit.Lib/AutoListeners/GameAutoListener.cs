using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Extensions;
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

                NotifyGamesChanged();   // the list may have shrunk (the dashboard's "Active now" follows it)
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
                RaiseChangedIfNeeded(false);
                NotifyGamesChanged();
            }
            else
            {
                _detectedGamePathsCache.Clear();
                Log.Instance.Trace($"Preserving games during restart: {_games.Count} game(s).");

                // Kept only for the restart that is meant to follow. If none does (nothing subscribes again), detection
                // is off, and "a game is running" mustn't stay true with nothing watching.
                var generation = ++_preserveGeneration;
                Task.Delay(PreserveTimeout).ContinueWith(_ => DropPreservedGames(generation), TaskScheduler.Default)
                    .Forget("Dropping kept games");
            }
        }
    }

    /// <summary>How long games are kept across a stop for a restart that should follow at once.</summary>
    internal static readonly TimeSpan PreserveTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Counts preserving stops, so an old time-out never drops games kept for a newer restart. Guarded by Lock.</summary>
    private int _preserveGeneration;

    private void DropPreservedGames(int generation)
    {
        lock (Lock)
        {
            if (!_preserveStateOnNextStart || generation != _preserveGeneration)
                return;   // restarted meanwhile (the start used the kept games), or kept again since

            Log.Instance.Trace($"No restart followed; dropping {_games.Count} kept game(s).");
            _preserveStateOnNextStart = false;
            _games.Clear();
            RaiseChangedIfNeeded(false);
            NotifyGamesChanged();
        }
    }

    /// <summary>Nothing restarted (others still use detection): a "keep games for the restart" request no longer applies.</summary>
    protected override void OnNoRestart()
    {
        lock (Lock)
        {
            if (!_preserveStateOnNextStart)
                return;

            _preserveStateOnNextStart = false;
            Log.Instance.Trace($"Game detection kept running; the request to keep games for a restart is cleared.");
        }
    }

    public bool AreGamesRunning()
    {
        lock (Lock)
        {
            return _lastState;
        }
    }

    /// <summary>
    /// Raised (on any thread) when a game starts counting as running or the last one ends. Unlike subscribing with
    /// <c>SubscribeChangedAsync</c>, listening to this doesn't start game detection; it only says what detection
    /// already running sees.
    /// </summary>
    public event EventHandler<bool>? RunningChanged;

    /// <summary>Raised (on any thread) when the list of running games changes. Doesn't start game detection either.</summary>
    public event EventHandler? GamesChanged;

    /// <summary>The names of the games counted as running now (empty while detection isn't running).</summary>
    public IReadOnlyList<string> RunningGames
    {
        get
        {
            lock (Lock)
                return [.. _games.Games.Select(g => g.Name).Distinct(StringComparer.OrdinalIgnoreCase)];
        }
    }

    /// <summary>Called under <see cref="Lock"/> after the tracked games changed. A failing listener never reaches detection.</summary>
    private void NotifyGamesChanged()
    {
        try
        {
            GamesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"A game-list listener failed.", ex);
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
        NotifyGamesChanged();
    }

    /// <summary>Called under <see cref="Lock"/>.</summary>
    private void TrackGpuProcesses(IEnumerable<GpuProcess> processes, string message)
    {
        foreach (var process in processes)
        {
            // Indra itself is never a game, whatever name the driver gives it.
            if (process.Id == Environment.ProcessId || _games.Contains(process.Id) || IsBlacklisted(process.Name))
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

                var endedGames = _games.Reconcile(running);
                foreach (var ended in endedGames)
                    Log.Instance.Trace($"Game Mode ended and process exited: {ended.Name} [Source: {ended.Source}] [pid={ended.Id}].");
                if (endedGames.Count > 0)
                    NotifyGamesChanged();

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

        return IsNeverAGame(processName);
    }

    /// <summary>
    /// Programs that are never games, whatever finds them (most often the discrete-GPU source, which sees every app that
    /// draws on the NVIDIA GPU): Windows' shell, Indra, monitoring tools, launchers and their helpers, overlays,
    /// browser engines and anti-cheat services. The Included and Excluded settings are checked before this.
    /// </summary>
    private static readonly HashSet<string> NeverGames = new(StringComparer.OrdinalIgnoreCase)
    {
        // Windows
        "explorer", "SearchUI", "LockApp", "TextInputHost", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost",
        "dwm", "csrss", "WmiApSrv", "conhost", "SnippingTool",
        // Indra
        "Lenovo Legion Toolkit",
        // Monitoring and NVIDIA tools
        "HWiNFO64", "HWiNFO32", "nvidia-smi", "nvngx_update", "NVIDIA App", "NVIDIA Overlay", "nvcontainer",
        // Game Bar and the Xbox app
        "GameBar", "XboxGameBarWidgets", "XboxPcApp", "GameBarFTServer",
        // Browser engines
        "msedge", "msedgewebview2", "cefsharp.browsersubprocess",
        // Launchers and their helpers
        "steam", "steamwebhelper", "EpicWebHelper", "GalaxyCommunicationService", "GalaxyClientHelper", "EABackgroundService",
        "Link2EA", "UbisoftConnect", "UbisoftGameLauncher", "Battle.net Helper", "Agent",
        // Crash reporters
        "RiotClientCrashHandler", "CrashReportClient", "UnityCrashHandler64", "UnityCrashHandler32", "UE4-CrashTracker", "crs-handler",
        // Anti-cheat services
        "GameGuard", "GameGuard.des", "GameMon", "GameMon64", "EasyAntiCheat", "EasyAntiCheat_EOS", "BEService", "BEService_x64",
        "vgc", "vgtray", "EAAntiCheat.GameService", "EAAntiCheat.GameServiceLauncher", "ACE-Base",
    };

    /// <summary>Whether a process name (no ".exe") is on the built-in never-a-game list.</summary>
    internal static bool IsNeverAGame(string processName) => NeverGames.Contains(processName);

    /// <summary>
    /// Whether a started process, as Windows names it in the start event, is one of <paramref name="names"/>
    /// (names without ".exe"). The event carries only the first 15 characters of the file name, so a long name is
    /// compared the same way game profiles do (<see cref="GameProfiles.GameProfileController.IsGame"/>). The full
    /// path is still checked against the known-games list afterwards.
    /// </summary>
    /// <summary>
    /// The name to check against the exclusion and never-a-game lists: the file name from the full path when it was read
    /// (no ".exe"), otherwise the start event's name, which may be cut to 15 characters.
    /// </summary>
    internal static string NameToCheck(string reportedName, string? processPath) =>
        string.IsNullOrEmpty(processPath) ? reportedName : ProcessInfo.FromPath(processPath).Name;

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

                // The start event cuts names to 15 characters; with a path, the full name decides (an excluded
                // "SomeLongGameName" arrives as "SomeLongGameNa" and used to slip past the exclusion list).
                var name = NameToCheck(e.ProcessName, processPath);
                if (IsBlacklisted(name))
                {
                    Log.Instance.Trace($"Ignoring blacklisted process: {name} [Source: {source}] [pid={e.ProcessId}, path={processPath ?? "Unknown"}].");
                    return;
                }

                Track(e.ProcessId, name, source, processPath);
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
                NotifyGamesChanged();
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
                NotifyGamesChanged();
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

        try
        {
            RunningChanged?.Invoke(this, newState);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"A game-running listener failed.", ex);
        }
    }
}
