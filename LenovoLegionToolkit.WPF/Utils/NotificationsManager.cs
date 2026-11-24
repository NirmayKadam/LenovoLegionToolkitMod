using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Windows;
using LenovoLegionToolkit.WPF.Windows.Utils;
using Wpf.Ui.Common;
using Wpf.Ui.Controls;

namespace LenovoLegionToolkit.WPF.Utils;

public class NotificationsManager
{
    private static Dispatcher Dispatcher => Application.Current.Dispatcher;

    private readonly NotificationSettings _settings;

    private List<INotificationWindow?> _windows = [];

    public NotificationsManager(NotificationSettings settings)
    {
        _settings = settings;

        MessagingCenter.Subscribe<NotificationMessage>(this, OnNotificationReceived);
    }

    /// <summary>Whether the user's settings allow this notification. Every type needs a case here (a missing one, like Extreme mode's, threw and the notification was lost).</summary>
    internal static bool IsAllowed(NotificationType type, NotificationSettings.NotificationSettingsStore store) => type switch
    {
        NotificationType.ACAdapterConnected => store.Notifications.ACAdapter,
        NotificationType.ACAdapterConnectedLowWattage => store.Notifications.ACAdapter,
        NotificationType.ACAdapterDisconnected => store.Notifications.ACAdapter,
        NotificationType.AirplaneModeOn => store.Notifications.AirplaneMode,
        NotificationType.AirplaneModeOff => store.Notifications.AirplaneMode,
        NotificationType.AutomationNotification => store.Notifications.AutomationNotification,
        NotificationType.CapsLockOn => store.Notifications.CapsLock,
        NotificationType.CapsLockOff => store.Notifications.CapsLock,
        NotificationType.CameraOn => store.Notifications.CameraLock,
        NotificationType.CameraOff => store.Notifications.CameraLock,
        NotificationType.FnLockOn => store.Notifications.FnLock,
        NotificationType.FnLockOff => store.Notifications.FnLock,
        NotificationType.MicrophoneOn => store.Notifications.Microphone,
        NotificationType.MicrophoneOff => store.Notifications.Microphone,
        NotificationType.NumLockOn => store.Notifications.NumLock,
        NotificationType.NumLockOff => store.Notifications.NumLock,
        NotificationType.PanelLogoLightingOn => store.Notifications.KeyboardBacklight,
        NotificationType.PanelLogoLightingOff => store.Notifications.KeyboardBacklight,
        NotificationType.PortLightingOn => store.Notifications.KeyboardBacklight,
        NotificationType.PortLightingOff => store.Notifications.KeyboardBacklight,
        NotificationType.PowerModeQuiet => store.Notifications.PowerMode,
        NotificationType.PowerModeBalance => store.Notifications.PowerMode,
        NotificationType.PowerModePerformance => store.Notifications.PowerMode,
        NotificationType.PowerModeExtreme => store.Notifications.PowerMode,
        NotificationType.PowerModeGodMode => store.Notifications.PowerMode,
        NotificationType.RefreshRate => store.Notifications.RefreshRate,
        NotificationType.RGBKeyboardBacklightOff => store.Notifications.KeyboardBacklight,
        NotificationType.RGBKeyboardBacklightChanged => store.Notifications.KeyboardBacklight,
        NotificationType.SmartKeyDoublePress => store.Notifications.SmartKey,
        NotificationType.SmartKeySinglePress => store.Notifications.SmartKey,
        NotificationType.SpectrumBacklightChanged => store.Notifications.KeyboardBacklight,
        NotificationType.SpectrumBacklightOff => store.Notifications.KeyboardBacklight,
        NotificationType.SpectrumBacklightPresetChanged => store.Notifications.KeyboardBacklight,
        NotificationType.TouchpadOn => store.Notifications.TouchpadLock,
        NotificationType.TouchpadOff => store.Notifications.TouchpadLock,
        NotificationType.UpdateAvailable => store.Notifications.UpdateAvailable,
        NotificationType.WhiteKeyboardBacklightOff => store.Notifications.KeyboardBacklight,
        NotificationType.WhiteKeyboardBacklightChanged => store.Notifications.KeyboardBacklight,
        NotificationType.WhiteKeyboardBacklightChangedSpecial => store.Notifications.KeyboardBacklight,
        NotificationType.ITSModeAuto => store.Notifications.ITSMode,
        NotificationType.ITSModeCool => store.Notifications.ITSMode,
        NotificationType.ITSModePerformance => store.Notifications.ITSMode,
        NotificationType.ITSModeGeek => store.Notifications.ITSMode,
        NotificationType.OsdShown => store.Notifications.Osd,
        NotificationType.OsdHidden => store.Notifications.Osd,
        NotificationType.GameSafeModeOn => store.Notifications.GameSafeMode,
        NotificationType.GameSafeModeOff => store.Notifications.GameSafeMode,
        NotificationType.GameProfileOn => store.Notifications.GameProfile,
        NotificationType.GameProfileOff => store.Notifications.GameProfile,
        NotificationType.GpuDriverError => true,
        NotificationType.HealthAttention => true,
        NotificationType.SafeStart => true,
        // Turned on and off with the report itself (Settings → App behaviour → Game session report).
        NotificationType.GameSessionReport => true,
        NotificationType.UiFreeze => true,
        _ => throw new ArgumentException($"No mapping for notification type {type}.", nameof(type))
    };

