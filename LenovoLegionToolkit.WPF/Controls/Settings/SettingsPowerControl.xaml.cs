using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using LenovoLegionToolkit.WPF.Windows.Settings;

namespace LenovoLegionToolkit.WPF.Controls.Settings;

public partial class SettingsPowerControl
{
    private readonly ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly PowerModeFeature _powerModeFeature = IoCContainer.Resolve<PowerModeFeature>();
    private readonly ITSModeFeature _itsModeFeature = IoCContainer.Resolve<ITSModeFeature>();

    private bool _isRefreshing;

    public SettingsPowerControl()
    {
        InitializeComponent();
    }

    public async Task RefreshAsync()
    {
        _isRefreshing = true;

        try
        {
            var mi = await Compatibility.GetMachineInformationAsync();
            if (mi.Features[CapabilityID.GodModeFnQSwitchable])
            {
                _godModeFnQSwitchableCard.Visibility = Visibility.Visible;
                _godModeFnQSwitchableToggle.IsChecked = await WMI.LenovoOtherMethod.GetFeatureValueAsync(CapabilityID.GodModeFnQSwitchable) == 1;
            }
            else
            {
                _godModeFnQSwitchableCard.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            _godModeFnQSwitchableCard.Visibility = Visibility.Collapsed;

            Log.Instance.Trace($"Failed to get GodModeFnQSwitchable status.", ex);
        }

        var isPowerModeFeatureSupported = await _powerModeFeature.IsSupportedAsync();
        var isITSModeFeatureSupported = await _itsModeFeature.IsSupportedAsync();
        var isAnyPowerFeatureSupported = isPowerModeFeatureSupported || isITSModeFeatureSupported;
        var isWindowsPowerModeSupported = WindowsPowerModeController.IsOverlaySupported;
        var powerModeMappingMode = _settings.Store.PowerModeMappingMode;

        if (!isWindowsPowerModeSupported && powerModeMappingMode == PowerModeMappingMode.WindowsPowerMode)
        {
            powerModeMappingMode = PowerModeMappingMode.Disabled;
            _settings.Store.PowerModeMappingMode = powerModeMappingMode;
            _settings.SynchronizeStore();
        }

        UpdatePowerModeMappingUi(powerModeMappingMode, isWindowsPowerModeSupported, isAnyPowerFeatureSupported);

        if (isITSModeFeatureSupported && _settings.Store.PowerModeMappingMode != PowerModeMappingMode.Disabled)
            _powerModeMappingCardHeader.Warning = Resource.SettingsPage_PowerModeMapping_ITSWarning;
        else
            _powerModeMappingCardHeader.Warning = string.Empty;

        _onBatterySinceResetToggle.IsChecked = _settings.Store.ResetBatteryOnSinceTimerOnReboot;
        _onBatterySinceResetToggle.Visibility = Visibility.Visible;

        _godModeFnQSwitchableToggle.Visibility = Visibility.Visible;
        _powerModeMappingComboBox.Visibility = Visibility.Visible;

        _isRefreshing = false;
    }

    private async void GodModeFnQSwitchableToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
        {
            return;
        }

        var state = _godModeFnQSwitchableToggle.IsChecked;
        if (state is null)
        {
            return;
        }

        _godModeFnQSwitchableToggle.IsEnabled = false;

        try
        {
            await WMI.LenovoOtherMethod.SetFeatureValueAsync(CapabilityID.GodModeFnQSwitchable, state.Value ? 1 : 0);
        }
        catch (Exception ex)
        {
            // The firmware refused: the switch goes back to what is set (it used to stay on the new position, greyed
            // out for good, with the general "Unexpected exception" message).
            _godModeFnQSwitchableToggle.IsChecked = !state.Value;
            UiErrors.Report("Changing Custom mode in Fn+Q", ex);
        }
        finally
        {
            _godModeFnQSwitchableToggle.IsEnabled = true;
        }
    }

