using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.GameProfiles;

public enum BackgroundServiceState
{
    /// <summary>LLT stopped it and it is still stopped.</summary>
    StoppedByLlt,

    /// <summary>LLT stopped it, but it is running again (Windows or an app started it).</summary>
    RunningAgain,

    /// <summary>Chosen, but not stopped yet (the setting is off, or Apply hasn't run).</summary>
    Running,

    /// <summary>Already stopped by something else; LLT leaves it as it is.</summary>
    StoppedAlready,

    NotInstalled,

    /// <summary>LLT won't stop it (<see cref="ServiceRules"/>): protected, a driver, can't stop, or others depend on it.</summary>
    Refused,
}

public sealed record BackgroundServiceStatus(string Name, string DisplayName, BackgroundServiceState State, ServiceRefusal? Refusal);

/// <summary>
/// Keeps the services of the chosen presets stopped while LLT runs. It only stops and starts them; Windows' own
/// service settings (startup type) are never changed, so closing or uninstalling LLT fully undoes it. Every
/// service LLT stops is written to a note first, and only those are started again: when LLT exits, when the setting
/// is turned off or a preset unticked, and at the next start after LLT was ended. A service something else stopped,
/// or a game profile paused, is never started by this controller.
/// </summary>
public class BackgroundServicesController
{
    private static readonly TimeSpan ServiceTimeout = TimeSpan.FromSeconds(20);

    private readonly BackgroundServicesSettings _settings;
    private readonly BackgroundServicesSessionSettings _session;
    private readonly IGameProfileSystem _system;
    private readonly Action<string, Exception> _reportError;
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    public BackgroundServicesController(BackgroundServicesSettings settings, BackgroundServicesSessionSettings session)
        : this(settings, session, new WindowsGameProfileSystem(), Log.Instance.ErrorReport) { }

    /// <summary>For tests: Windows' services and the error report are supplied by the caller.</summary>
    internal BackgroundServicesController(BackgroundServicesSettings settings, BackgroundServicesSessionSettings session, IGameProfileSystem system, Action<string, Exception> reportError)
    {
        _settings = settings;
        _session = session;
        _system = system;
        _reportError = reportError;
    }

    /// <summary>Raised after an apply or a put-back, so the window can show the new states. May be raised on any thread.</summary>
    public event EventHandler? Changed;

    /// <summary>The services of the chosen presets, in preset order, without repeats.</summary>
    public IReadOnlyList<string> ChosenServices() =>
        ServicePresets.All
            .Where(p => _settings.Store.Presets.Contains(p.Id, StringComparer.OrdinalIgnoreCase))
            .SelectMany(p => p.Services)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Makes Windows match the settings: services no longer wanted that LLT stopped are started again, and every
    /// wanted service that may be stopped and is running (including one running again) is stopped, noted first.
    /// Called at LLT's start, after the settings change, and by "Stop now".
    /// </summary>
    public async Task ApplyAsync()
    {
        await _oneAtATime.WaitAsync().ConfigureAwait(false);
        try
        {
            var wanted = _settings.Store.Enabled ? ChosenServices() : [];

            foreach (var name in _session.Store.StoppedServices.ToArray().Reverse())
            {
                if (!wanted.Contains(name, StringComparer.OrdinalIgnoreCase))
                    await StartAgainAsync(name).ConfigureAwait(false);
            }

            foreach (var name in wanted)
                await StopAsync(name).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _reportError("Background services: apply failed.", ex);
        }
        finally
        {
            _oneAtATime.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Starts again every service LLT stopped (LLT exiting, or "Start all again"). The settings are kept.</summary>
    public async Task RestoreAllAsync()
    {
        await _oneAtATime.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var name in _session.Store.StoppedServices.ToArray().Reverse())
                await StartAgainAsync(name).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _reportError("Background services: putting services back failed.", ex);
        }
        finally
        {
            _oneAtATime.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// How many services Indra has stopped and will start again, from its own note (no service is queried, so this
    /// is cheap enough for the dashboard's "Active now" strip).
    /// </summary>
    public int NotedCount => _session.Store.StoppedServices.Count;

    /// <summary>What each chosen or noted service is doing now, for the window.</summary>
    public IReadOnlyList<BackgroundServiceStatus> GetStatus()
    {
        var noted = _session.Store.StoppedServices;
        var names = ChosenServices().Concat(noted).Distinct(StringComparer.OrdinalIgnoreCase);

        return names.Select(name =>
        {
            var info = SafeRead(name);
            var stoppedByLlt = noted.Contains(name, StringComparer.OrdinalIgnoreCase);
            var refusal = ServiceRules.Check(name, info);

            var state = (info, stoppedByLlt, refusal) switch
            {
                (null, _, _) => BackgroundServiceState.NotInstalled,
                ({ Running: false }, true, _) => BackgroundServiceState.StoppedByLlt,
                ({ Running: true }, true, _) => BackgroundServiceState.RunningAgain,
                ({ Running: false }, false, _) => BackgroundServiceState.StoppedAlready,
                (_, _, { } and not ServiceRefusal.NotRunning) => BackgroundServiceState.Refused,
                _ => BackgroundServiceState.Running,
            };

            return new BackgroundServiceStatus(name, info?.DisplayName ?? name, state, state == BackgroundServiceState.Refused ? refusal : null);
        }).ToList();
    }

    private async Task StopAsync(string name)
    {
        var info = SafeRead(name);
        var refusal = ServiceRules.Check(name, info);
        if (refusal is not null)
        {
            if (refusal is not (ServiceRefusal.NotRunning or ServiceRefusal.NotInstalled))
                Log.Instance.Trace($"Background services: not stopping {name}: {refusal}.");
            return;
        }

        if (!_session.Store.StoppedServices.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            _session.Store.StoppedServices.Add(name);
            _session.SynchronizeStore();
        }

        var stopped = await _system.StopServiceAsync(name, ServiceTimeout).ConfigureAwait(false);
        if (stopped)
        {
            Log.Instance.Trace($"Background services: stopped {name}.");
            return;
        }

        // Not stopped and still running: nothing to put back later.
        if (SafeRead(name) is { Running: true })
        {
            _session.Store.StoppedServices.RemoveAll(s => string.Equals(s, name, StringComparison.OrdinalIgnoreCase));
            _session.SynchronizeStore();
        }

        Log.Instance.Trace($"Background services: couldn't stop {name}.");
    }

    /// <summary>
    /// Starts a service LLT stopped. It leaves the note once it runs (or is gone); one that won't start stays noted,
    /// so the next apply or start of LLT tries again.
    /// </summary>
    private async Task StartAgainAsync(string name)
    {
        if (SafeRead(name) is { Running: false })
        {
            if (!await _system.StartServiceAsync(name, ServiceTimeout).ConfigureAwait(false))
            {
                _reportError($"Background services: couldn't start {name} again.", new InvalidOperationException($"{name} didn't start."));
                return;
            }

            Log.Instance.Trace($"Background services: started {name} again.");
        }

        _session.Store.StoppedServices.RemoveAll(s => string.Equals(s, name, StringComparison.OrdinalIgnoreCase));
        _session.SynchronizeStore();
    }

    private ServiceInfo? SafeRead(string name)
    {
        try
        {
            return _system.ReadService(name);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Background services: couldn't read {name}.", ex);
            return null;
        }
    }
}
