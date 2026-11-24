using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Controllers.GodMode;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Settings;
using Wpf.Ui.Appearance;
using Wpf.Ui.Common;
using System.Windows.Threading;
using Grid = System.Windows.Controls.Grid;
using Label = System.Windows.Controls.Label;

namespace LenovoLegionToolkit.WPF.Windows.Utils;

public partial class StatusWindow
{
    private readonly ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly DashboardSettings _dashboardSettings = IoCContainer.Resolve<DashboardSettings>();
    private readonly SensorsControlSettings _sensorsControlSettings = IoCContainer.Resolve<SensorsControlSettings>();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private readonly CancellationTokenSource _cancellationTokenSource = new();

    private DateTime _lastUpdate = DateTime.MinValue;
    private const int UI_UPDATE_THROTTLE_MS = 100;
    private const int RETRY_DELAY_MS = 1000;

    private readonly PowerModeFeature _powerModeFeature = IoCContainer.Resolve<PowerModeFeature>();
    private readonly ITSModeFeature _itsModeFeature = IoCContainer.Resolve<ITSModeFeature>();
    private readonly GodModeController _godModeController = IoCContainer.Resolve<GodModeController>();
    private readonly GPUController _gpuController = IoCContainer.Resolve<GPUController>();
    private readonly BatteryFeature _batteryFeature = IoCContainer.Resolve<BatteryFeature>();
    private readonly UpdateChecker _updateChecker = IoCContainer.Resolve<UpdateChecker>();
    private readonly UpdateSettings _updateSettings = IoCContainer.Resolve<UpdateSettings>();
    private readonly SensorsController _sensorsController = IoCContainer.Resolve<SensorsController>();
    private readonly SensorsGroupController _sensorsGroupController = IoCContainer.Resolve<SensorsGroupController>();
    private IDisposable? _sensorSubscription;

    private MachineInformation? _machineInfo;
    private bool _hasSystemFans;
    private bool _isSpecialModel;

    private bool IsSpecialModel => _isSpecialModel;

    private double _anchorX;
    private double _anchorY;
    private bool _anchorRight;
    private bool _anchorBottom;

    /// <summary>A part of the status that couldn't be read is left out; the window closing (cancellation) isn't logged.</summary>
    private static void LogReadFailure(string what, Exception ex)
    {
        if (ex is OperationCanceledException)
            return;

        Log.Instance.Trace($"Status window: could not read {what}.", ex);
    }

    private void SetMachineInformation(MachineInformation info)
    {
        _machineInfo = info;
        _isSpecialModel = info.LegionSeries is > LegionSeries.Legion_9 and not LegionSeries.Unknown;
    }

    private readonly struct StatusWindowData(
        PowerModeState? powerModeState,
        ITSMode? itsMode,
        string? godModePresetName,
        GPUStatus? gpuStatus,
        BatteryInformation? batteryInformation,
        BatteryState? batteryState,
        bool hasUpdate,
        SensorsData? sensorData = null,
        double cpuPower = -1,
        double gpuPower = -1,
        double cpuClock = -1,
        double cpuTemp = -1,
        double gpuClock = -1,
        double gpuTemp = -1)
    {
        public PowerModeState? PowerModeState { get; } = powerModeState;
        public ITSMode? ITSMode { get; } = itsMode;
        public string? GodModePresetName { get; } = godModePresetName;
        public GPUStatus? GPUStatus { get; } = gpuStatus;
        public BatteryInformation? BatteryInformation { get; } = batteryInformation;
        public BatteryState? BatteryState { get; } = batteryState;
        public bool HasUpdate { get; } = hasUpdate;
        public SensorsData? SensorsData { get; } = sensorData;
        public double CpuPower { get; } = cpuPower;
        public double GpuPower { get; } = gpuPower;
        public double CpuClock { get; } = cpuClock;
        public double CpuTemp { get; } = cpuTemp;
        public double GpuClock { get; } = gpuClock;
        public double GpuTemp { get; } = gpuTemp;
    }

    public static async Task<StatusWindow> CreateAsync()
    {
        var window = new StatusWindow();
        await window.InitializeAsync();
        return window;
    }

