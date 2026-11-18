using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.AutoListeners;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.GameProfiles;

/// <summary>What a game profile did when its game started (or what was put back when it ended).</summary>
public sealed record GameProfileReport(string GameName, bool Started, int AppsEased, int AppsClosed, int AppsLeftOpen, int ServicesPaused, int Problems);

/// <summary>
/// Game profiles: while a chosen game runs, LLT can force game-safe mode, ease listed apps (Windows efficiency
/// mode and a lower priority), ask listed apps to close, and stop listed services; when the game ends, everything
/// it changed is put back.
/// <para>
/// The game is recognised from the names Windows reports for started and ended processes, and from the process
/// list: LLT never opens, reads or changes the game's process. Only apps and services the user listed are touched,
/// a fixed deny list protects critical services (<see cref="ServiceRules"/>), and every change is written to a
/// session note first, so it is put back when the game ends, when LLT exits, or at the next start if LLT was
/// ended while the game ran. Closed apps aren't reopened.
/// </para>
/// </summary>
public class GameProfileController
{
    public static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan ServiceTimeout = TimeSpan.FromSeconds(20);


    private readonly GameProfileSettings _settings;
    private readonly GameProfileSessionSettings _session;
    private readonly IGameProfileSystem _system;
    private readonly Action<string, Exception> _reportError;
    private readonly InstanceStartedEventAutoAutoListener? _startedListener;
    private readonly InstanceStoppedEventAutoAutoListener? _stoppedListener;

    /// <summary>One start or end of a session at a time.</summary>
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    /// <summary>Guards <see cref="_gamePids"/>; never held across a Windows call.</summary>
    private readonly object _lock = new();

    /// <summary>Running processes of profiled games, by id, with their profile.</summary>
    private readonly Dictionary<int, Guid> _gamePids = [];

    private bool _subscribed;

    public GameProfileController(GameProfileSettings settings, GameProfileSessionSettings session,
        InstanceStartedEventAutoAutoListener startedListener, InstanceStoppedEventAutoAutoListener stoppedListener)
        : this(settings, session, new WindowsGameProfileSystem(), Log.Instance.ErrorReport)
    {
        _startedListener = startedListener;
        _stoppedListener = stoppedListener;
    }

    /// <summary>For tests: Windows' answers and the error report are supplied by the caller; no process events are listened to.</summary>
    internal GameProfileController(GameProfileSettings settings, GameProfileSessionSettings session, IGameProfileSystem system, Action<string, Exception> reportError)
    {
        _settings = settings;
        _session = session;
        _system = system;
        _reportError = reportError;
    }

    /// <summary>True while the running game's profile asks for game-safe mode.</summary>
    public bool ForceGameSafe { get; private set; }

    /// <summary>Raised when <see cref="ForceGameSafe"/> changes (on a background thread).</summary>
    public event EventHandler? ForceGameSafeChanged;

    /// <summary>Raised after a profile was applied or put back (on a background thread).</summary>
    public event EventHandler<GameProfileReport>? Reported;

    /// <summary>The last apply or put-back, for the window.</summary>
    public GameProfileReport? LastReport { get; private set; }

    /// <summary>The game whose profile is in effect, or null.</summary>
    public string? ActiveGameName => _session.Store.ProfileId is null ? null : _session.Store.GameName;

    /// <summary>
    /// At startup: puts back whatever a session LLT didn't get to finish changed (LLT was ended while a game
    /// ran), then watches for profiled games, including ones already running.
    /// </summary>
    public async Task StartAsync()
    {
        await RecoverAsync().ConfigureAwait(false);
        await RefreshAsync().ConfigureAwait(false);
    }

    /// <summary>After the profiles changed: ends a session whose profile is gone or off, and updates what is watched.</summary>
    public async Task RefreshAsync()
    {
        try
        {
            var enabled = EnabledProfiles();

            if (_session.Store.ProfileId is { } activeId && enabled.All(p => p.Id != activeId))
                await EndSessionAsync().ConfigureAwait(false);

            await UpdateSubscriptionAsync(enabled.Count > 0).ConfigureAwait(false);

            // Games already running: found in the process list, without opening them.
            foreach (var profile in enabled)
                foreach (var pid in _system.FindByName(profile.ExeName))
                    await ProcessStartedAsync(profile.ExeName, pid).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Updating game profiles failed.", ex);
            _reportError("Updating game profiles failed.", ex);
        }
    }