    /// <summary>The notification's text. Every type needs a case here.</summary>
    internal static string Text(NotificationType type, object? args) => type switch
    {
        NotificationType.ACAdapterConnected => Resource.Notification_ACAdapterConnected,
        NotificationType.ACAdapterConnectedLowWattage => Resource.Notification_ACAdapterConnectedLowWattage,
        NotificationType.ACAdapterDisconnected => Resource.Notification_ACAdapterDisconnected,
        NotificationType.AirplaneModeOn => Resource.Notification_AirplaneModeOn,
        NotificationType.AirplaneModeOff => Resource.Notification_AirplaneModeOff,
        NotificationType.AutomationNotification => string.Format("{0}", args),
        NotificationType.CapsLockOn => Resource.Notification_CapsLockOn,
        NotificationType.CapsLockOff => Resource.Notification_CapsLockOff,
        NotificationType.CameraOn => Resource.Notification_CameraOn,
        NotificationType.CameraOff => Resource.Notification_CameraOff,
        NotificationType.FnLockOn => Resource.Notification_FnLockOn,
        NotificationType.FnLockOff => Resource.Notification_FnLockOff,
        NotificationType.MicrophoneOn => Resource.Notification_MicrophoneOn,
        NotificationType.MicrophoneOff => Resource.Notification_MicrophoneOff,
        NotificationType.NumLockOn => Resource.Notification_NumLockOn,
        NotificationType.NumLockOff => Resource.Notification_NumLockOff,
        NotificationType.PanelLogoLightingOn => Resource.Notification_PanelLogoLightingOn,
        NotificationType.PanelLogoLightingOff => Resource.Notification_PanelLogoLightingOff,
        NotificationType.PortLightingOn => Resource.Notification_PortLightingOn,
        NotificationType.PortLightingOff => Resource.Notification_PortLightingOff,
        NotificationType.PowerModeQuiet => string.Format("{0}", args),
        NotificationType.PowerModeBalance => string.Format("{0}", args),
        NotificationType.PowerModePerformance => string.Format("{0}", args),
        NotificationType.PowerModeExtreme => string.Format("{0}", args),
        NotificationType.PowerModeGodMode => string.Format("{0}", args),
        NotificationType.RefreshRate => string.Format("{0}", args),
        NotificationType.RGBKeyboardBacklightOff => string.Format("{0}", args),
        NotificationType.RGBKeyboardBacklightChanged => string.Format("{0}", args),
        NotificationType.SmartKeyDoublePress => string.Format("{0}", args),
        NotificationType.SmartKeySinglePress => string.Format("{0}", args),
        NotificationType.SpectrumBacklightChanged => string.Format(Resource.Notification_SpectrumKeyboardBacklight_Brightness, args),
        NotificationType.SpectrumBacklightOff => string.Format(Resource.Notification_SpectrumKeyboardBacklight_Backlight, args),
        NotificationType.SpectrumBacklightPresetChanged => string.Format(Resource.Notification_SpectrumKeyboardBacklight_Profile, args),
        NotificationType.TouchpadOn => Resource.Notification_TouchpadOn,
        NotificationType.TouchpadOff => Resource.Notification_TouchpadOff,
        NotificationType.UpdateAvailable => string.Format(Resource.Notification_UpdateAvailable, args),
        NotificationType.WhiteKeyboardBacklightOff => string.Format(Resource.Notification_WhiteKeyboardBacklight, args),
        NotificationType.WhiteKeyboardBacklightChanged => string.Format(Resource.Notification_WhiteKeyboardBacklight, args),
        NotificationType.WhiteKeyboardBacklightChangedSpecial => Resource.Notification_WhiteKeyboardBacklightSpecial,
        NotificationType.ITSModeAuto => string.Format("{0}", args),
        NotificationType.ITSModeCool => string.Format("{0}", args),
        NotificationType.ITSModePerformance => string.Format("{0}", args),
        NotificationType.ITSModeGeek => string.Format("{0}", args),
        NotificationType.OsdShown => Resource.Notification_OsdShown,
        NotificationType.OsdHidden => Resource.Notification_OsdHidden,
        NotificationType.GameSafeModeOn => Resource.Notification_GameSafeModeOn,
        NotificationType.GameSafeModeOff => Resource.Notification_GameSafeModeOff,
        NotificationType.GameProfileOn => string.Format("{0}", args),
        NotificationType.GameProfileOff => string.Format("{0}", args),
        NotificationType.GpuDriverError => string.Format("{0}", args),
        NotificationType.HealthAttention => string.Format(Resource.Health_Notification, args),
        NotificationType.SafeStart => string.Format("{0}", args),
        NotificationType.GameSessionReport => string.Format("{0}", args),
        NotificationType.UiFreeze => string.Format("{0}", args),
        _ => throw new ArgumentException($"No mapping for notification type {type}.", nameof(type))
    };

