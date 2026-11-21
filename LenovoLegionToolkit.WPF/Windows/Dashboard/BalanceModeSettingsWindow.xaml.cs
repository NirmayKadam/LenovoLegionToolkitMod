using System;
using System.Windows;
using System.Windows.Input;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.WPF.Windows.Dashboard;

public partial class BalanceModeSettingsWindow
{
    private readonly PowerModeFeature _powerModeFeature = IoCContainer.Resolve<PowerModeFeature>();
    private readonly AIController _aiController = IoCContainer.Resolve<AIController>();

    public BalanceModeSettingsWindow()
    {
        InitializeComponent();

        IsVisibleChanged += BalanceModeSettingsWindow_IsVisibleChanged;
    }

    private void BalanceModeSettingsWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible)
            return;

        _aiModeCheckBox.IsChecked = _aiController.IsAIModeEnabled;
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var isAiModeChecked = _aiModeCheckBox.IsChecked ?? false;

        _aiController.IsAIModeEnabled = isAiModeChecked;

        try
        {
            await _aiController.StopAsync();
            await _powerModeFeature.SetStateAsync(PowerModeState.Balance);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Applying the Balance mode settings failed.", ex);
        }
        finally
        {
            // Started again whatever happened above: a failure used to leave AI mode stopped until Indra restarted.
            try
            {
                await _aiController.StartIfNeededAsync();
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Restarting AI mode failed.", ex);
            }
        }

        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();
}