    /// <summary>Puts back what a session left behind; used at startup.</summary>
    internal async Task RecoverAsync()
    {
        if (_session.Store.ProfileId is null)
            return;

        Log.Instance.Trace($"Putting back what the game profile for {_session.Store.GameName} changed before LLT was ended.");
        await EndSessionAsync().ConfigureAwait(false);
    }

    private List<GameProfile> EnabledProfiles() =>
        _settings.Store.Profiles.Where(p => p.Enabled && !string.IsNullOrWhiteSpace(p.ExeName)).ToList();

    // ---- Watching processes ----

    private async Task UpdateSubscriptionAsync(bool needed)
    {
        if (_startedListener is null || _stoppedListener is null || needed == _subscribed)
            return;

        _subscribed = needed;
        if (needed)
        {
            await _startedListener.SubscribeChangedAsync(Started_Changed).ConfigureAwait(false);
            await _stoppedListener.SubscribeChangedAsync(Stopped_Changed).ConfigureAwait(false);
        }
        else
        {
            await _startedListener.UnsubscribeChangedAsync(Started_Changed).ConfigureAwait(false);
            await _stoppedListener.UnsubscribeChangedAsync(Stopped_Changed).ConfigureAwait(false);
        }
    }

    private void Started_Changed(object? sender, InstanceStartedEventAutoAutoListener.ChangedEventArgs e) => _ = ProcessStartedAsync(e.ProcessName, e.ProcessId);

    private void Stopped_Changed(object? sender, InstanceStoppedEventAutoAutoListener.ChangedEventArgs e) => _ = ProcessStoppedAsync(e.ProcessName, e.ProcessId);

    /// <summary>
    /// Whether a process name Windows reported is this game. The process trace may cut names to the 15 characters
    /// of the kernel's image name (".exe" included), so a cut name also matches.
    /// </summary>
    public static bool IsGame(string reportedName, string exeName)
    {
        if (string.Equals(reportedName, exeName, StringComparison.OrdinalIgnoreCase))
            return true;

        var fullName = exeName + ".exe";
        return fullName.Length > 15
               && string.Equals(reportedName, Path.GetFileNameWithoutExtension(fullName[..15]), StringComparison.OrdinalIgnoreCase);
    }

    internal async Task ProcessStartedAsync(string name, int pid)
    {
        try
        {
            var profile = EnabledProfiles().FirstOrDefault(p => IsGame(name, p.ExeName));
            if (profile is null)
                return;

            lock (_lock)
                _gamePids[pid] = profile.Id;

            if (_session.Store.ProfileId is null)
                await StartSessionAsync(profile).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Handling a started game failed. [name={name}, pid={pid}]", ex);
            _reportError("Handling a started game failed.", ex);
        }
    }

    internal async Task ProcessStoppedAsync(string name, int pid)
    {
        try
        {
            Guid profileId;
            bool stillRunning;
            lock (_lock)
            {
                if (!_gamePids.Remove(pid, out profileId))
                    return;
                stillRunning = _gamePids.ContainsValue(profileId);
            }

            if (stillRunning || _session.Store.ProfileId != profileId)
                return;

            await EndSessionAsync().ConfigureAwait(false);

            // Another profiled game may still be running; its profile applies now.
            Guid? next;
            lock (_lock)
                next = _gamePids.Values.Cast<Guid?>().FirstOrDefault();

            if (next is { } nextId && EnabledProfiles().FirstOrDefault(p => p.Id == nextId) is { } nextProfile)
                await StartSessionAsync(nextProfile).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Handling an ended game failed. [name={name}, pid={pid}]", ex);
            _reportError("Handling an ended game failed.", ex);
        }
    }

    // ---- Applying ----

