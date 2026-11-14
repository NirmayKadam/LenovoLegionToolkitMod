using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Shapes;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Settings;
using LenovoLegionToolkit.WPF.Utils;
using LenovoLegionToolkit.WPF.Utils.Skins;
using static LenovoLegionToolkit.WPF.Controls.Dashboard.StatusStripFormat;

namespace LenovoLegionToolkit.WPF.Controls.Dashboard;

/// <summary>
/// The dashboard's at-a-glance row: power mode, CPU, GPU, fans and battery. Every value comes from the
/// same sources the sensors section and power-mode card use; a reading the hardware doesn't report shows
/// as "—". Polling runs only while the strip is visible, at the sensors section's refresh interval.
/// </summary>
public partial class StatusStripControl
{
    private readonly ISensorsController _sensorsController = IoCContainer.Resolve<ISensorsController>();
    private readonly PowerModeFeature _powerModeFeature = IoCContainer.Resolve<PowerModeFeature>();
    private readonly PowerModeListener _powerModeListener = IoCContainer.Resolve<PowerModeListener>();
    private readonly ThermalModeListener _thermalModeListener = IoCContainer.Resolve<ThermalModeListener>();
    private readonly PowerStateListener _powerStateListener = IoCContainer.Resolve<PowerStateListener>();
    private readonly ApplicationSettings _applicationSettings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly DashboardSettings _dashboardSettings = IoCContainer.Resolve<DashboardSettings>();
    private readonly SensorsControlSettings _sensorsControlSettings = IoCContainer.Resolve<SensorsControlSettings>();

    private readonly ThemeManager _themeManager = IoCContainer.Resolve<ThemeManager>();

    private Border[] Bars => [_powerBar, _cpuBar, _gpuBar, _fanBar, _batteryBar];
    private TextBlock[] BlockBars => [_powerBlocks, _cpuBlocks, _gpuBlocks, _fanBlocks, _batteryBlocks];

    private readonly ThrottleLastDispatcher _powerThrottle = new(TimeSpan.FromMilliseconds(500), nameof(StatusStripControl));

    private CancellationTokenSource? _cts;
    private Task? _pollTask;
    private bool _subscribed;

    public StatusStripControl()
    {
        InitializeComponent();

        foreach (var bar in Bars)
            Motion.AnimateFill(bar, 0);

        IsVisibleChanged += StatusStripControl_IsVisibleChanged;
        SizeChanged += (_, e) => _grid.Columns = e.NewSize.Width switch
        {
            < 560 => 2,
            < 860 => 3,
            _ => 5
        };
    }

