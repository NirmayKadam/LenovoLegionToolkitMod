using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Controllers.GodMode;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;

namespace LenovoLegionToolkit.WPF.Windows.Dashboard;

/// <summary>
/// Fan curves for the built-in power modes. Uses the same curve editor as Custom mode. Whether the firmware keeps
/// a curve outside Custom mode is found out by testing (with the user's consent) and shown here; if it doesn't,
/// a Custom preset with that mode's limits and the curve can be created instead.
/// </summary>
public partial class FanCurvesWindow
{
    private readonly FanProfileSettings _settings = IoCContainer.Resolve<FanProfileSettings>();
    private readonly FanProfileController _controller = IoCContainer.Resolve<FanProfileController>();
    private readonly GodModeController _godModeController = IoCContainer.Resolve<GodModeController>();
    private readonly PowerModeFeature _powerModeFeature = IoCContainer.Resolve<PowerModeFeature>();

    private Control? _editor;
    private FanTableData[]? _tableData;
    private FanTable _minimum;
    private bool _loading = true;

    /// <summary>An action (save, test, apply now, on/off) is running; the controls are disabled meanwhile.</summary>
    private bool _busy;

    /// <summary>The power mode the laptop was in at the last check.</summary>
    private PowerModeState? _activeMode;

    /// <summary>The mode an unsaved curve was tried on with Apply now; put back to what's saved when the window closes.</summary>
    private PowerModeState? _previewMode;

    private enum StatusKind { Neutral, Success, Warning }

    private enum FanAction { Save, Test, Preview }

    public FanCurvesWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();

