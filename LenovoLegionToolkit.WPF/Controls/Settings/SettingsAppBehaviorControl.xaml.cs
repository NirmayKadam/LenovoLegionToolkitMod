using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Controls.Help;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Settings;
using LenovoLegionToolkit.WPF.Utils;
using LenovoLegionToolkit.WPF.Windows;
using LenovoLegionToolkit.WPF.Windows.Dashboard;
using LenovoLegionToolkit.WPF.Windows.Osd;
using LenovoLegionToolkit.WPF.Windows.Settings;
using LenovoLegionToolkit.WPF.Windows.Utils;
using OsdSettingsAlias = LenovoLegionToolkit.WPF.Windows.Osd.OsdSettingsWindow;

namespace LenovoLegionToolkit.WPF.Controls.Settings;

public partial class SettingsAppBehaviorControl
{
    private readonly ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly SunriseSunsetSettings _sunriseSunsetSettings = IoCContainer.Resolve<SunriseSunsetSettings>();
    private readonly DashboardSettings _dashboardSettings = IoCContainer.Resolve<DashboardSettings>();
    private readonly OsdSettings _OsdSettings = IoCContainer.Resolve<OsdSettings>();
    private readonly SensorsControlSettings _sensorsControlSettings = IoCContainer.Resolve<SensorsControlSettings>();
    private readonly HardwareSensorSettings _hardwareSensorSettings = IoCContainer.Resolve<HardwareSensorSettings>();

    private bool _isRefreshing = true;

    public SettingsAppBehaviorControl()
    {
        InitializeComponent();
    }

    public async Task RefreshAsync()
    {
        _isRefreshing = true;

        _autorunComboBox.SetItems(Enum.GetValues<AutorunState>(), Autorun.State, t => t.GetDisplayName());
        _minimizeToTrayToggle.IsChecked = _settings.Store.MinimizeToTray;
        _helpTooltipsToggle.IsChecked = _settings.Store.ShowHelpTooltips;
        _detailedLogToggle.IsChecked = Log.Instance.IsTraceEnabled;
        _sunLocationTextBox.Text = _sunriseSunsetSettings.Store is { Latitude: { } lat, Longitude: { } lon }
            ? string.Create(CultureInfo.InvariantCulture, $"{lat}, {lon}")
            : string.Empty;
        _helpTooltipDelayComboBox.SelectedIndex = _settings.Store.HelpTooltipDelayMs switch { <= 200 => 0, <= 600 => 1, _ => 2 };
        _minimizeOnCloseToggle.IsChecked = _settings.Store.MinimizeOnClose;
        _useNewSensorDashboardToggle.IsChecked = _settings.Store.UseNewSensorDashboard;
        _lockWindowSizeToggle.IsChecked = _settings.Store.LockWindowSize;
        _rememberWindowPositionToggle.IsChecked = _settings.Store.RememberWindowPosition;
        _alwaysOnTopToggle.IsChecked = _settings.Store.AlwaysOnTop;
        _compactModeToggle.IsChecked = _settings.Store.CompactMode;
        _enableLoggingToggle.IsChecked = _settings.Store.EnableLogging;

        _osdToggle.IsChecked = _OsdSettings.Store.ShowOsd;

        _autorunComboBox.Visibility = Visibility.Visible;
        _minimizeToTrayToggle.Visibility = Visibility.Visible;
        _minimizeOnCloseToggle.Visibility = Visibility.Visible;
        _enableLoggingToggle.Visibility = Visibility.Visible;
        _useNewSensorDashboardToggle.Visibility = Visibility.Visible;
        _hardwareSensorsToggle.Visibility = Visibility.Visible;
        _lockWindowSizeToggle.Visibility = Visibility.Visible;
        _rememberWindowPositionToggle.Visibility = Visibility.Visible;
        _alwaysOnTopToggle.Visibility = Visibility.Visible;
        _compactModeToggle.Visibility = Visibility.Visible;
        _osdToggle.Visibility = Visibility.Visible;

        _hardwareSensorsToggle.IsChecked = _settings.Store.EnableHardwareSensors;

        _useNewSensorDashboardCardControl.Visibility = _settings.Store.EnableHardwareSensors ? Visibility.Visible : Visibility.Collapsed;

        // The OSD works without hardware sensors too: it then shows driver-free readings only.
        _osdCardControl.Visibility = Visibility.Visible;

        var pawnIOState = PawnIOHelper.GetPawnIOState();

        if (pawnIOState is PawnIOState.UpdateRequired or PawnIOState.ServiceNotRunning)
        {
            await PawnIOHelper.TryShowPawnIODialogAsync(pawnIOState, true);
        }
        else
        {
            _hardwareSensorsCardHeader.Warning = pawnIOState == PawnIOState.Installed ? string.Empty : Resource.SettingsPage_HardwareSensors_PawnIOWarning;
        }

        _isRefreshing = false;
    }