    private async void StatusStripControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            Subscribe(true);
            await RefreshPowerAsync();
            StartPolling();
            return;
        }

        Subscribe(false);
        await StopPollingAsync();
    }

    private void Subscribe(bool subscribe)
    {
        if (subscribe == _subscribed)
            return;
        _subscribed = subscribe;

        if (subscribe)
        {
            _powerModeListener.Changed += PowerModeListener_Changed;
            _thermalModeListener.Changed += ThermalModeListener_Changed;
            _powerStateListener.Changed += PowerStateListener_Changed;
            _themeManager.ThemeApplied += ThemeManager_ThemeApplied;
            ApplySkin();
        }
        else
        {
            _powerModeListener.Changed -= PowerModeListener_Changed;
            _thermalModeListener.Changed -= ThermalModeListener_Changed;
            _powerStateListener.Changed -= PowerStateListener_Changed;
            _themeManager.ThemeApplied -= ThemeManager_ThemeApplied;
        }
    }

    private void PowerModeListener_Changed(object? sender, PowerModeListener.ChangedEventArgs e) => QueuePowerRefresh();
    private void ThermalModeListener_Changed(object? sender, ThermalModeListener.ChangedEventArgs e) => QueuePowerRefresh();
    private void PowerStateListener_Changed(object? sender, PowerStateListener.ChangedEventArgs e) => QueuePowerRefresh();

    private async void QueuePowerRefresh() => await _powerThrottle.DispatchAsync(async () =>
    {
        await Dispatcher.InvokeAsync(async () =>
        {
            if (IsLoaded && IsVisible)
                await RefreshPowerAsync();
        });
    });

    private void ThemeManager_ThemeApplied(object? sender, EventArgs e) => ApplySkin();

    /// <summary>
    /// Retro draws the bars as block characters; other skins use the thin graphical bars.
    /// </summary>
    private void ApplySkin()
    {
        var retro = _themeManager.CurrentPalette.Skin == UiSkin.Retro;
        foreach (var bar in Bars)
            if (bar.Parent is Border track)
                track.Visibility = retro ? Visibility.Collapsed : Visibility.Visible;
        foreach (var blocks in BlockBars)
            blocks.Visibility = retro ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void SetBar(Border bar, TextBlock blocks, double fraction)
    {
        Motion.AnimateFill(bar, fraction);
        blocks.Text = RetroText.BlockBar(fraction);
    }

    // ---- Power mode tile ----

    private async Task RefreshPowerAsync()
    {
        try
        {
            if (!await _powerModeFeature.IsSupportedAsync())
            {
                _powerTile.Visibility = Visibility.Collapsed;
                return;
            }

            var state = await _powerModeFeature.GetStateAsync();
            var adapter = await Power.IsPowerAdapterConnectedAsync();

            _powerTile.Visibility = Visibility.Visible;
            _powerValue.Text = state.GetDisplayName();
            _powerSub.Text = AdapterText(adapter);
            _powerDot.SetResourceReference(Shape.FillProperty, ModeBrushKey(state));
            _powerDot.Visibility = Visibility.Visible;
            _powerBar.SetResourceReference(Border.BackgroundProperty, ModeBrushKey(state));
            SkinGlow.SetColorKey(_powerTile, ModeBrushKey(state));
            SkinGlow.SetEnabled(_powerTile, true);
            SetBar(_powerBar, _powerBlocks, 1);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Status strip couldn't read the power mode.", ex);

            _powerValue.Text = Unavailable;
            _powerSub.Text = Unavailable;
            _powerDot.Visibility = Visibility.Collapsed;
            SkinGlow.SetEnabled(_powerTile, false);
            SetBar(_powerBar, _powerBlocks, 0);
        }

        Describe(_powerTile, Resource.StatusStrip_Power, _powerValue.Text, _powerSub.Text);
    }

    // ---- Sensors and battery ----

    private TimeSpan RefreshInterval()
    {
        // Same interval as whichever sensors section the dashboard shows.
        var seconds = _applicationSettings.Store.EnableHardwareSensors && _applicationSettings.Store.UseNewSensorDashboard
            ? _sensorsControlSettings.Store.SensorsRefreshIntervalSeconds
            : _dashboardSettings.Store.SensorsRefreshIntervalSeconds;
        return TimeSpan.FromSeconds(Math.Max(0.5, seconds));
    }

    private void StartPolling()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _pollTask = Task.Run(async () =>
        {
            var sensorsSupported = false;
            try
            {
                sensorsSupported = await _sensorsController.IsSupportedAsync();
                if (sensorsSupported)
                    await _sensorsController.PrepareAsync();
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Status strip couldn't start the sensors.", ex);
            }

            while (!token.IsCancellationRequested)
            {
                var data = SensorsData.Empty;
                if (sensorsSupported)
                {
                    try
                    {
                        data = await _sensorsController.GetDataAsync();
                    }
                    catch (Exception ex)
                    {
                        Log.Instance.Trace($"Status strip sensor read failed.", ex);
                    }
                }

                BatteryInformation? battery = null;
                try
                {
                    battery = Battery.GetBatteryInformation();
                }
                catch (Exception ex)
                {
                    Log.Instance.Trace($"Status strip battery read failed.", ex);
                }

                await Dispatcher.InvokeAsync(() => UpdateValues(data, battery));

                try
                {
                    await Task.Delay(RefreshInterval(), token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, token);
    }

    private async Task StopPollingAsync()
    {
        if (_cts is not null)
            await _cts.CancelAsync();
        _cts = null;

        if (_pollTask is not null)
        {
            try
            {
                await _pollTask;
            }
            catch (OperationCanceledException) { }
        }
        _pollTask = null;
    }

    private void UpdateValues(SensorsData data, BatteryInformation? battery)
    {
        var unit = _applicationSettings.Store.TemperatureUnit;

        // CPU: temperature, then load and clock.
        var cpuText = SetTemperature(_cpuValue, data.CPU.Temperature, unit);
        // Skins with a "hot" color (Neon) use it for a CPU running hot; others keep the normal text color.
        var hot = IsHotCpu(data.CPU.Temperature) && TryFindResource(SkinKeys.HotBrush) is not null;
        _cpuValue.SetResourceReference(TextBlock.ForegroundProperty, hot ? SkinKeys.HotBrush : "TextFillColorPrimaryBrush");
        _cpuSub.Text = JoinAvailable(Percent(data.CPU.Utilization), Gigahertz(data.CPU.CoreClock));
        SetBar(_cpuBar, _cpuBlocks, Fraction(data.CPU.Utilization, data.CPU.MaxUtilization) ?? 0);
        Describe(_cpuTile, Resource.StatusStrip_Cpu, cpuText, _cpuSub.Text);

        // GPU: temperature, then load and clock. A sleeping discrete GPU reports nothing.
        var gpuText = SetTemperature(_gpuValue, data.GPU.Temperature, unit);
        _gpuSub.Text = JoinAvailable(Percent(data.GPU.Utilization), Megahertz(data.GPU.CoreClock));
        SetBar(_gpuBar, _gpuBlocks, Fraction(data.GPU.Utilization, data.GPU.MaxUtilization) ?? 0);
        Describe(_gpuTile, Resource.StatusStrip_Gpu, gpuText, _gpuSub.Text);

        // Fans: CPU fan as the headline, GPU fan underneath.
        var fanText = SetNumber(_fanValue, data.CPU.FanSpeed, 0, " " + Resource.RPM);
        _fanSub.Text = Rpm(data.GPU.FanSpeed) is { } gpuFan ? string.Format(Resource.StatusStrip_GpuFan, gpuFan) : Unavailable;
        SetBar(_fanBar, _fanBlocks, Fraction(data.CPU.FanSpeed, data.CPU.MaxFanSpeed) ?? 0);
        Describe(_fanTile, Resource.StatusStrip_Fans, fanText, _fanSub.Text);

        // Battery: charge, and whether the adapter is plugged in (same wording as the sensors section).
        string batteryText;
        if (battery is { } b)
        {
            batteryText = SetNumber(_batteryValue, b.BatteryPercentage, 0, Resource.Percent);
            _batterySub.Text = b.IsCharging ? Resource.DashboardBattery_AcConnected : Resource.DashboardBattery_AcDisconnected;
            SetBar(_batteryBar, _batteryBlocks, Fraction(b.BatteryPercentage, 100) ?? 0);
        }
        else
        {
            batteryText = SetNumber(_batteryValue, -1, 0, string.Empty);
            _batterySub.Text = Unavailable;
            SetBar(_batteryBar, _batteryBlocks, 0);
        }
        Describe(_batteryTile, Resource.StatusStrip_Battery, batteryText, _batterySub.Text);
    }

    /// <summary>
    /// Shows a temperature in the user's unit and returns the final text (the shown text may still be counting).
    /// </summary>
    private static string SetTemperature(TextBlock textBlock, int celsius, TemperatureUnit unit)
    {
        if (!IsAvailable(celsius))
            return ShowUnavailable(textBlock);

        var value = ToDisplayTemperature(celsius, unit);
        var suffix = " " + TemperatureSuffix(unit);
        Motion.AnimateNumber(textBlock, value, 0, suffix);
        return Motion.FormatNumber(value, 0, suffix);
    }

    /// <summary>
    /// Shows a reading and returns the final text, or "—" when the reading is missing.
    /// </summary>
    private static string SetNumber(TextBlock textBlock, int value, int decimals, string suffix)
    {
        if (!IsAvailable(value))
            return ShowUnavailable(textBlock);

        Motion.AnimateNumber(textBlock, value, decimals, suffix);
        return Motion.FormatNumber(value, decimals, suffix);
    }

    private static string ShowUnavailable(TextBlock textBlock)
    {
        textBlock.BeginAnimation(Motion.NumberValueProperty, null);
        textBlock.Text = Unavailable;
        return Unavailable;
    }

    /// <summary>
    /// Gives screen readers the whole tile as one phrase, e.g. "CPU: 62 °C, 45% · 3.2 GHz".
    /// </summary>
    private static void Describe(FrameworkElement tile, string label, string value, string sub) =>
        AutomationProperties.SetName(tile, $"{label}: {value}, {sub}");
}
