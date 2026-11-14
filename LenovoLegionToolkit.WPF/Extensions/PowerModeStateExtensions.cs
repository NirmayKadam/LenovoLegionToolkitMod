using System.Windows.Media;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.WPF.Utils;

namespace LenovoLegionToolkit.WPF.Extensions;

public static class PowerModeStateExtensions
{
    /// <summary>
    /// The color for a power mode in notifications and the tray status popup. Skins with their own colors
    /// (Neon, Retro) use their mode colors; Fluent keeps the app's original ones.
    /// </summary>
    public static SolidColorBrush GetSolidColorBrush(this PowerModeState powerModeState)
    {
        if (ThemeManager.ActivePalette is { Colors: not null } palette)
            return new(palette.ModeColor(powerModeState));

        return GetDefaultSolidColorBrush(powerModeState);
    }

    /// <summary>
    /// The app's original power-mode colors, used by Fluent.
    /// </summary>
    public static SolidColorBrush GetDefaultSolidColorBrush(this PowerModeState powerModeState) => new(powerModeState switch
    {
        PowerModeState.Quiet => Color.FromRgb(53, 123, 242),
        PowerModeState.Balance => Colors.White,
        PowerModeState.Performance => Color.FromRgb(212, 51, 51),
        PowerModeState.Extreme => Color.FromRgb(99, 52, 227),
        PowerModeState.GodMode => Color.FromRgb(99, 52, 227),
        _ => Colors.Transparent,
    });
}