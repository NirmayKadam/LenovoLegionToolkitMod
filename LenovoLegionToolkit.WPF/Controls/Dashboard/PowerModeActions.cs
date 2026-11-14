using System;
using System.Threading.Tasks;
using System.Windows;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using LenovoLegionToolkit.WPF.Windows.Dashboard;

namespace LenovoLegionToolkit.WPF.Controls.Dashboard;

/// <summary>
/// Power-mode behavior shared by the power-mode card and the dashboard's power-mode selector, so both
/// warn, fail and open settings the same way.
/// </summary>
public static class PowerModeActions
{
    /// <summary>
    /// High-power modes on battery (or a weak adapter) get a warning, as they drain or throttle.
    /// </summary>
    public static bool ShowsAdapterWarning(PowerModeState state, PowerAdapterStatus adapterStatus) =>
        adapterStatus != PowerAdapterStatus.Connected
        && state is PowerModeState.Performance or PowerModeState.GodMode or PowerModeState.Extreme;

    /// <summary>
    /// Balance (AI mode) and Custom (God Mode) have a settings window, when the machine supports them and
    /// an adapter is attached.
    /// </summary>
    public static bool HasSettings(PowerModeState state, bool supportsAIMode, bool supportsGodMode, PowerAdapterStatus adapterStatus) =>
        adapterStatus != PowerAdapterStatus.Disconnected
        && state switch
        {
            PowerModeState.Balance => supportsAIMode,
            PowerModeState.GodMode => supportsGodMode,
            _ => false
        };

    /// <summary>
    /// Tells the user why a mode change failed, when there is something useful to say.
    /// </summary>
    public static void ShowSetStateError(Exception exception)
    {
        if (exception is PowerModeUnavailableWithoutACException ex)
        {
            SnackbarHelper.Show(Resource.PowerModeUnavailableWithoutACException_Title,
                string.Format(Resource.PowerModeUnavailableWithoutACException_Message, ex.PowerMode.GetDisplayName()),
                SnackbarType.Warning);
        }
    }

    /// <summary>
    /// Opens the settings window for a mode. Custom mode first shows the one-time warning about its limits.
    /// </summary>
    public static async Task OpenSettingsAsync(PowerModeState state, FrameworkElement owner)
    {
        switch (state)
        {
            case PowerModeState.Balance:
            {
                var window = new BalanceModeSettingsWindow { Owner = Window.GetWindow(owner) };
                window.ShowDialog();
                break;
            }
            case PowerModeState.GodMode:
            {
                if (!await CheckCustomModeWarningAsync(owner))
                    return;

                var window = new GodModeSettingsWindow { Owner = Window.GetWindow(owner) };
                window.ShowDialog();
                break;
            }
            default:
                throw new InvalidOperationException($"Access to Settings in {state} is denied.");
        }
    }

    private static async Task<bool> CheckCustomModeWarningAsync(FrameworkElement owner)
    {
        var settings = IoCContainer.Resolve<ApplicationSettings>();
        if (settings.Store.CustomModeWarningDontShowAgain)
            return true;

        try
        {
            var result = await await Application.Current.Dispatcher.InvokeAsync(() => MessageBoxHelper.ShowAsync(
                owner,
                Resource.Warning,
                Resource.GodModeSettingsWindow_CustomMode_Warning,
                true,
                Resource.Yes,
                Resource.No));

            if (result is { DontShowAgain: true, Result: true })
            {
                settings.Store.CustomModeWarningDontShowAgain = true;
                settings.SynchronizeStore();
                return true;
            }

            return result.Result;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to show the custom mode warning.", ex);
        }

        return false;
    }
}
