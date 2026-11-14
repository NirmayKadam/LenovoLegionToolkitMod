using System;
using System.Linq;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using LenovoLegionToolkit.WPF.Utils.Skins;

namespace LenovoLegionToolkit.WPF.Controls.Dashboard;

/// <summary>
/// How the dashboard status strip turns sensor readings into text and bar lengths. The sensor controller
/// reports a missing reading as a negative number; those show as "—" and an empty bar, never as a guess.
/// </summary>
public static class StatusStripFormat
{
    public const string Unavailable = "—";

    public static bool IsAvailable(int value) => value >= 0;

    /// <summary>CPU temperature, in °C, above which the reading is shown in the skin's "hot" color.</summary>
    public const int HotCpuCelsius = 78;

    public static bool IsHotCpu(int celsius) => celsius > HotCpuCelsius;

    /// <summary>
    /// How full a bar should be, or null when the reading or its maximum is unknown.
    /// </summary>
    public static double? Fraction(int value, int max)
    {
        if (value < 0 || max <= 0)
            return null;
        return Math.Clamp((double)value / max, 0, 1);
    }

    public static double ToDisplayTemperature(int celsius, TemperatureUnit unit) =>
        unit == TemperatureUnit.F ? celsius * 9.0 / 5.0 + 32.0 : celsius;

    public static string TemperatureSuffix(TemperatureUnit unit) =>
        unit == TemperatureUnit.F ? Resource.Fahrenheit : Resource.Celsius;

    public static string? Percent(int value) => IsAvailable(value) ? $"{value}{Resource.Percent}" : null;

    /// <summary>
    /// A clock reading in MHz shown as GHz. Zero is treated as missing: no running processor reports it.
    /// </summary>
    public static string? Gigahertz(int megahertz) => megahertz > 0 ? Motion.FormatNumber(megahertz / 1000.0, 1, " " + Resource.GHz) : null;

    public static string? Megahertz(int megahertz) => megahertz > 0 ? $"{megahertz} {Resource.MHz}" : null;

    public static string? Rpm(int rpm) => IsAvailable(rpm) ? $"{rpm} {Resource.RPM}" : null;

    /// <summary>
    /// Joins the readings that exist with " · "; when none do, the line is "—".
    /// </summary>
    public static string JoinAvailable(params string?[] parts)
    {
        var present = parts.Where(p => !string.IsNullOrEmpty(p)).ToArray();
        return present.Length == 0 ? Unavailable : string.Join(" · ", present);
    }

    public static string AdapterText(PowerAdapterStatus status) => status switch
    {
        PowerAdapterStatus.Connected => Resource.StatusStrip_PluggedIn,
        PowerAdapterStatus.ConnectedLowWattage => Resource.StatusStrip_LowWattageAdapter,
        _ => Resource.StatusStrip_OnBattery,
    };

    /// <summary>
    /// The skin resource that colors a power mode. Extreme and Custom (God Mode) share the custom color, as
    /// they do in the app's notifications.
    /// </summary>
    public static string ModeBrushKey(PowerModeState state) => state switch
    {
        PowerModeState.Quiet => SkinKeys.ModeQuietBrush,
        PowerModeState.Balance => SkinKeys.ModeBalanceBrush,
        PowerModeState.Performance => SkinKeys.ModePerformanceBrush,
        _ => SkinKeys.ModeCustomBrush,
    };

    /// <summary>
    /// Where the selector's highlight sits: segment <paramref name="index"/> of <paramref name="count"/> equal
    /// segments across <paramref name="totalWidth"/>. Null when the current mode isn't one of the segments.
    /// </summary>
    public static (double X, double Width)? PillGeometry(int index, int count, double totalWidth)
    {
        if (count <= 0 || index < 0 || index >= count || !(totalWidth > 0))
            return null;
        var width = totalWidth / count;
        return (index * width, width);
    }
}