    private void OnNotificationReceived(NotificationMessage notification)
    {
        Dispatcher.Post(() =>
        {
            Log.Instance.Trace($"Notification {notification} received");

            if (_settings.Store.DontShowNotifications)
            {
                Log.Instance.Trace($"Notifications are disabled.");

                return;
            }

            if (FullscreenHelper.IsAnyApplicationFullscreen() && !_settings.Store.NotificationAlwaysOnTop)
            {
                Log.Instance.Trace($"Some application is in fullscreen.");

                return;
            }

            var allow = IsAllowed(notification.Type, _settings.Store);

            if (!allow)
            {
                Log.Instance.Trace($"Notification type {notification.Type} is disabled.");

                return;
            }

            var symbol = GetDefaultSymbol(notification.Type);

            SymbolRegular? overlaySymbol = GetDefaultOverlaySymbol(notification.Type);

            var text = Text(notification.Type, notification.Args);

            Action<SymbolIcon>? symbolTransform = notification.Type switch
            {
                NotificationType.PowerModeQuiet => si => si.Foreground = PowerModeState.Quiet.GetSolidColorBrush(),
                NotificationType.PowerModePerformance => si => si.Foreground = PowerModeState.Performance.GetSolidColorBrush(),
                NotificationType.PowerModeExtreme => si => si.Foreground = PowerModeState.Extreme.GetSolidColorBrush(),
                NotificationType.PowerModeGodMode => si => si.Foreground = PowerModeState.GodMode.GetSolidColorBrush(),
                NotificationType.ITSModeAuto => si => si.Foreground = ITSMode.ItsAuto.GetSolidColorBrush(),
                NotificationType.ITSModeCool => si => si.Foreground = ITSMode.MmcCool.GetSolidColorBrush(),
                NotificationType.ITSModePerformance => si => si.Foreground = ITSMode.MmcPerformance.GetSolidColorBrush(),
                NotificationType.ITSModeGeek => si => si.Foreground = ITSMode.MmcGeek.GetSolidColorBrush(),
                _ => null
            };

            Action? clickAction = notification.Type switch
            {
                NotificationType.UpdateAvailable => UpdateAvailableAction,
                _ => null
            };

            if (notification.Overrides?.IconOverride != null && Enum.IsDefined(typeof(SymbolRegular), notification.Overrides.IconOverride.Value))
                symbol = (SymbolRegular)notification.Overrides.IconOverride.Value;
            else if (_settings.Store.Notifications.IconOverrides.TryGetValue(notification.Type, out var iconOverride)
                && Enum.IsDefined(typeof(SymbolRegular), iconOverride))
                symbol = (SymbolRegular)iconOverride;

            if (notification.Overrides?.ColorOverride != null)
                symbolTransform = si => si.Foreground = new SolidColorBrush(notification.Overrides.ColorOverride.Value.ToColor());
            else if (_settings.Store.Notifications.ColorOverrides.TryGetValue(notification.Type, out var colorOverride))
                symbolTransform = si => si.Foreground = new SolidColorBrush(colorOverride.ToColor());

            if (symbolTransform is null && overlaySymbol is not null)
                symbolTransform = si => si.SetResourceReference(Control.ForegroundProperty, "TextFillColorSecondaryBrush");

            Brush? textColor = null;
            if (notification.Overrides?.TextColorOverride != null)
                textColor = new SolidColorBrush(notification.Overrides.TextColorOverride.Value.ToColor());
            else if (_settings.Store.Notifications.TextColorOverrides.TryGetValue(notification.Type, out var textColorOverride))
                textColor = new SolidColorBrush(textColorOverride.ToColor());

            var effectiveDuration = notification.Overrides?.DurationOverride 
                ?? (_settings.Store.Notifications.DurationOverrides.TryGetValue(notification.Type, out var durOverride)
                    ? durOverride
                    : _settings.Store.NotificationDuration);

            var duration = effectiveDuration switch
            {
                NotificationDuration.Short  => notification.Type == NotificationType.RefreshRate ? 2000 : 500,
                NotificationDuration.Normal => notification.Type == NotificationType.RefreshRate ? 3500 : 1000,
                NotificationDuration.Long   => notification.Type == NotificationType.RefreshRate ? 5000 : 2500,
                _ => throw new ArgumentException(nameof(effectiveDuration))
            };

            var effectivePosition = notification.Overrides?.PositionOverride
                ?? (_settings.Store.Notifications.PositionOverrides.TryGetValue(notification.Type, out var posOverride)
                    ? posOverride
                    : _settings.Store.NotificationPosition);

            ShowNotification(duration, symbol, overlaySymbol, symbolTransform, text, textColor, clickAction, effectivePosition);

            Log.Instance.Trace($"Notification {notification} shown.");
        });
    }

