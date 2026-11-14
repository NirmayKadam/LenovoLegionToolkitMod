using System;
using System.Linq;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.WPF.Resources;

namespace LenovoLegionToolkit.WPF.Controls.Dashboard;

/// <summary>Text and bar sizes for the power mode details card.</summary>
public static class PowerModeDetailsFormat
{
    /// <summary>The same names the God Mode window uses for each value.</summary>
    public static string Label(PowerModeValueKind kind) => kind switch
    {
        PowerModeValueKind.CpuLongTermPowerLimit => Resource.GodModeSettingsWindow_CPU_LongTermPL,
        PowerModeValueKind.CpuShortTermPowerLimit => Resource.GodModeSettingsWindow_CPU_ShortTermPL,
        PowerModeValueKind.CpuPeakPowerLimit => Resource.GodModeSettingsWindow_CPU_PeakPL,
        PowerModeValueKind.CpuCrossLoadingPowerLimit => Resource.GodModeSettingsWindow_CPU_CrossLoading,
        PowerModeValueKind.CpuPl2Tau => Resource.GodModeSettingsWindow_CPU_PL2Tau,
        PowerModeValueKind.ApuSpptPowerLimit => Resource.GodModeSettingsWindow_CPU_APUsPPT_PL,
        PowerModeValueKind.CpuTemperatureLimit => Resource.GodModeSettingsWindow_CPU_TempLimit,
        PowerModeValueKind.GpuConfigurableTgp => Resource.GodModeSettingsWindow_GPU_CTGP,
        PowerModeValueKind.GpuPowerBoost => Resource.GodModeSettingsWindow_GPU_DynamicBoost,
        PowerModeValueKind.GpuToCpuDynamicBoost => Resource.GodModeSettingsWindow_GPU_ToCpuDynamicBoostControl,
        PowerModeValueKind.GpuTemperatureLimit => Resource.GodModeSettingsWindow_GPU_TempLimit,
        _ => kind.ToString(),
    };

    public static bool IsTemperature(PowerModeValueKind kind) => kind is PowerModeValueKind.CpuTemperatureLimit or PowerModeValueKind.GpuTemperatureLimit;

    /// <summary>One value with its unit: "80 W", "28 s", "95 °C" (or °F).</summary>
    public static string Single(PowerModeValueKind kind, int value, TemperatureUnit unit)
    {
        if (IsTemperature(kind))
        {
            return unit == TemperatureUnit.F
                ? $"{Math.Round(value * 9.0 / 5.0 + 32.0):0} {Resource.Fahrenheit}"
                : $"{value} {Resource.Celsius}";
        }

        return kind == PowerModeValueKind.CpuPl2Tau ? $"{value} s" : $"{value} {Resource.Watt}";
    }

    /// <summary>"55 → 80 W" when it changed, "80 W" when not, "— → 80 W" / "55 W → —" when it appears or disappears.</summary>
    public static string Value(PowerModeValueChange change, TemperatureUnit unit)
    {
        string Text(int? v) => v is { } x ? Single(change.Kind, x, unit) : "—";

        if (!change.Changed)
            return Text(change.Current);

        return $"{Text(change.Previous)} → {Text(change.Current)}";
    }

    public static string Signed(int value) => value > 0 ? $"+{value}" : value.ToString();

    /// <summary>
    /// Bar heights for a fan table: each entry relative to the highest one, at least 3 px so a step is visible.
    /// </summary>
    public static double[] BarHeights(ushort[] table, double maxHeight)
    {
        if (table.Length == 0)
            return [];

        var max = Math.Max(1, (int)table.Max());
        return table.Select(v => Math.Max(3, Math.Round(maxHeight * v / max, 1))).ToArray();
    }
}
