using System;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.GameProfiles;
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
/// <item>the Caps Lock / Num Lock keyboard hook (removed);</item>
/// <item>the keyboard hook and media-key listener for the extra Fn keys (removed).</item>
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
    private readonly Action<string, Exception> _reportError;
    private readonly Func<bool> _forcedByProfile;

    public GameSafeMode(AntiCheatMonitor monitor, ApplicationSettings settings, SensorsGroupController sensors, MacroController macros, NativeWindowsMessageListener nativeListener, GameProfileController gameProfiles, ExtraKeyListeners extraKeys)
        : this(monitor, () => settings.Store.GameSafeMode, active => SwitchAsync(active, sensors, macros, nativeListener, extraKeys), Log.Instance.ErrorReport, () => gameProfiles.ForceGameSafe)
    {
        gameProfiles.ForceGameSafeChanged += (_, _) => _ = ApplyAsync();
    }

    /// <summary>
    /// For tests: the setting, the actual pausing/resuming, the error report and whether a game profile forces
    /// game-safe mode are supplied by the caller.
    /// </summary>
    internal GameSafeMode(AntiCheatMonitor monitor, Func<bool> settingEnabled, Func<bool, Task> switchTo, Action<string, Exception> reportError, Func<bool>? forcedByProfile = null)
    {
        _monitor = monitor;
        _settingEnabled = settingEnabled;
        _switch = switchTo;
        _reportError = reportError;
        _forcedByProfile = forcedByProfile ?? (() => false);
        _monitor.ProtectedGameChanged += (_, _) => _ = ApplyAsync();
    }

    /// <summary>True while game-safe mode is in effect.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Raised when <see cref="IsActive"/> changes (on a background thread).</summary>
    public event EventHandler<bool>? ActiveChanged;

    /// <summary>
    /// Whether game-safe mode should be on: a game profile that asks for it wins (even with the setting off);
    /// otherwise the setting must be on and a protected game running.
    /// </summary>
    public static bool ShouldBeActive(bool settingEnabled, bool protectedGameRunning, bool forcedByProfile = false) =>
        forcedByProfile || (settingEnabled && protectedGameRunning);

    /// <summary>How often the anti-cheat services are checked while the setting is on.</summary>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);

    /// <summary>Starts the anti-cheat check if the setting is on; otherwise nothing runs.</summary>
    public void Start() => _ = ApplyAsync();

    /// <summary>Re-evaluates after the setting changes, starting or stopping the anti-cheat check with it.</summary>
    public Task RefreshAsync() => ApplyAsync();

    /// <summary>One switch at a time: the service check and the Settings toggle can both ask at once.</summary>
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    private async Task ApplyAsync()
    {
        await _oneAtATime.WaitAsync().ConfigureAwait(false);
        try
        {
            // Read inside the lock, so whoever runs last acts on the newest setting and game state.
            var settingEnabled = _settingEnabled();

            // The check only runs while the setting is on. Starting it runs the first check straight away on a
            // timer thread, which calls back here with the result.
            if (settingEnabled)
                _monitor.Start(CheckInterval);
            else
                _monitor.Stop();

            var forced = _forcedByProfile();
            var active = ShouldBeActive(settingEnabled, _monitor.IsProtectedGameRunning, forced);
            if (active == IsActive)
                return;

            // Recorded before switching, so if pausing fails part-way, the next switch still resumes everything.
            IsActive = active;
            Log.Instance.Trace($"Game-safe mode {(active ? "on" : "off")}. [services={string.Join(",", _monitor.ActiveServices)}, gameProfile={forced}]");

            try
            {
                await _switch(active).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Game-safe mode couldn't {(active ? "pause" : "resume")} everything.", ex);
                _reportError($"Game-safe mode couldn't {(active ? "pause" : "resume")} everything.", ex);
            }

            ActiveChanged?.Invoke(this, active);
        }
        catch (Exception ex)
        {
            // Nothing may escape: this runs from the service check's timer and from the Settings toggle.
            Log.Instance.Trace($"Game-safe mode update failed.", ex);
            _reportError("Game-safe mode update failed.", ex);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    /// <summary>Pauses (true) or resumes (false) everything game-safe mode covers, and says so.</summary>
    private static async Task SwitchAsync(bool active, SensorsGroupController sensors, MacroController macros, NativeWindowsMessageListener nativeListener, ExtraKeyListeners extraKeys)
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

        try
        {
            await extraKeys.SetSuspendedAsync(active).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't change the extra Fn key listeners.", ex);
        }

        MessagingCenter.Publish(new NotificationMessage(active ? NotificationType.GameSafeModeOn : NotificationType.GameSafeModeOff));
    }
}