    private void AutorunComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing || !IsLoaded)
            return;

        if (!_autorunComboBox.TryGetSelectedItem(out AutorunState state))
            return;

        try
        {
            Autorun.Set(state);
        }
        catch (UnprotectedAutorunLocationException ex)
        {
            Log.Instance.Trace($"Autorun refused for unprotected location.", ex);

            _isRefreshing = true;
            _autorunComboBox.SetItems(Enum.GetValues<AutorunState>(), Autorun.State, t => t.GetDisplayName());
            _isRefreshing = false;

            _ = SnackbarHelper.ShowAsync(Resource.SettingsPage_Autorun_Unprotected_Title, Resource.SettingsPage_Autorun_Unprotected_Message, SnackbarType.Warning);
        }
    }

    private void SafetyReportCard_Click(object sender, RoutedEventArgs e) =>
        new SafetyReportWindow { Owner = Window.GetWindow(this) }.ShowDialog();

    private void TourCard_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } owner)
            TourWindow.Open(owner);
    }

    /// <summary>Empty clears the location (back to the lookup by IP address); anything else must be a valid "latitude, longitude".</summary>
    private async void SunLocationSaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = _sunLocationTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                _sunriseSunsetSettings.Store.Latitude = null;
                _sunriseSunsetSettings.Store.Longitude = null;
            }
            else if (SunriseSunset.TryParseLocation(text, out var latitude, out var longitude))
            {
                _sunriseSunsetSettings.Store.Latitude = latitude;
                _sunriseSunsetSettings.Store.Longitude = longitude;
            }
            else
            {
                await SnackbarHelper.ShowAsync(Resource.SettingsPage_SunLocation_Invalid_Title, Resource.SettingsPage_SunLocation_Invalid_Message, SnackbarType.Warning);
                return;
            }

            // Worked out again from the new location next time it's needed.
            _sunriseSunsetSettings.Store.LastCheckDateTime = null;
            _sunriseSunsetSettings.SynchronizeStore();
            await SnackbarHelper.ShowAsync(Resource.SettingsPage_SunLocation_Saved, type: SnackbarType.Success);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't save the location for sunrise and sunset.", ex);
        }
    }

    private void DetailedLogToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing || !IsLoaded)
            return;

        _settings.Store.DetailedLog = _detailedLogToggle.IsChecked == true;
        _settings.SynchronizeStore();

        // Takes effect at once; the --trace start flag keeps it on for this run.
        Log.Instance.IsTraceEnabled = _settings.Store.DetailedLog || AppFlags.Instance.IsTraceEnabled;
        _detailedLogToggle.IsChecked = Log.Instance.IsTraceEnabled;
    }

    private void OpenLogFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = Path.GetDirectoryName(Log.Instance.LogPath);
            if (folder is not null && Directory.Exists(folder))
                Process.Start("explorer.exe", $"\"{folder}\"");
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't open the log folder.", ex);
        }
    }

    private void HelpTooltipsToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing || !IsLoaded)
            return;

        _settings.Store.ShowHelpTooltips = _helpTooltipsToggle.IsChecked == true;
        _settings.SynchronizeStore();
        HelpPresenter.Configure(_settings.Store.ShowHelpTooltips, _settings.Store.HelpTooltipDelayMs);
    }

    private void HelpTooltipDelayComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing || !IsLoaded)
            return;

        if (_helpTooltipDelayComboBox.SelectedItem is not ComboBoxItem { Tag: string tag } || !int.TryParse(tag, out var delay))
            return;

        _settings.Store.HelpTooltipDelayMs = delay;
        _settings.SynchronizeStore();
        HelpPresenter.Configure(_settings.Store.ShowHelpTooltips, _settings.Store.HelpTooltipDelayMs);
    }

    private void MinimizeToTrayToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing || !IsLoaded)
            return;

        var state = _minimizeToTrayToggle.IsChecked;
        if (state is null)
            return;

        _settings.Store.MinimizeToTray = state.Value;
        _settings.SynchronizeStore();
    }

    private void MinimizeOnCloseToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing || !IsLoaded)
            return;

        var state = _minimizeOnCloseToggle.IsChecked;
        if (state is null)
            return;

        _settings.Store.MinimizeOnClose = state.Value;
        _settings.SynchronizeStore();
    }

    private void LockWindowSizeToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing || !IsLoaded)
            return;

        var state = _lockWindowSizeToggle.IsChecked;
        if (state is null)
            return;

        _settings.Store.LockWindowSize = state.Value;
        _settings.SynchronizeStore();

        App.MainWindowInstance?.ApplyWindowLock(state.Value);
    }

    private void RememberWindowPositionToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing || !IsLoaded)
            return;

        var state = _rememberWindowPositionToggle.IsChecked;
        if (state is null)
            return;

        _settings.Store.RememberWindowPosition = state.Value;
        if (!state.Value)
            _settings.Store.WindowPosition = null;

        _settings.SynchronizeStore();
    }

    private void CompactModeToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing || !IsLoaded)
            return;

        var state = _compactModeToggle.IsChecked;
        if (state is null)
            return;

        _settings.Store.CompactMode = state.Value;
        _settings.SynchronizeStore();

        SnackbarHelper.Show(Resource.SettingsPage_CompactMode_Title, Resource.SettingsPage_RestartRequired_Message, SnackbarType.Success);
    }

    private void AlwaysOnTopToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing || !IsLoaded)
            return;

        var state = _alwaysOnTopToggle.IsChecked;
        if (state is null)
            return;

        _settings.Store.AlwaysOnTop = state.Value;
        _settings.SynchronizeStore();

        if (App.MainWindowInstance is { } w)
            w.Topmost = state.Value;
    }

    private async void EnableLoggingToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing || !IsLoaded)
            return;

        if (App.Current.MainWindow is not MainWindow mainWindow)
            return;

        var state = _enableLoggingToggle.IsChecked;
        if (state is null)
            return;

        const string logSuffix = " [LOGGING ENABLED]";

        await mainWindow.InvokeIfRequired(() =>
        {
            if (state.Value)
            {
                if (!mainWindow._title.Text.EndsWith(logSuffix))
                {
                    mainWindow._title.Text += logSuffix;
                }
            }
            else
            {
                mainWindow._title.Text = mainWindow._title.Text.Replace(logSuffix, string.Empty);
            }
        });

        Log.Instance.IsTraceEnabled = state.Value;
        AppFlags.Instance.IsTraceEnabled = state.Value;
        _settings.Store.EnableLogging = state.Value;
        _settings.SynchronizeStore();

        await Compatibility.PrintMachineInfoAsync();

        mainWindow._openLogIndicator.Visibility = Utils.BooleanToVisibilityConverter.Convert(_settings.Store.EnableLogging);
    }

    private void NotificationsCard_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing || !IsLoaded)
            return;

        var window = new NotificationsSettingsWindow { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }

    private void BackupSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing || !IsLoaded)
            return;

        var window = new SettingsBackupWindow { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }

    private async void HardwareSensors_Toggle(object sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (_isRefreshing || !IsLoaded)
            return;

        var state = _hardwareSensorsToggle.IsChecked;
        if (state is null)
            return;

        if (state.Value)
        {
            var pawnIOState = PawnIOHelper.GetPawnIOState();
            if (pawnIOState != PawnIOState.Installed)
            {
                await PawnIOHelper.TryShowPawnIODialogAsync(pawnIOState, true);

                _hardwareSensorsToggle.IsChecked = false;
                return;
            }

            var sensorsController = IoCContainer.Resolve<SensorsGroupController>();
            if (!sensorsController.IsLibreHardwareMonitorInitialized())
            {
                await sensorsController.IsSupportedAsync();
            }
        }
        else
        {
            _useNewSensorDashboardToggle.IsChecked = false;
            _settings.Store.UseNewSensorDashboard = false;
        }

        _settings.Store.EnableHardwareSensors = state.Value;
        _settings.SynchronizeStore();

        if (state.Value)
        {
            _hardwareSensorSettings.EnsureFileExists();
        }

        _useNewSensorDashboardCardControl.Visibility = state.Value ? Visibility.Visible : Visibility.Collapsed;

        MessagingCenter.Publish(new SensorDashboardSwappedMessage());
    }

    private async void UseNewSensorDashboard_Toggle(object sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (_isRefreshing || !IsLoaded)
            return;

        var state = _useNewSensorDashboardToggle.IsChecked;
        if (state is null)
            return;

        if (state.Value)
        {
            var pawnIOState = PawnIOHelper.GetPawnIOState();
            if (pawnIOState != PawnIOState.Installed)
            {
                await PawnIOHelper.TryShowPawnIODialogAsync(pawnIOState, true);

                _useNewSensorDashboardToggle.IsChecked = false;
                _settings.Store.UseNewSensorDashboard = false;
                _settings.SynchronizeStore();
                return;
            }
        }

        _settings.Store.UseNewSensorDashboard = state.Value;
        _settings.SynchronizeStore();

        if (state.Value)
        {
            _sensorsControlSettings.EnsureFileExists();
            _hardwareSensorSettings.EnsureFileExists();
        }

        MessagingCenter.Publish(new SensorDashboardSwappedMessage());
    }

    private void DashboardCustomButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing || !IsLoaded)
            return;

        var window = new EditSensorGroupWindow { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }

    private void ArgumentWindowButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing || !IsLoaded)
            return;

        ArgumentWindow.ShowInstance();
    }

    private void OsdToggle_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (_isRefreshing || !IsLoaded)
            return;

        try
        {
            var state = _osdToggle.IsChecked;
            if (state is null)
                return;

            Window? OsdPanelWindow = null;

            if (state.Value)
            {
                if (App.Current.OsdWindow == null)
                {
                    if (_OsdSettings.Store.SelectedStyleIndex == 0)
                    {
                        OsdPanelWindow = new OsdPanelWindow();
                    }
                    else if (_OsdSettings.Store.SelectedStyleIndex == 1)
                    {
                        OsdPanelWindow = new OsdBarWindow();
                    }

                    if (OsdPanelWindow != null)
                    {
                        App.Current.OsdWindow = OsdPanelWindow;
                        App.Current.OsdWindow.Show();
                    }
                }
                else
                {
                    bool needsStyleUpdate = false;

                    if (_OsdSettings.Store.SelectedStyleIndex == 0 && App.Current.OsdWindow.GetType() != typeof(OsdPanelWindow))
                    {
                        needsStyleUpdate = true;
                    }
                    else if (_OsdSettings.Store.SelectedStyleIndex == 1 && App.Current.OsdWindow.GetType() != typeof(OsdBarWindow))
                    {
                        needsStyleUpdate = true;
                    }

                    if (needsStyleUpdate)
                    {
                        App.Current.OsdWindow.Close();

                        if (_OsdSettings.Store.SelectedStyleIndex == 0)
                        {
                            OsdPanelWindow = new OsdPanelWindow();
                        }
                        else if (_OsdSettings.Store.SelectedStyleIndex == 1)
                        {
                            OsdPanelWindow = new OsdBarWindow();
                        }

                        if (OsdPanelWindow != null)
                        {
                            App.Current.OsdWindow = OsdPanelWindow;
                            App.Current.OsdWindow.Show();
                        }
                    }
                    else
                    {
                        if (!App.Current.OsdWindow.IsVisible)
                        {
                            App.Current.OsdWindow.Show();
                        }
                    }
                }
            }
            else
            {
                if (App.Current.OsdWindow != null)
                {
                    App.Current.OsdWindow.Hide();
                }
            }

            _OsdSettings.Store.ShowOsd = state.Value;
            _OsdSettings.SynchronizeStore();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Osd_Click error: {ex.Message}");

            _osdToggle.IsChecked = false;
            _OsdSettings.Store.ShowOsd = false;
            _OsdSettings.SynchronizeStore();
        }
    }



    private void CustomButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing || !IsLoaded)
            return;

        var window = new OsdSettingsAlias { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }

    private void SensorSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing || !IsLoaded)
            return;

        var window = new SensorSettingsWindow { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }

    private async void ResetSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing || !IsLoaded)
            return;

        var confirmed = await MessageBoxHelper.ShowAsync(
            Resource.SettingsPage_ResetSettings_Title,
            Resource.SettingsPage_ResetSettings_Confirm
        );

        if (!confirmed)
            return;

        try
        {
            App.IsRestoringSettings = true;

            var files = Directory.GetFiles(Folders.Settings);
            foreach (var file in files)
            {
                // Keep the migration marker so old per-user settings aren't copied back in after the reset.
                if (Path.GetFileName(file).Equals(".migrated", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    Log.Instance.Trace($"Failed to delete {file} during reset.", ex);
                }
            }

            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true });
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to reset settings.", ex);
        }
    }
}
