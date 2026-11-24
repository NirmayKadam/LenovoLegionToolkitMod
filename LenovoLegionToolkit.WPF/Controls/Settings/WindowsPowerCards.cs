using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Controls.Custom;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using Wpf.Ui.Common;

namespace LenovoLegionToolkit.WPF.Controls.Settings;

/// <summary>Which Windows power setting LLT changes along with the power mode (Settings → Power → mapping).</summary>
public enum WindowsPowerSection
{
    /// <summary>LLT leaves Windows' power settings alone.</summary>
    None,

    /// <summary>A Windows power plan per mode (and, for plans based on Balanced, a power mode on top).</summary>
    PowerPlan,

    /// <summary>A Windows power mode per mode, on AC and on battery.</summary>
    PowerMode,
}

/// <summary>
/// The cards that choose the Windows power plan or Windows power mode for one of LLT's power modes, shared by
/// the Windows power plans window, the Windows power modes window and the Power mode profiles window. For the
/// built-in modes it also reads and saves those choices and applies them at once when the laptop is in that mode.
/// </summary>
public static class WindowsPowerCards
{
    public static WindowsPowerPlan DefaultPlan { get; } = new(Guid.Empty, Resource.WindowsPowerPlansWindow_DefaultPowerPlan, false);

    public static WindowsPowerSection SectionFor(PowerModeMappingMode mapping, bool overlaySupported) => mapping switch
    {
        PowerModeMappingMode.WindowsPowerPlan => WindowsPowerSection.PowerPlan,
        PowerModeMappingMode.WindowsPowerMode when overlaySupported => WindowsPowerSection.PowerMode,
        _ => WindowsPowerSection.None,
    };

    // ---- Cards ----

    /// <summary>
    /// The plan picker, plus the Windows power mode on AC and on battery, shown only for plans based on Balanced
    /// (the only plans Windows' power modes apply to).
    /// </summary>
    public static IReadOnlyList<UIElement> PlanCards(WindowsPowerPlan[] powerPlans, Guid savedPlan, WindowsPowerMode ac, WindowsPowerMode dc,
        Func<WindowsPowerPlan, Task> onPlanChanged, Func<WindowsPowerMode, bool, Task> onOverlayChanged)
    {
        var selected = powerPlans.FirstOrDefault(pp => pp.Guid == savedPlan);
        var effective = selected == default ? DefaultPlan : selected;

        var planCombo = new ComboBox { MinWidth = 200, MaxDropDownHeight = 300, VerticalAlignment = VerticalAlignment.Center };
        planCombo.SetItems(powerPlans, effective, pp => pp.Name);

        var planCard = new CardControl
        {
            Margin = new Thickness(0, 0, 0, 8),
            Icon = SymbolRegular.FlashSettings24,
            Header = new CardHeaderControl { Title = Resource.WindowsPowerPlan_Title, HelpKey = "Help_PowerMapping_Plan" },
            Content = planCombo,
        };

        var overlayVisibility = IsBalancedPlan(effective.Guid) ? Visibility.Visible : Visibility.Collapsed;
        var acCard = ModeCard(true, ac, onOverlayChanged);
        var dcCard = ModeCard(false, dc, onOverlayChanged);
        acCard.Visibility = overlayVisibility;
        dcCard.Visibility = overlayVisibility;

        planCombo.SelectionChanged += async (_, _) =>
        {
            if (!planCombo.TryGetSelectedItem(out WindowsPowerPlan plan))
                return;

            var visibility = IsBalancedPlan(plan.Guid) ? Visibility.Visible : Visibility.Collapsed;
            acCard.Visibility = visibility;
            dcCard.Visibility = visibility;
            await RunSafelyAsync(() => onPlanChanged(plan));
        };

        return [planCard, acCard, dcCard];
    }

    /// <summary>The Windows power mode on AC and on battery.</summary>
    public static IReadOnlyList<UIElement> ModeCards(WindowsPowerMode ac, WindowsPowerMode dc, Func<WindowsPowerMode, bool, Task> onChanged) =>
        [ModeCard(true, ac, onChanged), ModeCard(false, dc, onChanged)];