    private async Task StartSessionAsync(GameProfile profile)
    {
        await _oneAtATime.WaitAsync().ConfigureAwait(false);
        try
        {
            bool gameRunning;
            lock (_lock)
                gameRunning = _gamePids.ContainsValue(profile.Id);

            if (_session.Store.ProfileId is not null || !gameRunning)
                return;

            Log.Instance.Trace($"Game profile for {profile.Name} starting.");

            // The note exists before anything changes, so even a change made just before LLT is ended is put back.
            _session.Store.ProfileId = profile.Id;
            _session.Store.GameName = profile.Name;
            _session.Store.PausedServices = [];
            _session.Store.EasedApps = [];
            _session.SynchronizeStore();

            var counts = new Counts();
            EaseApps(profile, counts);
            await CloseAppsAsync(profile, counts).ConfigureAwait(false);
            await PauseServicesAsync(profile, counts).ConfigureAwait(false);

            if (profile.ForceGameSafe)
                SetForceGameSafe(true);

            Publish(new GameProfileReport(profile.Name, true, counts.Eased, counts.Closed, counts.LeftOpen, counts.Paused, counts.Problems));
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    private sealed class Counts
    {
        public int Eased, Closed, LeftOpen, Paused, Problems;
    }

    /// <summary>Never the game itself, nor LLT or Windows' own shell and system processes (<see cref="AppRules"/>).</summary>
    private static bool IsOffLimits(string app, GameProfile profile) =>
        IsGame(app, profile.ExeName) || AppRules.IsProtectedName(app);

    /// <summary>Only apps in the user's own session, outside the Windows folder.</summary>
    private static bool IsUserApp(AppProcess app) => app.SessionId != 0 && AppRules.IsAllowedPath(app.Path);

    private void EaseApps(GameProfile profile, Counts counts)
    {
        // An app also listed to close is only closed: easing it first would be pointless.
        foreach (var exe in profile.EfficiencyApps.Distinct(StringComparer.OrdinalIgnoreCase)
                     .Where(e => !profile.CloseApps.Contains(e, StringComparer.OrdinalIgnoreCase)))
        {
            if (IsOffLimits(exe, profile))
            {
                counts.Problems++;
                continue;
            }

            foreach (var pid in _system.FindByName(exe))
            {
                try
                {
                    if (_system.ReadApp(pid) is not { } app)
                        continue;

                    if (!IsUserApp(app) || app.Priority == ProcessPriorityClass.RealTime)
                    {
                        counts.Problems++;
                        continue;
                    }

                    var lowerPriority = app.Priority is ProcessPriorityClass.Normal or ProcessPriorityClass.AboveNormal or ProcessPriorityClass.High;
                    // Efficiency mode only when Windows reports the app's setting and it isn't on already.
                    ThrottleState? throttle = app.Throttle is { IsEfficient: false } ? ThrottleState.Efficient : null;
                    if (!lowerPriority && throttle is null)
                        continue;   // already as eased as LLT would make it

                    _session.Store.EasedApps.Add(new EasedApp
                    {
                        ProcessId = pid,
                        StartTimeUtc = app.StartTimeUtc,
                        ExeName = exe,
                        OriginalPriority = app.Priority,
                        OriginalThrottleControlMask = throttle is null ? null : app.Throttle!.Value.ControlMask,
                        OriginalThrottleStateMask = throttle is null ? null : app.Throttle!.Value.StateMask,
                    });
                    _session.SynchronizeStore();

                    if (_system.SetApp(pid, app.StartTimeUtc, lowerPriority ? ProcessPriorityClass.BelowNormal : app.Priority, throttle))
                        counts.Eased++;
                    else
                        counts.Problems++;
                }
                catch (Exception ex)
                {
                    counts.Problems++;
                    Log.Instance.Trace($"Easing an app failed. [exe={exe}, pid={pid}]", ex);
                }
            }
        }
    }

    private async Task CloseAppsAsync(GameProfile profile, Counts counts)
    {
        foreach (var exe in profile.CloseApps.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (IsOffLimits(exe, profile))
            {
                counts.Problems++;
                continue;
            }

            foreach (var pid in _system.FindByName(exe))
            {
                try
                {
                    if (_system.ReadApp(pid) is not { } app)
                        continue;

                    if (!IsUserApp(app))
                    {
                        counts.Problems++;
                        continue;
                    }

                    if (await _system.CloseAppAsync(pid, app.StartTimeUtc, CloseTimeout).ConfigureAwait(false))
                        counts.Closed++;
                    else
                        counts.LeftOpen++;
                }
                catch (Exception ex)
                {
                    counts.Problems++;
                    Log.Instance.Trace($"Closing an app failed. [exe={exe}, pid={pid}]", ex);
                }
            }
        }
    }

    private async Task PauseServicesAsync(GameProfile profile, Counts counts)
    {
        foreach (var name in profile.PauseServices.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var service = _system.ReadService(name);
                if (ServiceRules.Check(name, service) is { } refusal)
                {
                    if (refusal != ServiceRefusal.NotRunning)   // not running: nothing to pause, nothing wrong
                    {
                        counts.Problems++;
                        Log.Instance.Trace($"Not pausing service {name}: {refusal}.");
                    }
                    continue;
                }

                _session.Store.PausedServices.Add(service!.Name);
                _session.SynchronizeStore();

                if (await _system.StopServiceAsync(name, ServiceTimeout).ConfigureAwait(false))
                {
                    counts.Paused++;
                    continue;
                }

                counts.Problems++;
                if (_system.ReadService(name) is { Running: true })
                {
                    // Still running: there is nothing to put back for it.
                    _session.Store.PausedServices.Remove(service.Name);
                    _session.SynchronizeStore();
                }
            }
            catch (Exception ex)
            {
                counts.Problems++;
                Log.Instance.Trace($"Pausing a service failed. [name={name}]", ex);
            }
        }
    }

    // ---- Putting back ----

    /// <summary>
    /// Puts back everything the session changed, in reverse order: services started again, apps' priority and
    /// efficiency mode restored (only for the same process; an id now used by another process is left alone), and
    /// game-safe released. Called when the game ends, when LLT exits, and at startup after LLT was ended.
    /// </summary>
    public async Task EndSessionAsync()
    {
        await _oneAtATime.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_session.Store.ProfileId is null)
                return;

            var gameName = _session.Store.GameName ?? string.Empty;
            var counts = new Counts();

            foreach (var name in Enumerable.Reverse(_session.Store.PausedServices).ToList())
            {
                try
                {
                    var service = _system.ReadService(name);
                    if (service is null || service.Running)
                        continue;   // gone, or already running again

                    if (await _system.StartServiceAsync(name, ServiceTimeout).ConfigureAwait(false))
                        counts.Paused++;
                    else
                        counts.Problems++;
                }
                catch (Exception ex)
                {
                    counts.Problems++;
                    Log.Instance.Trace($"Starting service {name} again failed.", ex);
                }
            }

            foreach (var app in Enumerable.Reverse(_session.Store.EasedApps).ToList())
            {
                try
                {
                    ThrottleState? throttle = app.OriginalThrottleControlMask is { } control
                        ? new ThrottleState(control, app.OriginalThrottleStateMask ?? 0)
                        : null;

                    // False when the app has closed or its id is now another process's: nothing to put back.
                    if (_system.SetApp(app.ProcessId, app.StartTimeUtc, app.OriginalPriority, throttle))
                        counts.Eased++;
                }
                catch (Exception ex)
                {
                    counts.Problems++;
                    Log.Instance.Trace($"Restoring app {app.ExeName} failed.", ex);
                }
            }

            SetForceGameSafe(false);

            _session.Store.ProfileId = null;
            _session.Store.GameName = null;
            _session.Store.PausedServices = [];
            _session.Store.EasedApps = [];
            _session.SynchronizeStore();

            Log.Instance.Trace($"Game profile for {gameName} ended. [services={counts.Paused}, apps={counts.Eased}, problems={counts.Problems}]");
            Publish(new GameProfileReport(gameName, false, counts.Eased, 0, 0, counts.Paused, counts.Problems));
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Ending a game profile failed.", ex);
            _reportError("Ending a game profile failed.", ex);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    private void SetForceGameSafe(bool force)
    {
        if (ForceGameSafe == force)
            return;

        ForceGameSafe = force;
        try
        {
            ForceGameSafeChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Game-safe update after a game profile change failed.", ex);
        }
    }

    private void Publish(GameProfileReport report)
    {
        LastReport = report;
        try
        {
            Reported?.Invoke(this, report);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Reporting a game profile change failed.", ex);
        }
    }
}