        _controller.Applied += Controller_Applied;
        Closed += (_, _) =>
        {
            _controller.Applied -= Controller_Applied;
            if (_previewMode is { } previewed)
                _ = Task.Run(() => PutSavedCurveBackAsync(previewed));
        };
    }

    private PowerModeState? SelectedMode => _modeComboBox.TryGetSelectedItem(out PowerModeState mode) ? mode : null;

    private async Task LoadAsync()
    {
        _loading = true;
        try
        {
            var modes = (await _powerModeFeature.GetAllStatesAsync()).Where(m => m != PowerModeState.GodMode).ToArray();
            var current = await _powerModeFeature.GetStateAsync();
            _modeComboBox.SetItems(modes, modes.Contains(current) ? current : modes.First(), m => m.GetDisplayName());

            _enabledToggle.IsChecked = _settings.Store.Enabled;

            // The editor needs the firmware's temperature steps, which come with the God Mode fan data.
            var state = await _godModeController.GetStateAsync();
            _tableData = state.Presets.TryGetValue(state.ActivePresetId, out var preset) ? preset.FanTableInfo?.Data : null;
            _minimum = await _godModeController.GetMinimumFanTableAsync();

            var mi = await Compatibility.GetMachineInformationAsync();
            _editor = mi.SmartFanVersion >= 6 ? new Controls.FanCurveControlV2() : new Controls.FanCurveControl();
            _editorHost.Content = _editor;

            ShowSupport();
            ShowMode();
            await UpdateApplyNowAsync();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Fan curves window failed to load.", ex);
            _noDataText.Visibility = Visibility.Visible;
            _applyNowButton.IsEnabled = false;
        }
        finally
        {
            _loading = false;
        }
    }

    private bool HasEditor => _tableData is not null && _editor is not null;

    /// <summary>
    /// Runs one action with the window locked: every control disabled, a progress ring and a status line, so
    /// nothing else can start until it ends.
    /// </summary>
    private async Task RunBusyAsync(string status, Func<Task> action)
    {
        if (_busy)
            return;

        _busy = true;
        SetControlsEnabled(false);
        _busyRing.IsIndeterminate = true;
        _busyRing.Visibility = Visibility.Visible;
        SetStatus(status, StatusKind.Neutral);
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Fan curves action failed.", ex);
            SetStatus(Resource.FanCurvesWindow_Result_Failed, StatusKind.Warning);
        }
        finally
        {
            _busy = false;
            _busyRing.IsIndeterminate = false;   // a hidden spinning ring keeps animating
            _busyRing.Visibility = Visibility.Collapsed;
            SetControlsEnabled(true);
            ShowSupport();
            await UpdateApplyNowAsync();
        }
    }

    private void SetControlsEnabled(bool enabled)
    {
        _enabledToggle.IsEnabled = enabled;
        _testButton.IsEnabled = enabled;
        _modeComboBox.IsEnabled = enabled;
        _useCurveCheckBox.IsEnabled = enabled && HasEditor;
        _editorHost.IsEnabled = enabled;
        _fallbackPanel.IsEnabled = enabled;
        _saveButton.IsEnabled = enabled;
        _applyNowButton.IsEnabled = enabled && _applyNowButton.IsEnabled;
    }

    private void SetStatus(string text, StatusKind kind)
    {
        _statusText.Text = text;
        _statusText.SetResourceReference(ForegroundProperty, kind switch
        {
            StatusKind.Success => "SystemFillColorSuccessBrush",
            StatusKind.Warning => "SystemFillColorCautionBrush",
            _ => "TextFillColorSecondaryBrush",
        });
    }

    /// <summary>
    /// Apply now only makes sense for the mode the laptop is in (the controller refuses others anyway), and not
    /// on a laptop known to put its own curve back. The tooltip says why when it's off.
    /// </summary>
    private async Task UpdateApplyNowAsync()
    {
        try
        {
            _activeMode = await _powerModeFeature.GetStateAsync();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't read the power mode.", ex);
            _activeMode = null;
        }

        if (_busy)
            return;

        var rejects = _settings.Store.FirmwareSupport == FanCurveFirmwareSupport.Rejects;
        var isActive = SelectedMode is { } selected && selected == _activeMode;

        _applyNowButton.IsEnabled = HasEditor && isActive && !rejects;
        _applyNowButton.ToolTip = rejects
            ? Resource.FanCurvesWindow_Support_Rejects
            : !isActive && _activeMode is { } active && active != PowerModeState.GodMode
                ? string.Format(Resource.FanCurvesWindow_ApplyNow_OnlyActive, active.GetDisplayName())
                : Resource.FanCurvesWindow_ApplyNow_Tooltip;
    }

    /// <summary>The saved curve for the selected mode, or the editor's starting curve when none is saved.</summary>
    private ushort[] SavedOrDefaultCurve(PowerModeState mode) =>
        _settings.Store.Curves.TryGetValue(mode, out var saved) && saved is { Length: 10 } ? saved : [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

    private bool HasUnsavedEdits() =>
        SelectedMode is { } mode && EditorCurve() is { } curve && !curve.SequenceEqual(SavedOrDefaultCurve(mode));

    /// <summary>The background check found that the firmware undid a curve; say so if nothing else is running.</summary>
    private void Controller_Applied(object? sender, (PowerModeState Mode, FanCurveApplyResult Result) e)
    {
        if (e.Result != FanCurveApplyResult.Rejected)
            return;

        Dispatcher.InvokeAsync(() =>
        {
            if (!_busy)
                SetStatus(string.Format(Resource.FanCurvesWindow_Result_RevertedLater, e.Mode.GetDisplayName()), StatusKind.Warning);
        });
    }

    /// <summary>
    /// After Apply now without Save: the fans get what is saved for that mode again, i.e. the saved curve, or the
    /// firmware's curve (by re-applying the power mode) when there is none. Nothing happens if the laptop has
    /// left that mode, since the new mode brought its own curve.
    /// </summary>
    private async Task PutSavedCurveBackAsync(PowerModeState mode)
    {
        try
        {
            var result = await _controller.ApplyForModeAsync(mode);
            if (result is FanCurveApplyResult.NotApplicable or FanCurveApplyResult.SkippedUnsupported
                && await _powerModeFeature.GetStateAsync() == mode)
            {
                await _powerModeFeature.SetStateAsync(mode);
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't put the saved fan curve back after Apply now.", ex);
        }
    }

    private void ShowSupport()
    {
        var support = _settings.Store.FirmwareSupport;
        _supportHeader.Subtitle = support switch
        {
            FanCurveFirmwareSupport.Accepts => Resource.FanCurvesWindow_Support_Accepts,
            FanCurveFirmwareSupport.Rejects => Resource.FanCurvesWindow_Support_Rejects,
            _ => Resource.FanCurvesWindow_Support_Unknown,
        };
        _fallbackPanel.Visibility = support == FanCurveFirmwareSupport.Rejects ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowMode()
    {
        if (SelectedMode is not { } mode)
            return;

        _useCurveCheckBox.Content = string.Format(Resource.FanCurvesWindow_UseCurve, mode.GetDisplayName());
        _useCurveCheckBox.IsChecked = _settings.Store.Curves.ContainsKey(mode);

        if (_tableData is null || _editor is null)
        {
            _noDataText.Visibility = Visibility.Visible;
            _editorHost.Visibility = Visibility.Collapsed;
            _useCurveCheckBox.IsEnabled = false;
            return;
        }

        var curve = _settings.Store.Curves.TryGetValue(mode, out var saved) && saved is { Length: 10 }
            ? new FanTable(saved)
            : new FanTable([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);

        var info = new FanTableInfo(_tableData, curve);
        switch (_editor)
        {
            case Controls.FanCurveControl v1: v1.SetFanTableInfo(info, _minimum); break;
            case Controls.FanCurveControlV2 v2: v2.SetFanTableInfo(info, _minimum); break;
        }
    }

    private ushort[]? EditorCurve() => (_editor switch
    {
        Controls.FanCurveControl v1 => v1.GetFanTableInfo(),
        Controls.FanCurveControlV2 v2 => v2.GetFanTableInfo(),
        _ => null,
    })?.Table.GetTable();

    private async void ModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
            return;

        ShowMode();
        await UpdateApplyNowAsync();
    }

    /// <summary>
    /// Takes effect at once: switching on applies the current mode's curve; switching off re-applies the power
    /// mode so the firmware's own curve comes back.
    /// </summary>
    private async void EnabledToggle_Click(object sender, RoutedEventArgs e)
    {
        var enabled = _enabledToggle.IsChecked == true;
        _settings.Store.Enabled = enabled;
        _settings.SynchronizeStore();

        await RunBusyAsync(enabled ? Resource.FanCurvesWindow_Status_Applying : Resource.FanCurvesWindow_Status_Restoring, async () =>
        {
            SetStatus(string.Empty, StatusKind.Neutral);

            var current = await _powerModeFeature.GetStateAsync();
            if (current == PowerModeState.GodMode || !_settings.Store.Curves.ContainsKey(current))
                return;

            if (enabled)
            {
                ShowResult(await _controller.ApplyForModeAsync(current), FanAction.Save);
            }
            else
            {
                await _powerModeFeature.SetStateAsync(current);
                _previewMode = null;
            }
        });
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedMode is not { } mode)
            return;

        await RunBusyAsync(Resource.FanCurvesWindow_Status_Applying, async () =>
        {
            var curve = EditorCurve();
            var hadCurve = _settings.Store.Curves.ContainsKey(mode);

            if (_useCurveCheckBox.IsChecked == true && curve is { Length: 10 })
            {
                if (!await _godModeController.IsValidFanTableAsync(new FanTable(curve)))
                {
                    SetStatus(Resource.FanCurvesWindow_Invalid, StatusKind.Warning);
                    return;
                }
                _settings.Store.Curves[mode] = curve;
            }
            else
            {
                _settings.Store.Curves.Remove(mode);
            }

            _settings.SynchronizeStore();
            if (_previewMode == mode)
                _previewMode = null;   // what was tried is now what's saved, or deliberately replaced

            // Takes effect now if this is the active mode: apply the curve, or bring the firmware's curve back
            // by re-applying the power mode.
            if (await _powerModeFeature.GetStateAsync() != mode)
            {
                SetStatus(string.Format(Resource.FanCurvesWindow_Result_SavedNotActive, mode.GetDisplayName()), StatusKind.Success);
                return;
            }

            if (_settings.Store.Curves.ContainsKey(mode) && _settings.Store.Enabled)
            {
                ShowResult(await _controller.ApplyForModeAsync(mode), FanAction.Save);
            }
            else if (hadCurve)
            {
                SetStatus(Resource.FanCurvesWindow_Status_Restoring, StatusKind.Neutral);
                await _powerModeFeature.SetStateAsync(mode);
                SetStatus(Resource.FanCurvesWindow_Result_Saved, StatusKind.Success);
            }
            else
            {
                SetStatus(Resource.FanCurvesWindow_Result_Saved, StatusKind.Success);
            }
        });
    }

    /// <summary>
    /// Asks first, then writes the active mode's curve (as shown in the editor) and checks whether the firmware
    /// keeps it. The fans go back to the curve they had before, unless this is the saved curve and it was kept.
    /// </summary>
    private async void TestButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        PowerModeState current;
        try
        {
            current = await _powerModeFeature.GetStateAsync();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't read the power mode.", ex);
            SetStatus(Resource.FanCurvesWindow_Result_Failed, StatusKind.Warning);
            return;
        }

        if (current == PowerModeState.GodMode)
        {
            SetStatus(Resource.FanCurvesWindow_Test_NotInCustom, StatusKind.Warning);
            return;
        }

        // The test is about the mode the laptop is in, so it uses that mode's curve, not whatever is selected.
        var otherModeSelected = SelectedMode is { } selected && selected != current;
        var message = string.Format(Resource.FanCurvesWindow_Test_Confirm, current.GetDisplayName());
        if (otherModeSelected && HasUnsavedEdits())
            message += Environment.NewLine + Environment.NewLine + string.Format(Resource.FanCurvesWindow_Test_DiscardEdits, SelectedMode!.Value.GetDisplayName());

        if (!await MessageBoxHelper.ShowAsync(this, Resource.FanCurvesWindow_Test, message, Resource.Yes, Resource.No))
            return;

        if (otherModeSelected)
            _modeComboBox.SelectItem(current);   // shows the active mode's curve in the editor

        if (EditorCurve() is not { Length: 10 } curve)
            return;

        await RunBusyAsync(Resource.FanCurvesWindow_Status_Testing, async () =>
        {
            var probe = await _controller.ProbeAsync(current, curve);
            ShowResult(probe.Result, FanAction.Test);

            if (probe.NeedsModeReapply)
            {
                var resultText = _statusText.Text;
                SetStatus(Resource.FanCurvesWindow_Status_Restoring, StatusKind.Neutral);
                await _powerModeFeature.SetStateAsync(current);
                ShowResult(probe.Result, FanAction.Test);
                Log.Instance.Trace($"Fan curve test: restored the firmware curve by re-applying {current}. [{resultText}]");
            }
        });
    }

    /// <summary>Writes the curve in the editor to the fans now, without saving it.</summary>
    private async void ApplyNowButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedMode is not { } mode || EditorCurve() is not { Length: 10 } curve)
            return;

        await RunBusyAsync(Resource.FanCurvesWindow_Status_Applying, async () =>
        {
            var result = await _controller.PreviewAsync(mode, curve);
            if (result == FanCurveApplyResult.Applied)
                _previewMode = mode;
            ShowResult(result, FanAction.Preview);
        });
    }

    private void ShowResult(FanCurveApplyResult result, FanAction action)
    {
        var (text, kind) = result switch
        {
            FanCurveApplyResult.Applied => action switch
            {
                FanAction.Test => (Resource.FanCurvesWindow_Result_Applied, StatusKind.Success),
                FanAction.Preview => (Resource.FanCurvesWindow_Result_Previewed, StatusKind.Success),
                _ => (Resource.FanCurvesWindow_Result_AppliedNow, StatusKind.Success),
            },
            FanCurveApplyResult.Rejected => (Resource.FanCurvesWindow_Result_Rejected, StatusKind.Warning),
            FanCurveApplyResult.Invalid => (Resource.FanCurvesWindow_Invalid, StatusKind.Warning),
            FanCurveApplyResult.Superseded => (Resource.FanCurvesWindow_Result_Superseded, StatusKind.Warning),
            FanCurveApplyResult.Failed => (Resource.FanCurvesWindow_Result_Failed, StatusKind.Warning),
            FanCurveApplyResult.SkippedUnsupported => (Resource.FanCurvesWindow_Support_Rejects, StatusKind.Warning),
            _ => (Resource.FanCurvesWindow_Result_Saved, StatusKind.Success),
        };

        SetStatus(text, kind);
    }

    /// <summary>
    /// The Custom-mode method: a Custom preset with this mode's firmware limits and the curve in the editor,
    /// made from the active preset (so it stays within the laptop's allowed ranges).
    /// </summary>
    private async void CreatePresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedMode is not { } mode || EditorCurve() is not { Length: 10 } curve)
            return;

        try
        {
            var defaults = await _godModeController.GetDefaultsInOtherPowerModesAsync();
            if (!defaults.TryGetValue(mode, out var modeDefaults))
            {
                _supportHeader.Warning = string.Format(Resource.FanCurvesWindow_Fallback_NoDefaults, mode.GetDisplayName());
                return;
            }

            var state = await _godModeController.GetStateAsync();
            var template = state.Presets[state.ActivePresetId];
            var name = string.Format(Resource.FanCurvesWindow_Fallback_PresetName, mode.GetDisplayName());
            var preset = FanProfileController.BuildCustomPreset(template, modeDefaults, curve, name);

            // Replaces the preset made earlier for this mode, so repeated clicks don't pile up copies.
            var presets = new Dictionary<Guid, GodModePreset>(state.Presets);
            FanProfileController.UpsertFallbackPreset(presets, _settings.Store.FallbackPresetIds, mode, preset);
            await _godModeController.SetStateAsync(new GodModeState { ActivePresetId = state.ActivePresetId, Presets = new ReadOnlyDictionary<Guid, GodModePreset>(presets) });
            _settings.SynchronizeStore();

            _supportHeader.Success = string.Format(Resource.FanCurvesWindow_Fallback_Created, name);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't create the Custom preset.", ex);
            _supportHeader.Warning = ex.Message;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
