using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Shapes;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Controllers.GodMode;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using Wpf.Ui.Common;

namespace LenovoLegionToolkit.WPF.Controls.Dashboard;

/// <summary>
/// "What this power mode sets": the power and temperature limits, fan curve and clock offsets of the active
/// power mode, with what changed since the previous one. Updates when the mode changes from anywhere (Fn+Q,
/// the dashboard, automations) and when the Custom preset changes. Values come from the firmware's own per-mode
/// table or the Custom preset; anything the laptop doesn't report is simply not listed.
/// </summary>
public partial class PowerModeDetailsControl
{
    private readonly PowerModeFeature _feature = IoCContainer.Resolve<PowerModeFeature>();
    private readonly PowerModeProfileReader _reader = IoCContainer.Resolve<PowerModeProfileReader>();
    private readonly PowerModeListener _powerModeListener = IoCContainer.Resolve<PowerModeListener>();
    private readonly GodModeController _godModeController = IoCContainer.Resolve<GodModeController>();
    private readonly GPUOverclockController _gpuOverclockController = IoCContainer.Resolve<GPUOverclockController>();
    private readonly FanProfileController _fanProfileController = IoCContainer.Resolve<FanProfileController>();
    private readonly ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly ThrottleLastDispatcher _throttle = new(TimeSpan.FromMilliseconds(400), nameof(PowerModeDetailsControl));

    private PowerModeProfile? _shown;
    private bool _subscribed;

    public PowerModeDetailsControl()
    {
        InitializeComponent();
        ApplyExpanded();
        Loaded += async (_, _) => await RefreshAsync();
        IsVisibleChanged += PowerModeDetailsControl_IsVisibleChanged;
    }

