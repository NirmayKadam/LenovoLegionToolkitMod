using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace LenovoLegionToolkit.Lib.GameProfiles;

/// <summary>A running app LLT may ease or close (only apps the user listed; never the game).</summary>
/// <param name="ProcessId">Its id.</param>
/// <param name="StartTimeUtc">With the id, identifies the process.</param>
/// <param name="Path">Its .exe, or null when Windows doesn't say.</param>
/// <param name="SessionId">The Windows session (0 = services).</param>
/// <param name="Priority">Its priority now.</param>
/// <param name="Throttle">Its efficiency-mode setting now, or null when Windows can't report it.</param>
public sealed record AppProcess(int ProcessId, DateTime StartTimeUtc, string? Path, int SessionId, ProcessPriorityClass Priority, ThrottleState? Throttle);

/// <summary>A process's power-throttling ("efficiency mode") masks, as Windows reports and sets them.</summary>
public readonly record struct ThrottleState(uint ControlMask, uint StateMask)
{
    /// <summary>Windows' "execution speed" throttling, which is what Task Manager calls efficiency mode.</summary>
    public const uint ExecutionSpeed = 0x1;

    public bool IsEfficient => (ControlMask & ExecutionSpeed) != 0 && (StateMask & ExecutionSpeed) != 0;

    public static ThrottleState Efficient { get; } = new(ExecutionSpeed, ExecutionSpeed);
}

/// <summary>
/// The Windows calls a game profile makes. The product uses <see cref="WindowsGameProfileSystem"/>; tests supply
/// scripted answers to check the controller's decisions.
/// </summary>
internal interface IGameProfileSystem
{
    /// <summary>Ids of running processes with this file name (no ".exe"), from a process list: no process is opened.</summary>
    IReadOnlyList<int> FindByName(string exeName);

    /// <summary>
    /// Whether a process with this id is still running under the name Windows reported for it (which may be cut to
    /// 15 characters), from a process list: no process is opened.
    /// </summary>
    bool IsRunning(int processId, string reportedName);

    /// <summary>Opens a listed app's process and reads what LLT needs; null if it is gone or can't be read.</summary>
    AppProcess? ReadApp(int processId);

    /// <summary>
    /// Sets an app's priority and, when <paramref name="throttle"/> is given, its efficiency mode, only if the process
    /// with this id still started at <paramref name="startTimeUtc"/>. False when it didn't happen.
    /// </summary>
    bool SetApp(int processId, DateTime startTimeUtc, ProcessPriorityClass priority, ThrottleState? throttle);

    /// <summary>Asks the app to close, like clicking X, and waits up to <paramref name="timeout"/>. True when it exited.</summary>
    Task<bool> CloseAppAsync(int processId, DateTime startTimeUtc, TimeSpan timeout);

    /// <summary>The service's state, or null when it isn't installed.</summary>
    ServiceInfo? ReadService(string name);

    /// <summary>Stops a service and waits up to <paramref name="timeout"/>. True when it stopped.</summary>
    Task<bool> StopServiceAsync(string name, TimeSpan timeout);

    /// <summary>Starts a service and waits up to <paramref name="timeout"/>. True when it runs.</summary>
    Task<bool> StartServiceAsync(string name, TimeSpan timeout);

    /// <summary>The laptop's power mode, or null when it can't be read or the laptop has no power modes.</summary>
    Task<PowerModeState?> GetPowerModeAsync();

    /// <summary>Switches the laptop's power mode (as Fn+Q would). False when the laptop doesn't offer that mode or it failed.</summary>
    Task<bool> SetPowerModeAsync(PowerModeState mode);
}
