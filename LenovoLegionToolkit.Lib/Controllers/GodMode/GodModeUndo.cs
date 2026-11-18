using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Controllers.GodMode;

/// <summary>
/// Puts the God Mode (Custom mode) settings back to what they were before a Save. Nothing reads values back from
/// the firmware, so "back" means: the earlier saved presets, sent to the firmware again when Custom mode is in
/// use, which is what the firmware had before the Save.
/// </summary>
public sealed class GodModeUndo
{
    private readonly GodModeSettings _settings;
    private readonly GodModeSettings.GodModeSettingsStore _before;
    private readonly PowerModeState? _switchedFrom;
    private readonly Func<Task<PowerModeState>> _currentMode;
    private readonly Func<PowerModeState, Task> _setMode;
    private readonly Func<Task> _apply;

    /// <summary>For tests: the power mode and the firmware apply are supplied by the caller.</summary>
    internal GodModeUndo(
        GodModeSettings settings,
        PowerModeState? switchedFrom,
        Func<Task<PowerModeState>> currentMode,
        Func<PowerModeState, Task> setMode,
        Func<Task> apply)
    {
        _settings = settings;
        _before = settings.CloneStore();
        _switchedFrom = switchedFrom;
        _currentMode = currentMode;
        _setMode = setMode;
        _apply = apply;
    }

    /// <summary>
    /// Takes a copy of the saved God Mode settings. Call it before saving. <paramref name="switchedFrom"/> is the
    /// mode the laptop was in when this Save is about to switch it to Custom (null when it doesn't switch).
    /// </summary>
    public static GodModeUndo Capture(GodModeSettings settings, PowerModeState? switchedFrom, PowerModeFeature powerModeFeature, IGodModeController controller) =>
        new(settings, switchedFrom, powerModeFeature.GetStateAsync, powerModeFeature.SetStateAsync, controller.ApplyStateAsync);

    /// <summary>
    /// Puts the earlier settings back (saved at once). Then, only if the laptop is still in Custom mode: when the
    /// Save had switched it to Custom, it goes back to the earlier mode; otherwise the earlier preset is sent to the
    /// firmware again. A mode change made since (Fn+Q) is left alone. True when all of that worked; false when the
    /// settings are back but the power mode couldn't be read.
    /// </summary>
    public async Task<bool> RestoreAsync()
    {
        _settings.RestoreStore(_before);

        PowerModeState now;
        try
        {
            now = await _currentMode().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"God Mode undo: couldn't read the power mode.", ex);
            return false;
        }

        if (now != PowerModeState.GodMode)
            return true;   // the earlier preset is used the next time Custom mode is chosen

        if (_switchedFrom is { } earlier && earlier != PowerModeState.GodMode)
        {
            await _setMode(earlier).ConfigureAwait(false);
            return true;
        }

        await _apply().ConfigureAwait(false);
        return true;
    }
}
