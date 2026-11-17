using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Controls;
using LenovoLegionToolkit.WPF.Controls.Custom;
using LenovoLegionToolkit.WPF.Controls.Settings;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using Wpf.Ui.Common;
using Button = Wpf.Ui.Controls.Button;

namespace LenovoLegionToolkit.WPF.Windows.Dashboard;

/// <summary>
/// Everything a power mode brings, in one place: its fan curve and the Windows power plan or power mode LLT
/// switches to with it. It edits the same settings as the Fan curves window and the Windows power plan/mode
/// windows, through the same paths; it writes nothing to the firmware itself. "Apply now" applies the shown
/// mode's settings again, only when the laptop is in that mode.
/// </summary>
public partial class PowerModeProfilesWindow
{
    private readonly PowerModeFeature _powerModeFeature = IoCContainer.Resolve<PowerModeFeature>();
    private readonly FanProfileController _fanProfileController = IoCContainer.Resolve<FanProfileController>();
    private readonly FanProfileSettings _fanSettings = IoCContainer.Resolve<FanProfileSettings>();
    private readonly WindowsPowerPlanController _windowsPowerPlanController = IoCContainer.Resolve<WindowsPowerPlanController>();
    private readonly ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();

    /// <summary>The mode the laptop was in at the last refresh.</summary>
    private PowerModeState? _activeMode;

    private WindowsPowerSection _section;

    /// <summary>Apply now or a fan reset is running; buttons stay off meanwhile.</summary>
    private bool _busy;

    public PowerModeProfilesWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private PowerModeState? SelectedMode => (_tabControl.SelectedItem as TabItem)?.Tag is PowerModeState mode ? mode : null;

    private async Task RefreshAsync(PowerModeState? select = null)
    {
        _loader.IsLoading = true;
        try
        {
            var modes = await _powerModeFeature.GetAllStatesAsync();
            _activeMode = await _powerModeFeature.GetStateAsync();
            _section = WindowsPowerCards.SectionFor(_settings.Store.PowerModeMappingMode, WindowsPowerModeController.IsOverlaySupported);
            WindowsPowerPlan[] plans = _section == WindowsPowerSection.PowerPlan
                ? _windowsPowerPlanController.GetPowerPlans().OrderBy(x => x.Name).Prepend(WindowsPowerCards.DefaultPlan).ToArray()
                : [];

            _tabControl.Items.Clear();
            foreach (var mode in modes)
            {
                UIElement content = mode == PowerModeState.GodMode ? BuildCustomTab() : BuildModeTab(mode, plans);
                _tabControl.Items.Add(new TabItem { Header = mode.GetDisplayName(), Tag = mode, Content = content });
            }

            var wanted = select ?? _activeMode;
            _tabControl.SelectedItem = _tabControl.Items.Cast<TabItem>().FirstOrDefault(t => t.Tag is PowerModeState m && m == wanted)
                                       ?? _tabControl.Items.Cast<TabItem>().FirstOrDefault();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Power mode profiles failed to load.", ex);
            _statusText.Text = Resource.PowerModeProfiles_LoadFailed;
        }
        finally
        {
            _loader.IsLoading = false;
            UpdateApplyNow();
        }
    }

    private StackPanel BuildModeTab(PowerModeState mode, WindowsPowerPlan[] plans)
    {
        var panel = new StackPanel();
        panel.Children.Add(BuildFanCard(mode));

        IReadOnlyList<UIElement> cards = _section switch
        {
            WindowsPowerSection.PowerPlan => WindowsPowerCards.PlanCards(plans,
                WindowsPowerCards.SavedPlan(_settings, mode),
                WindowsPowerCards.SavedOverlay(_settings, mode, isAc: true),
                WindowsPowerCards.SavedOverlay(_settings, mode, isAc: false),
                plan => WindowsPowerCards.SavePlanAsync(_settings, _powerModeFeature, mode, plan),
                (windowsMode, isAc) => WindowsPowerCards.SaveOverlayAsync(_settings, _powerModeFeature, mode, windowsMode, isAc)),
            WindowsPowerSection.PowerMode => WindowsPowerCards.ModeCards(
                WindowsPowerCards.SavedMode(_settings, mode, isAc: true),
                WindowsPowerCards.SavedMode(_settings, mode, isAc: false),
                (windowsMode, isAc) => WindowsPowerCards.SaveModeAsync(_settings, _powerModeFeature, mode, windowsMode, isAc)),
            _ => [InfoCard(SymbolRegular.Flash24, new CardHeaderControl { Title = Resource.PowerModeProfiles_WindowsOff_Title, Subtitle = Resource.PowerModeProfiles_WindowsOff_Message, HelpKey = "Help_PowerModeProfiles_WindowsOff" }, null)],
        };

        foreach (var card in cards)
            panel.Children.Add(card);

        return panel;
    }