    private void ShowNotification(int duration, SymbolRegular symbol, SymbolRegular? overlaySymbol, Action<SymbolIcon>? symbolTransform, string text, Brush? textColor, Action? clickAction, NotificationPosition position)
    {
        if (App.Current.MainWindow is not MainWindow mainWindow)
            return;

        if (_windows.Count != 0)
        {
            foreach (var window in _windows)
                window?.Close(true);

            _windows.Clear();
        }

        ScreenHelper.UpdateScreenInfos();

        if (_settings.Store.NotificationOnAllScreens)
        {
            foreach (var screen in ScreenHelper.Screens)
            {
                ShowOnScreen(screen, duration, symbol, overlaySymbol, symbolTransform, text, textColor, clickAction, position);
            }
        }
        else
        {
            var primaryScreen = ScreenHelper.PrimaryScreen;
            if (primaryScreen.HasValue)
            {
                ShowOnScreen(primaryScreen.Value, duration, symbol, overlaySymbol, symbolTransform, text, textColor, clickAction, position);
            }
        }
    }

    private void ShowOnScreen(ScreenInfo screen, int duration, SymbolRegular symbol, SymbolRegular? overlaySymbol, Action<SymbolIcon>? symbolTransform, string text, Brush? textColor, Action? clickAction, NotificationPosition position)
    {
        var nw = new NotificationWindow(symbol, overlaySymbol, symbolTransform, text, textColor, clickAction, screen, position);
        if (_settings.Store.NotificationAlwaysOnTop)
        {
            nw.SourceInitialized += (_, _) => nw.EscalateZBand();
        }

        nw.Show(duration);
        _windows.Add(nw);
    }