    private async Task InitializeAsync()
    {
        if (!_machineInfo.HasValue)
        {
            SetMachineInformation(await Compatibility.GetMachineInformationAsync());
        }
        var token = _cancellationTokenSource.Token;
        try
        {
            var initialData = await GetStatusWindowDataAsync(token);
            ApplyDataToUI(initialData);
        }
        catch (Exception ex)
        {
            LogReadFailure("the first status", ex);
        }

        await ShowActiveNowAsync();
    }

    /// <summary>
    /// The "Active now" line (game, AI boost, game-safe, fan curve…), read once per hover: the window is made anew each
    /// time. A failed read only leaves the line hidden.
    /// </summary>
    private async Task ShowActiveNowAsync()
    {
        try
        {
            var (state, _) = await Task.Run(() => new WPF.Utils.ActiveNowReader().ReadAsync());
            var line = WPF.Utils.ActiveNow.Line(WPF.Utils.ActiveNow.Build(state));
            _activeNowLabel.Text = line ?? string.Empty;
            _activeNowLabel.Visibility = line is null ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (Exception ex)
        {
            LogReadFailure("what is active now", ex);
            _activeNowLabel.Visibility = Visibility.Collapsed;
        }
    }

    public StatusWindow()
    {
        InitializeComponent();
        Loaded += StatusWindow_Loaded;
        SizeChanged += StatusWindow_SizeChanged;
        IsVisibleChanged += StatusWindow_IsVisibleChanged;
        WindowStyle = WindowStyle.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Focusable = false;
        Topmost = true;
        ExtendsContentIntoTitleBar = true;
        ShowInTaskbar = false;
        ShowActivated = false;

#if DEBUG
        _title.Text += " [DEBUG]";
#else
        var version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
        if (version is not null && version.IsBeta()) _title.Text += " [BETA]";
#endif
        if (Log.Instance.IsTraceEnabled) _title.Text += " [LOGGING ENABLED]";
    }

    private void UpdateUiLayout(GPUStatus? gpuStatus)
    {
        if (!_machineInfo.HasValue) return;

        var useSensors = _settings.Store.EnableHardwareSensors;
        var sensorVis = useSensors ? Visibility.Visible : Visibility.Collapsed;

        _cpuGrid.Visibility = sensorVis;
        _cpuFreqAndTempDesc.Visibility = sensorVis;
        _cpuFreqAndTempLabel.Visibility = sensorVis;
        _cpuFanAndPowerDesc.Visibility = sensorVis;
        _cpuFanAndPowerLabel.Visibility = sensorVis;

        _systemFansControl.Visibility = useSensors && _hasSystemFans && !IsSpecialModel ? Visibility.Visible : Visibility.Collapsed;

        if (gpuStatus.HasValue)
        {
            _gpuGrid.Visibility = Visibility.Visible;
            var isGpuOn = gpuStatus.Value.State != GPUState.PoweredOff;
            var gpuDetailsVis = isGpuOn ? Visibility.Visible : Visibility.Collapsed;
            var gpuSensorVis = (useSensors && isGpuOn) ? Visibility.Visible : Visibility.Collapsed;

            _gpuActive.Visibility = gpuStatus.Value.State == GPUState.Active ? Visibility.Visible : Visibility.Collapsed;
            _gpuInactive.Visibility = gpuStatus.Value.State == GPUState.Inactive ? Visibility.Visible : Visibility.Collapsed;
            _gpuPoweredOff.Visibility = gpuStatus.Value.State == GPUState.PoweredOff ? Visibility.Visible : Visibility.Collapsed;

            _gpuPowerStateValue.Visibility = gpuDetailsVis;
            _gpuPowerStateValueLabel.Visibility = gpuDetailsVis;
            _gpuFreqAndTempDesc.Visibility = gpuSensorVis;
            _gpuFreqAndTempLabel.Visibility = gpuSensorVis;
            _gpuFanAndPowerDesc.Visibility = gpuSensorVis;
            _gpuFanAndPowerLabel.Visibility = gpuSensorVis;
        }
        else
        {
            _gpuGrid.Visibility = Visibility.Collapsed;
        }
    }

    private async void StatusWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            ComputeAnchorAndPosition();
            if (!_machineInfo.HasValue)
            {
                SetMachineInformation(await Compatibility.GetMachineInformationAsync());
            }
        }
        catch (Exception ex)
        {
            LogReadFailure("the machine information", ex);
        }
    }

    private void StatusWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_anchorRight)
            Left = _anchorX - ActualWidth;
        else
            Left = _anchorX;

        if (_anchorBottom)
            Top = _anchorY - ActualHeight;
        else
            Top = _anchorY;
    }

    private void StatusWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            if (!_settings.Store.EnableHardwareSensors)
                return;

            _sensorsGroupController.SensorsUpdated -= OnSensorsUpdated;
            _sensorsGroupController.SensorsUpdated += OnSensorsUpdated;

            var refreshInterval = _settings.Store.UseNewSensorDashboard
                ? _sensorsControlSettings.Store.SensorsRefreshIntervalSeconds
                : _dashboardSettings.Store.SensorsRefreshIntervalSeconds;

            _sensorSubscription?.Dispose();
            _sensorSubscription = _sensorsGroupController.Subscribe(TimeSpan.FromSeconds(refreshInterval), HardwareUpdateScope.Cpu | HardwareUpdateScope.Gpu);
        }
        else
        {
            _sensorSubscription?.Dispose();
            _sensorsGroupController.SensorsUpdated -= OnSensorsUpdated;
            _cancellationTokenSource.Cancel();
        }
    }

    /// <summary>The last reading of the slow parts (power mode, GPU state, battery, updates) and when it was taken.</summary>
    private StatusWindowData? _lastFull;
    private long _lastFullAtMs;

    /// <summary>How often the slow parts are read again while the window stays open; sensor values follow every tick.</summary>
    private const int SLOW_PARTS_EVERY_MS = 5000;

    private async void OnSensorsUpdated(HardwareSensorSnapshot snapshot)
    {
        // One update at a time: a tick that arrives while the previous one is still reading is skipped. Before, each
        // tick read everything again (a full GPU refresh included) and slow reads piled up and finished out of order.
        if (!_refreshLock.Wait(0))
            return;

        var token = _cancellationTokenSource.Token;
        try
        {
            var slowParts = _lastFull is null || Environment.TickCount64 - _lastFullAtMs >= SLOW_PARTS_EVERY_MS;
            var data = await GetStatusWindowDataAsync(token, snapshot: snapshot, slowParts: slowParts);
            if (token.IsCancellationRequested) return;

            if ((DateTime.Now - _lastUpdate).TotalMilliseconds >= UI_UPDATE_THROTTLE_MS)
            {
                _lastUpdate = DateTime.Now;
                await Dispatcher.InvokeAsync(() => ApplyDataToUI(data), DispatcherPriority.Normal, token);
            }
        }
        catch (Exception ex)
        {
            LogReadFailure("the sensors", ex);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<StatusWindowData> GetStatusWindowDataAsync(CancellationToken token, HardwareSensorSnapshot? snapshot = null, bool slowParts = true)
    {
        if (!_machineInfo.HasValue)
        {
            SetMachineInformation(await Compatibility.GetMachineInformationAsync());
        }

        PowerModeState? state = null;
        ITSMode? mode = null;
        string? godModePresetName = null;
        GPUStatus? gpuStatus = null;
        BatteryInformation? batteryInfo = null;
        BatteryState? batteryState = null;
        bool hasUpdate = false;
        SensorsData? sensorsData = null;
        double cpuPower = -1, gpuPower = -1, cpuClock = -1, cpuTemp = -1, gpuClock = -1, gpuTemp = -1;

        if (slowParts || _lastFull is null)
        {
        var tasks = new List<Task>
        {
            Task.Run(async () =>
            {
                try
                {
                    if (await _powerModeFeature.IsSupportedAsync().WaitAsync(token))
                    {
                        state = await _powerModeFeature.GetStateAsync().WaitAsync(token);
                        if (state == PowerModeState.GodMode) godModePresetName = await _godModeController.GetActivePresetNameAsync().WaitAsync(token);
                    }
                    else if (await _itsModeFeature.IsSupportedAsync().WaitAsync(token))
                    {
                        mode = await _itsModeFeature.GetStateAsync().WaitAsync(token);
                    }
                }
                catch (Exception ex) { LogReadFailure("the power mode", ex); }
            }, token),
            Task.Run(async () => { try { if (_gpuController.IsSupported()) gpuStatus = await _gpuController.RefreshNowAsync().WaitAsync(token); } catch (Exception ex) { LogReadFailure("the GPU state", ex); } }, token),
            Task.Run(() => { try { batteryInfo = Battery.GetBatteryInformation(); } catch (Exception ex) { LogReadFailure("the battery", ex); } }, token),
            Task.Run(async () => { try { if (await _batteryFeature.IsSupportedAsync().WaitAsync(token)) batteryState = await _batteryFeature.GetStateAsync().WaitAsync(token); } catch (Exception ex) { LogReadFailure("the battery mode", ex); } }, token),
            Task.Run(async () =>
            {
                try
                {
                    if ((await _updateChecker.GetUpdatesAsync().WaitAsync(token)).Length > 0)
                    {
                        hasUpdate = true;
                    }
                    else if (_updateSettings.Store.UpdateCheckFrequency != UpdateCheckFrequency.Never)
                    {
                        hasUpdate = await _updateChecker.CheckAsync(false).WaitAsync(token) is not null;
                    }
                }
                catch (Exception ex) { LogReadFailure("updates", ex); }
            }, token)
        };

        await Task.WhenAll(tasks);
        }
        else if (_lastFull is { } last)
        {
            (state, mode, godModePresetName, gpuStatus, batteryInfo, batteryState, hasUpdate) =
                (last.PowerModeState, last.ITSMode, last.GodModePresetName, last.GPUStatus, last.BatteryInformation, last.BatteryState, last.HasUpdate);
        }

        if (_settings.Store.EnableHardwareSensors)
        {
            try
            {
                if (!IsSpecialModel && await _sensorsController.IsSupportedAsync().WaitAsync(token))
                {
                    sensorsData = await _sensorsController.GetDataAsync().WaitAsync(token);
                }

                if (_sensorsGroupController.IsLibreHardwareMonitorInitialized())
                {
                    var groupSnapshot = snapshot ?? _sensorsGroupController.Snapshot;
                    cpuPower = groupSnapshot[SensorItem.CpuPower];
                    gpuPower = groupSnapshot[SensorItem.GpuPower];
                    cpuTemp = groupSnapshot[SensorItem.CpuTemperature];
                    gpuClock = groupSnapshot[SensorItem.GpuFrequency];
                    gpuTemp = groupSnapshot[SensorItem.GpuCoreTemperature];

                    cpuClock = _sensorsGroupController.GetCpuFrequency(groupSnapshot);
                }
            }
            catch { /* Ignore */ }
        }

        var result = new StatusWindowData(state, mode, godModePresetName, gpuStatus, batteryInfo, batteryState, hasUpdate, sensorsData, cpuPower, gpuPower, cpuClock, cpuTemp, gpuClock, gpuTemp);
        if (slowParts || _lastFull is null)
        {
            _lastFull = result;
            _lastFullAtMs = Environment.TickCount64;
        }
        return result;
    }

    private void ApplyDataToUI(StatusWindowData data)
    {
        UpdateUiLayout(data.GPUStatus);
        RefreshPowerMode(data.PowerModeState, data.ITSMode, data.GodModePresetName);

        var useSensors = _settings.Store.EnableHardwareSensors;

        if (useSensors)
        {
            ApplySensorsData(data);
        }

        if (data.GPUStatus.HasValue)
        {
            _gpuPowerStateValueLabel.Content = data.GPUStatus.Value.PerformanceState ?? "-";
        }

        RefreshBattery(data.BatteryInformation, data.BatteryState);
        RefreshUpdate(data.HasUpdate);
    }

    private void ApplySensorsData(StatusWindowData data)
    {

        _cpuFreqAndTempDesc.Content = Resource.StatusWindow_Frequency_And_Temperature;
        UpdateFreqAndTemp(_cpuFreqAndTempLabel, data.CpuClock, data.CpuTemp);

        if (IsSpecialModel)
        {
            _cpuFanAndPowerDesc.Content = Resource.SensorsControl_CPU_Power;
            UpdatePowerOnly(_cpuFanAndPowerLabel, data.CpuPower);
        }
        else
        {
            _cpuFanAndPowerDesc.Content = Resource.StatusWindow_Fan_And_Power;
            UpdateFanAndPower(_cpuFanAndPowerLabel, data.SensorsData?.CPU.FanSpeed ?? -1, data.CpuPower);
        }

        UpdateSystemFans(data.SensorsData?.SystemFans ?? Array.Empty<FanData>());
        _systemFansControl.Visibility = _settings.Store.EnableHardwareSensors && _hasSystemFans && !IsSpecialModel ? Visibility.Visible : Visibility.Collapsed;

        if (data.GPUStatus.HasValue && data.GPUStatus.Value.State != GPUState.PoweredOff)
        {
            _gpuFreqAndTempDesc.Content = Resource.StatusWindow_Frequency_And_Temperature;
            UpdateFreqAndTemp(_gpuFreqAndTempLabel, data.GpuClock, data.GpuTemp);

            if (IsSpecialModel)
            {
                _gpuFanAndPowerDesc.Content = Resource.SensorsControl_GPU_Power;
                UpdatePowerOnly(_gpuFanAndPowerLabel, data.GpuPower);
            }
            else
            {
                _gpuFanAndPowerDesc.Content = Resource.StatusWindow_Fan_And_Power;
                UpdateFanAndPower(_gpuFanAndPowerLabel, data.SensorsData?.GPU.FanSpeed ?? -1, data.GpuPower);
            }
        }
    }

    private void UpdateSystemFans(IReadOnlyList<FanData> fans)
    {
        var showNumber = fans.Count > 1;
        UpdateSystemFanRow(_systemFan1Row, _systemFan1Name, _systemFan1Value, fans.Count > 0 ? fans[0] : null, showNumber);
        UpdateSystemFanRow(_systemFan2Row, _systemFan2Name, _systemFan2Value, fans.Count > 1 ? fans[1] : null, showNumber);
        _hasSystemFans = fans.Count > 0;
    }

    private static void UpdateSystemFanRow(Grid row, Label name, Label value, FanData? fan, bool showNumber)
    {
        if (fan is not { } data)
        {
            row.Visibility = Visibility.Collapsed;
            return;
        }

        row.Visibility = Visibility.Visible;
        name.Content = showNumber ? $"{Resource.StatusWindow_System_Fan} {data.DisplayNumber}" : Resource.StatusWindow_System_Fan;
        value.Content = data.Speed < 0 ? "-" : $"{data.Speed} {Resource.RPM}";
    }

    private void RefreshPowerMode(PowerModeState? powerModeState, ITSMode? itsMode, string? godModePresetName)
    {
        if (powerModeState != null)
        {
            _powerModeValueLabel.Content = powerModeState.GetDisplayName();
            _powerModeValueIndicator.Fill = powerModeState?.GetSolidColorBrush() ?? new SolidColorBrush(Colors.Transparent);
            var isGod = powerModeState == PowerModeState.GodMode;
            _powerModePresetLabel.Visibility = isGod ? Visibility.Visible : Visibility.Collapsed;
            _powerModePresetValueLabel.Visibility = isGod ? Visibility.Visible : Visibility.Collapsed;
            _powerModePresetValueLabel.Content = godModePresetName ?? "-";
        }
        else
        {
            _powerModeValueLabel.Content = itsMode?.GetDisplayName() ?? "-";
            _powerModeValueIndicator.Fill = itsMode?.GetSolidColorBrush() ?? Brushes.Transparent;
            _powerModePresetLabel.Visibility = _powerModePresetValueLabel.Visibility = Visibility.Collapsed;
        }
    }

    private void RefreshBattery(BatteryInformation? batteryInfo, BatteryState? batteryState)
    {
        if (!batteryInfo.HasValue || !batteryState.HasValue) { _batteryIcon.Symbol = SymbolRegular.Battery024; _batteryValueLabel.Content = "-"; return; }
        var info = batteryInfo.Value;
        _batteryIcon.Symbol = (int)Math.Round(info.BatteryPercentage / 10.0) switch { 10 => SymbolRegular.Battery1024, 9 => SymbolRegular.Battery924, 8 => SymbolRegular.Battery824, 7 => SymbolRegular.Battery724, 6 => SymbolRegular.Battery624, 5 => SymbolRegular.Battery524, 4 => SymbolRegular.Battery424, 3 => SymbolRegular.Battery324, 2 => SymbolRegular.Battery224, 1 => SymbolRegular.Battery124, _ => SymbolRegular.Battery024 };
        if (info.IsCharging) _batteryIcon.Symbol = batteryState == BatteryState.Conservation ? SymbolRegular.BatterySaver24 : SymbolRegular.BatteryCharge24;
        if (info.IsLowBattery) _batteryValueLabel.SetResourceReference(ForegroundProperty, "SystemFillColorCautionBrush"); else _batteryValueLabel.ClearValue(ForegroundProperty);
        _batteryValueLabel.Content = $"{info.BatteryPercentage}{Resource.Percent}";
        _batteryModeValueLabel.Content = batteryState.GetDisplayName();
        _batteryDischargeValueLabel.Content = info.DischargeRate == int.MinValue
            ? "-"
            : $"{info.DischargeRate / 1000.0:+0.00;-0.00;0.00} {Resource.Watt}";
        _batteryMinDischargeValueLabel.Content = $"{info.MinDischargeRate / 1000.0:+0.00;-0.00;0.00} {Resource.Watt}";
        _batteryMaxDischargeValueLabel.Content = $"{info.MaxDischargeRate / 1000.0:+0.00;-0.00;0.00} {Resource.Watt}";
    }

    private void RefreshUpdate(bool hasUpdate) => _updateIndicator.Visibility = hasUpdate ? Visibility.Visible : Visibility.Collapsed;
    private string GetTemperatureText(double t) => (double.IsNaN(t) || t < 0) ? "-" : (_settings.Store.TemperatureUnit == TemperatureUnit.F ? $"{(t * 9 / 5 + 32):0}{Resource.Fahrenheit}" : $"{t:0}{Resource.Celsius}");
    private void UpdateFreqAndTemp(Label l, double f, double t) => l.Content = (t < 0 || f < 0) ? "-" : $"{f:0}{Resource.MHz} | {GetTemperatureText(t)}";
    private static void UpdateFanAndPower(Label l, double f, double p) => l.Content = (f < 0 || p < 0) ? "-" : $"{f:0}{Resource.RPM} | {p:0}{Resource.Watt}";
    private static void UpdatePowerOnly(Label l, double p) => l.Content = p < 0 ? "-" : $"{p:0}{Resource.Watt}";

    private void ComputeAnchorAndPosition()
    {
        UpdateLayout();

        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget == null)
        {
            Left = 0;
            Top = 0;
            return;
        }

        var matrix = source.CompositionTarget.TransformFromDevice;

        var mousePoint = Control.MousePosition;

        var screen = Screen.FromPoint(mousePoint);
        var workingArea = screen.WorkingArea;

        var mouseLogical = matrix.Transform(new Point(mousePoint.X, mousePoint.Y));
        var screenLeftTop = matrix.Transform(new Point(workingArea.Left, workingArea.Top));
        var screenRightBottom = matrix.Transform(new Point(workingArea.Right, workingArea.Bottom));

        const double offset = 8;

        if (mouseLogical.X + offset + ActualWidth > screenRightBottom.X)
        {
            _anchorRight = true;
            _anchorX = mouseLogical.X - offset;
            Left = _anchorX - ActualWidth;
        }
        else
        {
            _anchorRight = false;
            _anchorX = mouseLogical.X + offset;
            Left = _anchorX;
        }

        if (mouseLogical.Y + offset + ActualHeight > screenRightBottom.Y)
        {
            _anchorBottom = true;
            _anchorY = mouseLogical.Y - offset;
            Top = _anchorY - ActualHeight;
        }
        else
        {
            _anchorBottom = false;
            _anchorY = mouseLogical.Y + offset;
            Top = _anchorY;
        }

        if (Left < screenLeftTop.X) { Left = screenLeftTop.X; _anchorX = _anchorRight ? Left + ActualWidth : Left; }
        if (Top < screenLeftTop.Y) { Top = screenLeftTop.Y; _anchorY = _anchorBottom ? Top + ActualHeight : Top; }
    }
}
