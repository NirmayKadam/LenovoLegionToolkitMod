using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Threading;

namespace LenovoLegionToolkit.Lib.Utils;

/// <summary>
/// Notices when a game protected by a kernel anti-cheat is running, by checking whether the anti-cheat's own
/// Windows service is running. It only asks the Service Control Manager for service status: it never looks at,
/// opens or enumerates game processes.
/// </summary>
/// <remarks>
/// The services start with a protected game and stop after it. Vanguard's boot driver (vgk) is always loaded
/// once installed; its user-mode client service (vgc) is what runs during a Riot game.
/// </remarks>
public sealed class AntiCheatMonitor : IDisposable
{
    /// <summary>Riot Vanguard, Easy Anti-Cheat (classic and Epic Online Services), BattlEye.</summary>
    public static readonly IReadOnlyList<string> WatchedServices = ["vgc", "EasyAntiCheat", "EasyAntiCheat_EOS", "BEService"];

    private readonly Func<string, bool> _isServiceRunning;
    private readonly object _lock = new();
    private Timer? _timer;

    public bool IsProtectedGameRunning { get; private set; }

    public IReadOnlyList<string> ActiveServices { get; private set; } = [];

    /// <summary>Raised on a change, with the new state. Raised on a background thread.</summary>
    public event EventHandler<bool>? ProtectedGameChanged;

    public AntiCheatMonitor() : this(IsServiceRunning) { }

    internal AntiCheatMonitor(Func<string, bool> isServiceRunning) => _isServiceRunning = isServiceRunning;

    public void Start(TimeSpan interval)
    {
        lock (_lock)
        {
            _timer ??= new Timer(_ => Poll(), null, TimeSpan.Zero, interval);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    /// <summary>Checks the services once; raises <see cref="ProtectedGameChanged"/> if the state changed.</summary>
    public bool Poll()
    {
        bool changed;
        bool state;

        lock (_lock)
        {
            var active = WatchedServices.Where(SafeIsRunning).ToArray();
            state = active.Length > 0;
            changed = state != IsProtectedGameRunning;
            IsProtectedGameRunning = state;
            ActiveServices = active;
        }

        if (changed)
        {
            Log.Instance.Trace($"Protected game {(state ? "started" : "stopped")}. [services={string.Join(",", ActiveServices)}]");
            ProtectedGameChanged?.Invoke(this, state);
        }

        return state;
    }

    private bool SafeIsRunning(string name)
    {
        try
        {
            return _isServiceRunning(name);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsServiceRunning(string name)
    {
        try
        {
            using var service = new ServiceController(name);
            return service.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending;
        }
        catch (InvalidOperationException)
        {
            return false; // not installed
        }
    }

    public void Dispose() => Stop();
}
