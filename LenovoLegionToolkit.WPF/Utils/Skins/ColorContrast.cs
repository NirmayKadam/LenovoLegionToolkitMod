using System;
using System.Windows.Media;

namespace LenovoLegionToolkit.WPF.Utils.Skins;

/// <summary>
/// WCAG 2.x contrast math, used to check that every skin's text is readable.
/// </summary>
public static class ColorContrast
{
    /// <summary>
    /// Blends a (possibly translucent) foreground over an opaque background, the way the renderer does.
    /// </summary>
    public static Color Composite(Color foreground, Color background)
    {
        var a = foreground.A / 255.0;
        return Color.FromRgb(
            Blend(foreground.R, background.R, a),
            Blend(foreground.G, background.G, a),
            Blend(foreground.B, background.B, a));

        static byte Blend(byte f, byte b, double a) => (byte)Math.Round(f * a + b * (1 - a));
    }

    /// <summary>
    /// Relative luminance of an opaque color, 0 (black) to 1 (white).
    /// </summary>
    public static double RelativeLuminance(Color color)
    {
        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);

        static double Channel(byte value)
        {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
    }

    /// <summary>
    /// Contrast ratio between two opaque colors, from 1 (none) to 21 (black on white).
    /// </summary>
    public static double Ratio(Color a, Color b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        var lighter = Math.Max(la, lb);
        var darker = Math.Min(la, lb);
        return (lighter + 0.05) / (darker + 0.05);
    }
}