    private async void PowerModeMappingComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing)
        {
            return;
        }

        if (!_powerModeMappingComboBox.TryGetSelectedItem(out PowerModeMappingMode powerModeMappingMode))
        {
            return;
        }

        // Read before anything is saved; a failure leaves the setting as it was and says so (it used to end in the
        // general "Unexpected exception" message).
        bool isPowerModeFeatureSupported, isITSModeFeatureSupported;
        try
        {
            isPowerModeFeatureSupported = await _powerModeFeature.IsSupportedAsync();
            isITSModeFeatureSupported = await _itsModeFeature.IsSupportedAsync();
        }
        catch (Exception ex)
        {
            UiErrors.Report("Reading the power modes for the mapping", ex);
            return;
        }
        var isAnyPowerFeatureSupported = isPowerModeFeatureSupported || isITSModeFeatureSupported;
        var isWindowsPowerModeSupported = WindowsPowerModeController.IsOverlaySupported;
        if (!isWindowsPowerModeSupported && powerModeMappingMode == PowerModeMappingMode.WindowsPowerMode)
        {
            powerModeMappingMode = PowerModeMappingMode.Disabled;
        }

        _settings.Store.PowerModeMappingMode = powerModeMappingMode;
        _settings.SynchronizeStore();

        UpdatePowerModeMappingUi(powerModeMappingMode, isWindowsPowerModeSupported, isAnyPowerFeatureSupported, updateItems: false);

        if (isITSModeFeatureSupported && powerModeMappingMode != PowerModeMappingMode.Disabled)
        {
            _powerModeMappingCardHeader.Warning = Resource.SettingsPage_PowerModeMapping_ITSWarning;
        }
        else
        {
            _powerModeMappingCardHeader.Warning = string.Empty;
        }

        if (powerModeMappingMode != PowerModeMappingMode.Disabled)
        {
            // The mapping is saved; putting Windows' power settings in line can fail on its own and is reported.
            await UiErrors.RunAsync("Setting the Windows power plan or mode for the mapping",
                () => _powerModeFeature.EnsureCorrectWindowsPowerSettingsAreSetAsync());
        }
    }

    private void UpdatePowerModeMappingUi(PowerModeMappingMode mappingMode, bool isWindowsPowerModeSupported, bool isAnyPowerFeatureSupported, bool updateItems = true)
    {
        _powerModeMappingCard.Visibility = isAnyPowerFeatureSupported ? Visibility.Visible : Visibility.Collapsed;
        if (updateItems)
        {
            var mappingModes = Enum.GetValues<PowerModeMappingMode>();
            if (!isWindowsPowerModeSupported)
            {
                mappingModes = mappingModes.Where(t => t != PowerModeMappingMode.WindowsPowerMode).ToArray();
            }

            _powerModeMappingComboBox.SetItems(mappingModes, mappingMode, t => t.GetDisplayName());
        }
        _powerModesCard.Visibility = isWindowsPowerModeSupported && mappingMode == PowerModeMappingMode.WindowsPowerMode && isAnyPowerFeatureSupported ? Visibility.Visible : Visibility.Collapsed;
        _windowsPowerPlansCard.Visibility = mappingMode == PowerModeMappingMode.WindowsPowerPlan && isAnyPowerFeatureSupported ? Visibility.Visible : Visibility.Collapsed;
        _windowsPowerPlansControlPanelCard.Visibility = mappingMode == PowerModeMappingMode.WindowsPowerPlan && isAnyPowerFeatureSupported ? Visibility.Visible : Visibility.Collapsed;
    }

    private void WindowsPowerPlans_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        var window = new WindowsPowerPlansWindow { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }

    private void PowerModes_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        var window = new WindowsPowerModesWindow { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }

    private void WindowsPowerPlansControlPanel_Click(object sender, RoutedEventArgs e)
    {
        Process.Start("control", "/name Microsoft.PowerOptions");
    }

    private void OnBatterySinceResetToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        var state = _onBatterySinceResetToggle.IsChecked;
        if (state is null)
            return;

        _settings.Store.ResetBatteryOnSinceTimerOnReboot = state.Value;
        _settings.SynchronizeStore();
    }

    private void FanCurvesButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new LenovoLegionToolkit.WPF.Windows.Dashboard.FanCurvesWindow { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }

    private void PowerModeProfilesButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new LenovoLegionToolkit.WPF.Windows.Dashboard.PowerModeProfilesWindow { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }
}
