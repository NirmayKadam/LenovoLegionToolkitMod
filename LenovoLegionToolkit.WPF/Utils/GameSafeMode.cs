using System;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.Macro;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.WPF.Utils;

/// <summary>
/// Game-safe mode: while a game protected by a kernel anti-cheat (Vanguard, Easy Anti-Cheat, BattlEye) runs,
/// LLT stops the things such anti-cheats are known to watch:
/// <list type="bullet">
/// <item>detailed sensors through LibreHardwareMonitor and the PawnIO kernel driver (paused);</item>
/// <item>macros, which send simulated key presses, and their keyboard hook (paused);</item>
/// <item>the Caps Lock / Num Lock keyboard hook (removed).</item>
/// </list>
/// The OSD keeps working from driver-free sources (Lenovo WMI, NVIDIA's API, Windows memory status, ETW frame
/// timing). Everything resumes when the game ends. LLT can't promise any game won't flag it; this only avoids
/// what is known to be risky.
/// </summary>
public sealed class GameSafeMode
{
    private readonly AntiCheatMonitor _monitor;
    private readonly Func<bool> _settingEnabled;
    private readonly Func<bool, Task> _switch;

    public GameSafeMode(AntiCheatMonitor monitor, ApplicationSettings settings, SensorsGroupController sensors, MacroController macros, NativeWindowsMessageListener nativeListener)
        : this(monitor, () => settings.Store.GameSafeMode, active => SwitchAsync(active, sensors, macros, nativeListener)) { }

    /// <summary>For tests: the setting and the actual pausing/resuming are supplied by the caller.</summary>
    internal GameSafeMode(AntiCheatMonitor monitor, Func<bool> settingEnabled, Func<bool, Task> switchTo)
    {
        _monitor = monitor;
        _settingEnabled = settingEnabled;
        _switch = switchTo;
        _monitor.ProtectedGameChanged += (_, _) => _ = ApplyAsync();
    }

    /// <summary>True while game-safe mode is in effect.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Raised when <see cref="IsActive"/> changes (on a background thread).</summary>
    public event EventHandler<bool>? ActiveChanged;

    /// <summary>Whether game-safe mode should be on, given the setting and whether a protected game runs.</summary>
    public static bool ShouldBeActive(bool settingEnabled, bool protectedGameRunning) => settingEnabled && protectedGameRunning;

    public void Start() => _monitor.Start(TimeSpan.FromSeconds(5));

    /// <summary>Re-evaluates after the setting changes.</summary>
    public Task RefreshAsync() => ApplyAsync();

    /// <summary>One switch at a time: the service check and the Settings toggle can both ask at once.</summary>
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    private async Task ApplyAsync()
    {
        await _oneAtATime.WaitAsync().ConfigureAwait(false);
        try
        {
            // Read inside the lock, so whoever runs last acts on the newest setting and game state.
            var active = ShouldBeActive(_settingEnabled(), _monitor.IsProtectedGameRunning);
            if (active == IsActive)
                return;

            IsActive = active;
            Log.Instance.Trace($"Game-safe mode {(active ? "on" : "off")}. [services={string.Join(",", _monitor.ActiveServices)}]");

            await _switch(active).ConfigureAwait(false);

            ActiveChanged?.Invoke(this, active);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    /// <summary>Pauses (true) or resumes (false) everything game-safe mode covers, and says so.</summary>
    private static async Task SwitchAsync(bool active, SensorsGroupController sensors, MacroController macros, NativeWindowsMessageListener nativeListener)
    {
        sensors.SetPaused(active);

        if (active)
            macros.Suspend();
        else
            macros.Resume();

        try
        {
            await nativeListener.SetKeyboardHookSuspendedAsync(active).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't change the Caps/Num Lock hook.", ex);
        }

        MessagingCenter.Publish(new NotificationMessage(active ? NotificationType.GameSafeModeOn : NotificationType.GameSafeModeOff));
    }
}
