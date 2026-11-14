using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;
using Wpf.Ui.Common;
using Button = Wpf.Ui.Controls.Button;

namespace LenovoLegionToolkit.WPF.Controls.Dashboard;

public class PowerModeControl : AbstractComboBoxFeatureCardControl<PowerModeState>
{
    private readonly ThermalModeListener _thermalModeListener = IoCContainer.Resolve<ThermalModeListener>();
    private readonly PowerModeListener _powerModeListener = IoCContainer.Resolve<PowerModeListener>();

    private readonly ThrottleLastDispatcher _throttleDispatcher = new(TimeSpan.FromMilliseconds(500), nameof(PowerModeControl));

    private readonly Button _configButton = new()
    {
        Icon = SymbolRegular.Settings24,
        FontSize = 20,
        Margin = new(8, 0, 0, 0),
        Visibility = Visibility.Collapsed,
    };

    public PowerModeControl()
    {
        Icon = SymbolRegular.Gauge24;
        Title = Resource.PowerModeControl_Title;
        Subtitle = Resource.PowerModeControl_Message;
        HelpKey = "Help_Dashboard_PowerMode";

        AutomationProperties.SetName(_configButton, Resource.PowerModeControl_Title);

        _thermalModeListener.Changed += ThermalModeListener_Changed;
        _powerModeListener.Changed += PowerModeListener_Changed;
    }

    private async void ThermalModeListener_Changed(object? sender, ThermalModeListener.ChangedEventArgs e) => await _throttleDispatcher.DispatchAsync(async () =>
    {
        await Dispatcher.InvokeAsync(async () =>
        {
            if (IsLoaded && IsVisible)
                await RefreshAsync();
        });
    });

    private async void PowerModeListener_Changed(object? sender, PowerModeListener.ChangedEventArgs e) => await _throttleDispatcher.DispatchAsync(async () =>
    {
        await Dispatcher.InvokeAsync(async () =>
        {
            if (IsLoaded && IsVisible)
                await RefreshAsync();
        });
    });

    protected override async Task OnRefreshAsync()
    {
        await base.OnRefreshAsync();

        var adapterStatus = await Power.IsPowerAdapterConnectedAsync();
        Warning = TryGetSelectedItem(out var state) && PowerModeActions.ShowsAdapterWarning(state, adapterStatus)
            ? Resource.PowerModeControl_Warning
            : string.Empty;
    }

    protected override async Task OnStateChangeAsync(ComboBox comboBox, IFeature<PowerModeState> feature, PowerModeState? newValue, PowerModeState? oldValue)
    {
        await base.OnStateChangeAsync(comboBox, feature, newValue, oldValue);

        if (newValue is null)
        {
            return;
        }

        var mi = await Compatibility.GetMachineInformationAsync();
        var adapterStatus = await Power.IsPowerAdapterConnectedAsync();

        var shouldShowButton = PowerModeActions.HasSettings(newValue.Value, mi.Properties.SupportsAIMode, mi.Properties.SupportsGodMode, adapterStatus);

        _configButton.ToolTip = shouldShowButton ? Resource.PowerModeControl_Settings : null;
        _configButton.Visibility = shouldShowButton ? Visibility.Visible : Visibility.Collapsed;
    }

    protected override void OnStateChangeException(Exception exception) => PowerModeActions.ShowSetStateError(exception);

    protected override FrameworkElement GetAccessory(ComboBox comboBox)
    {
        _configButton.Click += ConfigButton_Click;

        var stackPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
        };
        stackPanel.Children.Add(_configButton);
        stackPanel.Children.Add(comboBox);

        return stackPanel;
    }

    private async void ConfigButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedItem(out var state))
        {
            return;
        }

        await PowerModeActions.OpenSettingsAsync(state, this);
    }
}