    private async void PowerModeDetailsControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Subscribe(IsVisible);
        if (IsVisible)
            await RefreshAsync();
    }

    private void Subscribe(bool subscribe)
    {
        if (subscribe == _subscribed)
            return;
        _subscribed = subscribe;

        if (subscribe)
        {
            _powerModeListener.Changed += PowerModeListener_Changed;
            _godModeController.PresetChanged += GodModeController_PresetChanged;
            _gpuOverclockController.Changed += GpuOverclockController_Changed;
            _fanProfileController.Applied += FanProfileController_Applied;
        }
        else
        {
            _powerModeListener.Changed -= PowerModeListener_Changed;
            _godModeController.PresetChanged -= GodModeController_PresetChanged;
            _gpuOverclockController.Changed -= GpuOverclockController_Changed;
            _fanProfileController.Applied -= FanProfileController_Applied;
        }
    }

    // The fan curve's result arrives after the mode change (and a revert even later): show whether it's in use.
    private void FanProfileController_Applied(object? sender, (PowerModeState Mode, FanCurveApplyResult Result) e) => QueueRefresh();

    private void PowerModeListener_Changed(object? sender, PowerModeListener.ChangedEventArgs e) => QueueRefresh();
    private void GodModeController_PresetChanged(object? sender, Guid e) => QueueRefresh();
    private void GpuOverclockController_Changed(object? sender, EventArgs e) => QueueRefresh();

    private async void QueueRefresh() => await _throttle.DispatchAsync(async () =>
    {
        await Dispatcher.InvokeAsync(async () =>
        {
            if (IsLoaded && IsVisible)
                await RefreshAsync();
        });
    });

    private async Task RefreshAsync()
    {
        try
        {
            if (!await _feature.IsSupportedAsync())
            {
                Visibility = Visibility.Collapsed;
                return;
            }

            var mode = await _feature.GetStateAsync();
            var profile = await _reader.GetProfileAsync(mode);
            var live = await PowerModeProfileReader.ReadLiveValuesAsync();

            var changes = PowerModeProfileReader.Compare(_shown, profile);
            var modeChanged = _shown is not null && _shown.Mode != profile.Mode;
            _shown = profile;

            Show(profile, changes, live, modeChanged);
            Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Power mode details refresh failed.", ex);
        }
    }

    private void Show(PowerModeProfile profile, IReadOnlyList<PowerModeValueChange> changes, IReadOnlyDictionary<PowerModeValueKind, int> live, bool modeChanged)
    {
        var modeName = profile.Mode.GetDisplayName();
        _header.Subtitle = profile.Mode == PowerModeState.GodMode
            ? Resource.PowerModeDetails_SourceCustom
            : string.Format(Resource.PowerModeDetails_SourceFirmware, modeName);

        _rows.Children.Clear();
        _rows.RowDefinitions.Clear();

        if (changes.Count == 0)
        {
            AddRow(string.Format(Resource.PowerModeDetails_NotProvided, modeName), string.Empty, false, null);
        }

        foreach (var change in changes)
        {
            var text = PowerModeDetailsFormat.Value(change, _settings.Store.TemperatureUnit);
            string? now = null;
            if (change.Current is { } expected && live.TryGetValue(change.Kind, out var actual) && actual != expected)
                now = string.Format(Resource.PowerModeDetails_Now, PowerModeDetailsFormat.Single(change.Kind, actual, _settings.Store.TemperatureUnit));

            AddRow(PowerModeDetailsFormat.Label(change.Kind), text, change.Changed, now);
        }

        ShowFanCurve(profile);
        _clocks.Text = profile.GpuClockOffsets is { } o && (o.CoreDeltaMhz != 0 || o.MemoryDeltaMhz != 0)
            ? string.Format(Resource.PowerModeDetails_GpuOffsets, PowerModeDetailsFormat.Signed(o.CoreDeltaMhz), PowerModeDetailsFormat.Signed(o.MemoryDeltaMhz))
            : Resource.PowerModeDetails_GpuOffsetsNone;

        if (modeChanged)
            Motion.Pulse(_rows);

        AutomationProperties.SetName(this, $"{Resource.PowerModeDetails_Title}: {modeName}");
    }

    private void AddRow(string label, string value, bool changed, string? now)
    {
        var row = _rows.RowDefinitions.Count;
        _rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var name = new TextBlock { Text = label, FontSize = 12, Margin = new(0, 2, 12, 2), TextWrapping = TextWrapping.Wrap };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        Grid.SetRow(name, row);
        _rows.Children.Add(name);

        var valuePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 2, 0, 2) };
        var valueText = new TextBlock { Text = value, FontSize = 12, FontWeight = changed ? FontWeights.SemiBold : FontWeights.Normal };
        valueText.SetResourceReference(TextBlock.ForegroundProperty, changed ? "SystemAccentColorPrimaryBrush" : "TextFillColorPrimaryBrush");
        valueText.SetResourceReference(TextBlock.FontFamilyProperty, "LltNumberFontFamily");
        valuePanel.Children.Add(valueText);

        if (now is not null)
        {
            var nowText = new TextBlock { Text = "  " + now, FontSize = 12 };
            nowText.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
            valuePanel.Children.Add(nowText);
        }

        Grid.SetRow(valuePanel, row);
        Grid.SetColumn(valuePanel, 1);
        _rows.Children.Add(valuePanel);
    }

    private void ShowFanCurve(PowerModeProfile profile)
    {
        _fanLabel.Text = profile.FanSource switch
        {
            FanCurveSource.CustomPreset => Resource.PowerModeDetails_FanCustom,
            FanCurveSource.UserCurve => Resource.PowerModeDetails_FanUser,
            FanCurveSource.UserCurvePending => Resource.PowerModeDetails_FanUserPending,
            FanCurveSource.UserCurveNotKept => Resource.PowerModeDetails_FanUserNotKept,
            _ => Resource.PowerModeDetails_FanFirmware,
        };

        _fanBars.Children.Clear();
        if (profile.FanTable is not { Length: > 0 } table)
        {
            _fanBars.Visibility = Visibility.Collapsed;
            return;
        }

        var heights = PowerModeDetailsFormat.BarHeights(table, 34);
        foreach (var height in heights)
        {
            var bar = new Rectangle { Width = 10, Height = height, Margin = new(0, 0, 3, 0), VerticalAlignment = VerticalAlignment.Bottom, RadiusX = 1.5, RadiusY = 1.5 };
            bar.SetResourceReference(Shape.FillProperty, "SystemAccentColorPrimaryBrush");
            _fanBars.Children.Add(bar);
        }

        _fanBars.Visibility = Visibility.Visible;
        AutomationProperties.SetName(_fanBars, string.Join(", ", table));
    }

    private async void FanCurvesButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new LenovoLegionToolkit.WPF.Windows.Dashboard.FanCurvesWindow { Owner = Window.GetWindow(this) };
        window.ShowDialog();
        await RefreshAsync();
    }

    private void ExpandButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.Store.PowerModeDetailsExpanded = !_settings.Store.PowerModeDetailsExpanded;
        _settings.SynchronizeStore();
        ApplyExpanded();
    }

    private void ApplyExpanded()
    {
        var expanded = _settings.Store.PowerModeDetailsExpanded;
        _body.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        _expandButton.Icon = expanded ? SymbolRegular.ChevronUp24 : SymbolRegular.ChevronDown24;
    }
}