    private static CardControl ModeCard(bool isAc, WindowsPowerMode selected, Func<WindowsPowerMode, bool, Task> onChanged)
    {
        var combo = new ComboBox { MinWidth = 200, VerticalAlignment = VerticalAlignment.Center };
        combo.SetItems(Enum.GetValues<WindowsPowerMode>(), selected, pm => pm.GetDisplayName());
        combo.SelectionChanged += async (_, _) =>
        {
            if (combo.TryGetSelectedItem(out WindowsPowerMode mode))
                await RunSafelyAsync(() => onChanged(mode, isAc));
        };

        return new CardControl
        {
            Margin = new Thickness(0, 0, 0, 8),
            Icon = isAc ? SymbolRegular.PlugConnected24 : SymbolRegular.BatterySaver24,
            Header = new CardHeaderControl
            {
                Title = $"{Resource.WindowsPowerPlansWindow_PowerMode_Title} - {(isAc ? Resource.WindowsPowerPlansWindow_PowerMode_AC : Resource.WindowsPowerPlansWindow_PowerMode_DC)}",
                HelpKey = isAc ? "Help_PowerMapping_WindowsModeAc" : "Help_PowerMapping_WindowsModeDc",
            },
            Content = combo,
        };
    }

    public static bool IsBalancedPlan(Guid guid) => PowerPlanExtensions.IsPlanBasedOnBalanced(guid);

    /// <summary>A choice handler runs from a combo box event: a failure is logged, never thrown into WPF.</summary>
    private static async Task RunSafelyAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Changing a Windows power setting failed.", ex);
        }
    }

    // ---- The built-in modes' saved choices ----

    public static Guid SavedPlan(ApplicationSettings settings, PowerModeState state) =>
        settings.Store.PowerPlans.TryGetValue(state, out var guid) ? guid : Guid.Empty;

    public static WindowsPowerMode SavedOverlay(ApplicationSettings settings, PowerModeState state, bool isAc) =>
        (isAc ? settings.Store.Overrides.GetPowerPlanBalanceOnAc(state) : settings.Store.Overrides.GetPowerPlanBalanceOnDc(state)) ?? WindowsPowerMode.Balanced;

    public static WindowsPowerMode SavedMode(ApplicationSettings settings, PowerModeState state, bool isAc) =>
        (isAc ? settings.Store.Overrides.GetPowerModeOnAc(state) : settings.Store.Overrides.GetPowerModeOnDc(state))
        ?? settings.Store.PowerModes.GetValueOrDefault(state, WindowsPowerMode.Balanced);

    public static Task SavePlanAsync(ApplicationSettings settings, PowerModeFeature feature, PowerModeState state, WindowsPowerPlan plan)
    {
        settings.Store.PowerPlans[state] = plan.Guid;
        return SaveAndApplyAsync(settings, feature, state);
    }

    public static Task SaveOverlayAsync(ApplicationSettings settings, PowerModeFeature feature, PowerModeState state, WindowsPowerMode mode, bool isAc)
    {
        if (isAc)
            settings.Store.Overrides.SetPowerPlanBalanceOnAc(state, mode);
        else
            settings.Store.Overrides.SetPowerPlanBalanceOnDc(state, mode);
        return SaveAndApplyAsync(settings, feature, state);
    }

    public static Task SaveModeAsync(ApplicationSettings settings, PowerModeFeature feature, PowerModeState state, WindowsPowerMode mode, bool isAc)
    {
        if (isAc)
            settings.Store.Overrides.SetPowerModeOnAc(state, mode);
        else
            settings.Store.Overrides.SetPowerModeOnDc(state, mode);
        return SaveAndApplyAsync(settings, feature, state);
    }

    /// <summary>Saves, and applies at once if the laptop is in that mode (otherwise at the next switch to it).</summary>
    private static async Task SaveAndApplyAsync(ApplicationSettings settings, PowerModeFeature feature, PowerModeState state)
    {
        settings.SynchronizeStore();

        if (await feature.GetStateAsync() == state)
            await feature.EnsureCorrectWindowsPowerSettingsAreSetAsync(skipThrottle: true);
    }
}