    internal static SymbolRegular GetDefaultSymbol(NotificationType type) => type switch
    {
        NotificationType.ACAdapterConnected => SymbolRegular.BatteryCharge24,
        NotificationType.ACAdapterConnectedLowWattage => SymbolRegular.BatteryCharge24,
        NotificationType.ACAdapterDisconnected => SymbolRegular.BatteryCharge24,
        NotificationType.AirplaneModeOn => SymbolRegular.Airplane24,
        NotificationType.AirplaneModeOff => SymbolRegular.Airplane24,
        NotificationType.AutomationNotification => SymbolRegular.Rocket24,
        NotificationType.CapsLockOn => SymbolRegular.KeyboardShiftUppercase24,
        NotificationType.CapsLockOff => SymbolRegular.KeyboardShiftUppercase24,
        NotificationType.CameraOn => SymbolRegular.Camera24,
        NotificationType.CameraOff => SymbolRegular.Camera24,
        NotificationType.FnLockOn => SymbolRegular.Keyboard24,
        NotificationType.FnLockOff => SymbolRegular.Keyboard24,
        NotificationType.MicrophoneOn => SymbolRegular.Mic24,
        NotificationType.MicrophoneOff => SymbolRegular.Mic24,
        NotificationType.NumLockOn => SymbolRegular.Keyboard12324,
        NotificationType.NumLockOff => SymbolRegular.Keyboard12324,
        NotificationType.PanelLogoLightingOn => SymbolRegular.LightbulbCircle24,
        NotificationType.PanelLogoLightingOff => SymbolRegular.LightbulbCircle24,
        NotificationType.PortLightingOn => SymbolRegular.UsbPlug24,
        NotificationType.PortLightingOff => SymbolRegular.UsbPlug24,
        NotificationType.PowerModeQuiet => SymbolRegular.Gauge24,
        NotificationType.PowerModeBalance => SymbolRegular.Gauge24,
        NotificationType.PowerModePerformance => SymbolRegular.Gauge24,
        NotificationType.PowerModeExtreme => SymbolRegular.Gauge24,
        NotificationType.PowerModeGodMode => SymbolRegular.Gauge24,
        NotificationType.RefreshRate => SymbolRegular.DesktopPulse24,
        NotificationType.RGBKeyboardBacklightOff => SymbolRegular.Lightbulb24,
        NotificationType.RGBKeyboardBacklightChanged => SymbolRegular.Lightbulb24,
        NotificationType.SmartKeyDoublePress => SymbolRegular.StarEmphasis24,
        NotificationType.SmartKeySinglePress => SymbolRegular.Star24,
        NotificationType.SpectrumBacklightChanged => SymbolRegular.Lightbulb24,
        NotificationType.SpectrumBacklightOff => SymbolRegular.Lightbulb24,
        NotificationType.SpectrumBacklightPresetChanged => SymbolRegular.Lightbulb24,
        NotificationType.TouchpadOn => SymbolRegular.Tablet24,
        NotificationType.TouchpadOff => SymbolRegular.Tablet24,
        NotificationType.UpdateAvailable => SymbolRegular.ArrowSync24,
        NotificationType.WhiteKeyboardBacklightOff => SymbolRegular.Lightbulb24,
        NotificationType.WhiteKeyboardBacklightChanged => SymbolRegular.Lightbulb24,
        NotificationType.WhiteKeyboardBacklightChangedSpecial => SymbolRegular.Lightbulb24,
        NotificationType.ITSModeAuto => SymbolRegular.Gauge24,
        NotificationType.ITSModeCool => SymbolRegular.Gauge24,
        NotificationType.ITSModePerformance => SymbolRegular.Gauge24,
        NotificationType.ITSModeGeek => SymbolRegular.Gauge24,
        NotificationType.OsdShown => SymbolRegular.Eye24,
        NotificationType.OsdHidden => SymbolRegular.EyeOff24,
        NotificationType.GameSafeModeOn => SymbolRegular.ShieldCheckmark24,
        NotificationType.GameSafeModeOff => SymbolRegular.Shield24,
        NotificationType.GameProfileOn => SymbolRegular.Games24,
        NotificationType.GameProfileOff => SymbolRegular.Games24,
        NotificationType.GpuDriverError => SymbolRegular.ErrorCircle24,
        NotificationType.HealthAttention => SymbolRegular.HeartPulse24,
        NotificationType.SafeStart => SymbolRegular.ShieldError24,
        NotificationType.GameSessionReport => SymbolRegular.DataHistogram24,
        NotificationType.UiFreeze => SymbolRegular.Warning24,
        _ => SymbolRegular.Question24,
    };

    internal static SymbolRegular? GetDefaultOverlaySymbol(NotificationType type) => type switch
    {
        NotificationType.ACAdapterDisconnected => SymbolRegular.Line24,
        NotificationType.AirplaneModeOff => SymbolRegular.Line24,
        NotificationType.CapsLockOff => SymbolRegular.Line24,
        NotificationType.CameraOff => SymbolRegular.Line24,
        NotificationType.FnLockOff => SymbolRegular.Line24,
        NotificationType.MicrophoneOff => SymbolRegular.Line24,
        NotificationType.NumLockOff => SymbolRegular.Line24,
        NotificationType.PanelLogoLightingOff => SymbolRegular.Line24,
        NotificationType.PortLightingOff => SymbolRegular.Line24,
        NotificationType.RGBKeyboardBacklightOff => SymbolRegular.Line24,
        NotificationType.SpectrumBacklightOff => SymbolRegular.Line24,
        NotificationType.TouchpadOff => SymbolRegular.Line24,
        NotificationType.WhiteKeyboardBacklightOff => SymbolRegular.Line24,
        NotificationType.GameProfileOff => SymbolRegular.Line24,
        _ => null,
    };

    private static void UpdateAvailableAction()
    {
        if (App.Current.MainWindow is not MainWindow mainWindow)
            return;

        mainWindow.BringToForeground();
        mainWindow.ShowUpdateWindow();
    }
}