    /// <summary>The mode's fan curve: whose it is, editing it, and going back to the firmware's.</summary>
    private CardControl BuildFanCard(PowerModeState mode)
    {
        var hasCurve = _fanSettings.Store.Curves.ContainsKey(mode);
        var subtitle = hasCurve && !_fanSettings.Store.Enabled
            ? Resource.PowerModeProfiles_Fan_Off
            : _fanProfileController.CurveInUse(mode).Source switch
            {
                FanCurveSource.UserCurve => Resource.PowerModeProfiles_Fan_User,
                FanCurveSource.UserCurvePending => Resource.PowerModeProfiles_Fan_Pending,
                FanCurveSource.UserCurveNotKept => Resource.PowerModeProfiles_Fan_NotKept,
                _ => Resource.PowerModeProfiles_Fan_Firmware,
            };

        var edit = new Button { Appearance = ControlAppearance.Secondary, Content = Resource.PowerModeProfiles_Fan_Edit, Margin = new Thickness(0, 0, 8, 0) };
        edit.Click += async (_, _) =>
        {
            new FanCurvesWindow(mode) { Owner = this }.ShowDialog();
            await RefreshAsync(mode);
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(edit);

        if (hasCurve)
        {
            var reset = new Button { Appearance = ControlAppearance.Secondary, Content = Resource.FanCurvesWindow_Reset_One };
            reset.Click += async (_, _) => await ResetFanCurveAsync(mode);
            buttons.Children.Add(reset);
        }

        return InfoCard(SymbolRegular.WeatherDuststorm24, new CardHeaderControl { Title = Resource.PowerModeProfiles_Fan_Title, Subtitle = subtitle, HelpKey = "Help_PowerModeProfiles_Fan" }, buttons);
    }

    private CardControl BuildCustomTab()
    {
        var open = new Button { Appearance = ControlAppearance.Secondary, Content = Resource.PowerModeProfiles_Custom_Open };
        open.Click += async (_, _) =>
        {
            new GodModeSettingsWindow { Owner = this }.ShowDialog();
            await RefreshAsync(PowerModeState.GodMode);
        };

        return InfoCard(SymbolRegular.WrenchScrewdriver24, new CardHeaderControl { Title = Resource.PowerModeProfiles_Custom_Title, Subtitle = Resource.PowerModeProfiles_Custom_Message, HelpKey = "Help_PowerModeProfiles_Custom" }, open);
    }

    private static CardControl InfoCard(SymbolRegular icon, CardHeaderControl header, UIElement? content) => new()
    {
        Margin = new Thickness(0, 0, 0, 8),
        Icon = icon,
        Header = header,
        Content = content,
    };

    private async Task ResetFanCurveAsync(PowerModeState mode)
    {
        if (_busy || !await MessageBoxHelper.ShowAsync(this, Resource.FanCurvesWindow_Reset_Title, string.Format(Resource.FanCurvesWindow_Reset_ConfirmOne, mode.GetDisplayName()), Resource.Yes, Resource.No))
            return;

        await RunBusyAsync(Resource.FanCurvesWindow_Status_Restoring, async () =>
        {
            var result = await Task.Run(() => _fanProfileController.ResetToFirmwareAsync(mode, _powerModeFeature.SetStateAsync));
            await RefreshAsync(mode);
            _statusText.Text = FanCurvesWindow.ResetResultText(result);
        });
    }

    private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source == _tabControl)
            UpdateApplyNow();
    }

    /// <summary>Apply now only for the mode the laptop is in, and not for Custom (God Mode applies its own preset).</summary>
    private void UpdateApplyNow()
    {
        var selected = SelectedMode;
        var isActive = selected is { } mode && mode == _activeMode && mode != PowerModeState.GodMode;

        _applyNowButton.IsEnabled = !_busy && isActive;
        _applyNowButton.ToolTip = isActive || _activeMode is not { } active
            ? Resource.PowerModeProfiles_ApplyNow_Tooltip
            : string.Format(Resource.PowerModeProfiles_ApplyNow_OnlyActive, active.GetDisplayName());
    }

    /// <summary>Applies the active mode's fan curve and Windows power setting again, the way a mode change does.</summary>
    private async void ApplyNowButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedMode is not { } mode || mode != _activeMode)
            return;

        await RunBusyAsync(Resource.PowerModeProfiles_Applying, async () =>
        {
            var fan = await Task.Run(() => _fanProfileController.ApplyForModeAsync(mode));
            await Task.Run(() => _powerModeFeature.EnsureCorrectWindowsPowerSettingsAreSetAsync());

            var fanText = fan switch
            {
                FanCurveApplyResult.Applied => Resource.PowerModeProfiles_Applied_FanUser,
                FanCurveApplyResult.NotApplicable or FanCurveApplyResult.SkippedUnsupported => Resource.PowerModeProfiles_Applied_FanFirmware,
                FanCurveApplyResult.Rejected or FanCurveApplyResult.Invalid => Resource.PowerModeProfiles_Applied_FanNotKept,
                _ => null,
            };

            _statusText.Text = fanText is null
                ? Resource.PowerModeProfiles_ApplyFailed
                : _section == WindowsPowerSection.None ? fanText : $"{fanText} {Resource.PowerModeProfiles_Applied_Windows}";
        });
    }

    private async Task RunBusyAsync(string status, Func<Task> action)
    {
        _busy = true;
        UpdateApplyNow();
        _statusText.Text = status;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Power mode profiles action failed.", ex);
            _statusText.Text = Resource.PowerModeProfiles_ApplyFailed;
        }
        finally
        {
            _busy = false;
            UpdateApplyNow();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
