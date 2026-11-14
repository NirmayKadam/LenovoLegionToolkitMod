using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using LenovoLegionToolkit.Lib;

namespace LenovoLegionToolkit.WPF.Utils.Skins;

/// <summary>
/// Resource keys the app's own controls read. Every skin defines all of them.
/// </summary>
public static class SkinKeys
{
    public const string FontFamily = "LltFontFamily";
    public const string NumberFontFamily = "LltNumberFontFamily";
    public const string GlowColor = "LltGlowColor";
    public const string GlowOpacity = "LltGlowOpacity";
    public const string ModeQuietBrush = "LltModeQuietBrush";
    public const string ModeBalanceBrush = "LltModeBalanceBrush";
    public const string ModePerformanceBrush = "LltModePerformanceBrush";
    public const string ModeCustomBrush = "LltModeCustomBrush";
    public const string SelectionTextBrush = "LltSelectionTextBrush";
    public const string SelectionFillOpacity = "LltSelectionFillOpacity";

    /// <summary>Optional: the color a reading turns when it runs hot. Only skins that use it define it.</summary>
    public const string HotBrush = "LltHotBrush";

    public static readonly IReadOnlyList<string> All =
    [
        FontFamily, NumberFontFamily, GlowColor, GlowOpacity,
        ModeQuietBrush, ModeBalanceBrush, ModePerformanceBrush, ModeCustomBrush,
        SelectionTextBrush, SelectionFillOpacity
    ];

    /// <summary>
    /// WPF-UI keys that a skin with its own colors must override. These are the keys the app and
    /// WPF-UI's control templates use most; leaving one out would mix the skin with Fluent colors.
    /// </summary>
    public static readonly IReadOnlyList<string> RequiredWpfUiOverrides =
    [
        "ApplicationBackgroundColor", "ApplicationBackgroundBrush",
        "CardBackgroundFillColorDefault", "CardBackgroundFillColorDefaultBrush",
        "CardStrokeColorDefault", "CardStrokeColorDefaultBrush",
        "CardStrokeColorDefaultSolid", "CardStrokeColorDefaultSolidBrush",
        "ControlFillColorDefault", "ControlFillColorDefaultBrush",
        "ControlStrokeColorDefault", "ControlStrokeColorDefaultBrush",
        "ControlElevationBorderBrush",
        "DividerStrokeColorDefault", "DividerStrokeColorDefaultBrush",
        "TextFillColorPrimary", "TextFillColorPrimaryBrush",
        "TextFillColorSecondary", "TextFillColorSecondaryBrush",
        "TextFillColorTertiary", "TextFillColorTertiaryBrush",
        "TextFillColorDisabled", "TextFillColorDisabledBrush",
        "SystemFillColorSuccess", "SystemFillColorSuccessBrush",
        "SystemFillColorCaution", "SystemFillColorCautionBrush",
        "ControlCornerRadius"
    ];
}

/// <summary>
/// The colors a non-Fluent skin uses in place of WPF-UI's theme colors.
/// </summary>
public sealed class SkinColors
{
    public required Color ApplicationBackground { get; init; }
    public required Color CardBackground { get; init; }
    public required Color CardBackgroundSecondary { get; init; }
    public required Color CardStroke { get; init; }
    public required Color ControlFill { get; init; }
    public required Color ControlFillSecondary { get; init; }
    public required Color ControlFillTertiary { get; init; }
    public required Color ControlFillDisabled { get; init; }
    public required Color ControlFillInputActive { get; init; }
    public required Color ControlStroke { get; init; }
    public required Color ControlStrokeSecondary { get; init; }
    public required Color ControlStrongFill { get; init; }
    public required Color ControlStrongStroke { get; init; }
    public required Color SubtleFillSecondary { get; init; }
    public required Color SubtleFillTertiary { get; init; }
    public required Color Divider { get; init; }
    public required Color SolidBackgroundBase { get; init; }
    public required Color SolidBackgroundSecondary { get; init; }
    public required Color SolidBackgroundTertiary { get; init; }
    public required Color SolidBackgroundQuarternary { get; init; }
    public required Color TextPrimary { get; init; }
    public required Color TextSecondary { get; init; }
    public required Color TextTertiary { get; init; }
    public required Color TextDisabled { get; init; }
    public required Color Accent { get; init; }
    public required Color Success { get; init; }
    public required Color Caution { get; init; }
    public required Color Critical { get; init; }
    public required CornerRadius ControlCornerRadius { get; init; }
    public required CornerRadius PopupCornerRadius { get; init; }

    /// <summary>
    /// The WPF-UI color keys this skin overrides, paired with their values. Each one also gets its brush key.
    /// </summary>
    public IEnumerable<(string ColorKey, Color Value)> WpfUiColors()
    {
        yield return ("ApplicationBackgroundColor", ApplicationBackground);
        yield return ("SolidBackgroundFillColorBase", SolidBackgroundBase);
        yield return ("SolidBackgroundFillColorSecondary", SolidBackgroundSecondary);
        yield return ("SolidBackgroundFillColorTertiary", SolidBackgroundTertiary);
        yield return ("SolidBackgroundFillColorQuarternary", SolidBackgroundQuarternary);

        yield return ("CardBackgroundFillColorDefault", CardBackground);
        yield return ("CardBackgroundFillColorSecondary", CardBackgroundSecondary);
        yield return ("CardStrokeColorDefault", CardStroke);
        yield return ("CardStrokeColorDefaultSolid", ColorContrast.Composite(CardStroke, ApplicationBackground));

        yield return ("ControlFillColorDefault", ControlFill);
        yield return ("ControlFillColorSecondary", ControlFillSecondary);
        yield return ("ControlFillColorTertiary", ControlFillTertiary);
        yield return ("ControlFillColorDisabled", ControlFillDisabled);
        yield return ("ControlFillColorInputActive", ControlFillInputActive);
        yield return ("ControlStrokeColorDefault", ControlStroke);
        yield return ("ControlStrokeColorSecondary", ControlStrokeSecondary);
        yield return ("ControlStrongFillColorDefault", ControlStrongFill);
        yield return ("ControlStrongStrokeColorDefault", ControlStrongStroke);
        yield return ("SubtleFillColorSecondary", SubtleFillSecondary);
        yield return ("SubtleFillColorTertiary", SubtleFillTertiary);
        yield return ("DividerStrokeColorDefault", Divider);
        yield return ("LayerFillColorDefault", CardBackgroundSecondary);
        yield return ("LayerOnMicaBaseAltFillColorDefault", CardBackgroundSecondary);

        yield return ("TextFillColorPrimary", TextPrimary);
        yield return ("TextFillColorSecondary", TextSecondary);
        yield return ("TextFillColorTertiary", TextTertiary);
        yield return ("TextFillColorDisabled", TextDisabled);
        yield return ("TextPlaceholderColor", TextTertiary);

        yield return ("SystemFillColorSuccess", Success);
        yield return ("SystemFillColorCaution", Caution);
        yield return ("SystemFillColorCritical", Critical);
    }
}

/// <summary>
/// One UI skin: its fonts, glow, power-mode colors and, for skins other than Fluent, the colors that
/// replace WPF-UI's theme colors. <see cref="ToResourceDictionary"/> turns it into the dictionary that
/// <see cref="ThemeManager"/> merges after WPF-UI's own theme.
/// </summary>
public sealed class SkinPalette
{
    public required UiSkin Skin { get; init; }

    /// <summary>The skin has no light variant, so the Dark base theme is always used with it.</summary>
    public required bool DarkOnly { get; init; }

    /// <summary>The skin draws a solid background, so window backdrops (Mica and so on) and background images are not used.</summary>
    public required bool SolidBackground { get; init; }

    /// <summary>Colors that replace WPF-UI's; <c>null</c> keeps WPF-UI's own colors (Fluent).</summary>
    public SkinColors? Colors { get; init; }

    public required FontFamily FontFamily { get; init; }
    public required FontFamily NumberFontFamily { get; init; }
    public required Color GlowColor { get; init; }
    public required double GlowOpacity { get; init; }
    public required Color ModeQuiet { get; init; }
    public required Color ModeBalance { get; init; }
    public required Color ModePerformance { get; init; }
    public required Color ModeCustom { get; init; }

    /// <summary>
    /// How strongly the power selector's highlight is filled with the mode color: a light tint in Fluent and
    /// Neon, solid in Retro (where the text on it then switches to <see cref="SelectionText"/>).
    /// </summary>
    public required double SelectionFillOpacity { get; init; }

    /// <summary>Text color on a selected item.</summary>
    public required Color SelectionText { get; init; }

    /// <summary>Color for readings that run hot (see <see cref="SkinKeys.HotBrush"/>); null when the skin doesn't use one.</summary>
    public Color? HotColor { get; init; }

    /// <summary>The color this skin gives a power mode. Extreme and Custom share the custom color.</summary>
    public Color ModeColor(PowerModeState state) => state switch
    {
        PowerModeState.Quiet => ModeQuiet,
        PowerModeState.Balance => ModeBalance,
        PowerModeState.Performance => ModePerformance,
        _ => ModeCustom,
    };

    /// <summary>Skins users can pick in this build.</summary>
    public static IReadOnlyList<UiSkin> Available { get; } = [UiSkin.Fluent, UiSkin.Neon, UiSkin.Retro];

    /// <summary>
    /// The palette for a skin. Skins not available in this build fall back to Fluent, so a settings
    /// file written by a newer version never breaks the UI.
    /// </summary>
    public static SkinPalette For(UiSkin skin, bool isDark, SkinOptions options)
    {
        return skin switch
        {
            UiSkin.Neon => Neon(options),
            UiSkin.Retro => Retro(options.Phosphor),
            _ => Fluent(isDark),
        };
    }

    /// <summary>
    /// Glow strength for Neon: the user's 0–100 setting mapped to 0–0.9 opacity, and none at all when the
    /// GPU can't draw effects cheaply.
    /// </summary>
    public static double NeonGlowOpacity(int glow, bool effectsAllowed) =>
        effectsAllowed ? Math.Clamp(glow, 0, 100) / 100.0 * 0.9 : 0;

    /// <summary>
    /// Dark graphite with cyan data and color-coded power modes; a limited glow on the active elements.
    /// </summary>
    public static SkinPalette Neon(SkinOptions options) => new()
    {
        Skin = UiSkin.Neon,
        DarkOnly = true,
        SolidBackground = true,
        Colors = new SkinColors
        {
            ApplicationBackground = Rgb(0x0B0D12),
            CardBackground = Rgb(0x12151C),
            CardBackgroundSecondary = Rgb(0x0F1218),
            CardStroke = Rgb(0x1E222B),
            ControlFill = Rgb(0x171B23),
            ControlFillSecondary = Rgb(0x1C212B),
            ControlFillTertiary = Rgb(0x141820),
            ControlFillDisabled = Rgb(0x12151C),
            ControlFillInputActive = Rgb(0x0B0D12),
            ControlStroke = Rgb(0x262B36),
            ControlStrokeSecondary = Rgb(0x2E3440),
            ControlStrongFill = Rgb(0x9AA1AD),
            ControlStrongStroke = Rgb(0x9AA1AD),
            SubtleFillSecondary = Rgb(0x171B23),
            SubtleFillTertiary = Rgb(0x141820),
            Divider = Rgb(0x1E222B),
            SolidBackgroundBase = Rgb(0x0B0D12),
            SolidBackgroundSecondary = Rgb(0x0F1218),
            SolidBackgroundTertiary = Rgb(0x12151C),
            SolidBackgroundQuarternary = Rgb(0x171B23),
            TextPrimary = Rgb(0xE7E9EE),
            TextSecondary = Rgb(0x9AA1AD),
            TextTertiary = Rgb(0x7D8590),
            TextDisabled = Rgb(0x4A505C),
            Accent = Rgb(0x22D3EE),
            Success = Rgb(0x34D399),
            Caution = Rgb(0xFBBF24),
            Critical = Rgb(0xFF4D5E),
            ControlCornerRadius = new CornerRadius(8),
            PopupCornerRadius = new CornerRadius(10),
        },
        FontFamily = SystemFonts.MessageFontFamily,
        NumberFontFamily = new FontFamily("Bahnschrift, Segoe UI"),
        GlowColor = Rgb(0x22D3EE),
        GlowOpacity = NeonGlowOpacity(options.NeonGlow, options.EffectsAllowed),
        ModeQuiet = Rgb(0x4DA3FF),
        ModeBalance = Rgb(0xE7E9EE),
        ModePerformance = Rgb(0xFF4D5E),
        ModeCustom = Rgb(0xB57BFF),
        HotColor = Rgb(0xFF8A5B),
        SelectionFillOpacity = 0.22,
        SelectionText = Rgb(0xE7E9EE),
    };

    /// <summary>
    /// A monochrome CRT terminal: one phosphor color in a bright and a dim shade on a near-black screen,
    /// square corners, a monospaced font, and selections drawn in inverse video.
    /// </summary>
    public static SkinPalette Retro(RetroPhosphor phosphor)
    {
        // Bright text, dim text, screen. Amber's dim shade is a little brighter than in the mockup so it
        // still reaches 4.5:1 on cards.
        var (text, dim, screen) = phosphor switch
        {
            RetroPhosphor.Amber => (Rgb(0xFFB52E), Rgb(0xB8801F), Rgb(0x140B00)),
            RetroPhosphor.White => (Rgb(0xE6F2EA), Rgb(0x8B9A90), Rgb(0x0A0D0B)),
            _ => (Rgb(0x3DFF7A), Rgb(0x1E9E48), Rgb(0x031207)),
        };

        Color Glass(double amount) => Mix(screen, text, amount);

        var font = new FontFamily("Consolas, Lucida Console, Courier New");

        return new SkinPalette
        {
            Skin = UiSkin.Retro,
            DarkOnly = true,
            SolidBackground = true,
            Colors = new SkinColors
            {
                ApplicationBackground = screen,
                CardBackground = Glass(0.04),
                CardBackgroundSecondary = Glass(0.02),
                CardStroke = Glass(0.28),
                ControlFill = Glass(0.06),
                ControlFillSecondary = Glass(0.10),
                ControlFillTertiary = Glass(0.04),
                ControlFillDisabled = Glass(0.03),
                ControlFillInputActive = screen,
                ControlStroke = Glass(0.35),
                ControlStrokeSecondary = Glass(0.45),
                ControlStrongFill = dim,
                ControlStrongStroke = dim,
                SubtleFillSecondary = Glass(0.10),
                SubtleFillTertiary = Glass(0.06),
                Divider = Glass(0.20),
                SolidBackgroundBase = screen,
                SolidBackgroundSecondary = Glass(0.02),
                SolidBackgroundTertiary = Glass(0.04),
                SolidBackgroundQuarternary = Glass(0.07),
                TextPrimary = text,
                TextSecondary = dim,
                TextTertiary = dim,
                TextDisabled = Glass(0.30),
                Accent = text,
                Success = text,
                Caution = text,
                Critical = text,
                ControlCornerRadius = new CornerRadius(0),
                PopupCornerRadius = new CornerRadius(0),
            },
            FontFamily = font,
            NumberFontFamily = font,
            GlowColor = text,
            GlowOpacity = 0, // Retro's glow is the whole-screen phosphor bloom, handled by the main window.
            ModeQuiet = text,
            ModeBalance = text,
            ModePerformance = text,
            ModeCustom = text,
            SelectionFillOpacity = 1,
            SelectionText = screen,
        };
    }

    /// <summary>Blends <paramref name="from"/> toward <paramref name="to"/> by <paramref name="amount"/> (0–1).</summary>
    public static Color Mix(Color from, Color to, double amount)
    {
        byte Channel(byte a, byte b) => (byte)Math.Round(a + (b - a) * amount);
        return Color.FromRgb(Channel(from.R, to.R), Channel(from.G, to.G), Channel(from.B, to.B));
    }

    private static Color Rgb(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    /// <summary>
    /// Today's look: WPF-UI's own colors and the system font. The power-mode colors are the ones the
    /// app already uses for notifications (see <c>PowerModeStateExtensions</c>); Balance is white there,
    /// which would vanish on the light theme, so it follows the text color instead.
    /// </summary>
    public static SkinPalette Fluent(bool isDark) => new()
    {
        Skin = UiSkin.Fluent,
        DarkOnly = false,
        SolidBackground = false,
        Colors = null,
        FontFamily = SystemFonts.MessageFontFamily,
        NumberFontFamily = SystemFonts.MessageFontFamily,
        GlowColor = global::System.Windows.Media.Colors.Transparent,
        GlowOpacity = 0,
        ModeQuiet = Color.FromRgb(53, 123, 242),
        ModeBalance = isDark ? global::System.Windows.Media.Colors.White : Color.FromRgb(0x1A, 0x1A, 0x1A),
        ModePerformance = Color.FromRgb(212, 51, 51),
        ModeCustom = Color.FromRgb(99, 52, 227),
        SelectionFillOpacity = 0.22,
        SelectionText = isDark ? global::System.Windows.Media.Colors.White : Color.FromArgb(0xE4, 0, 0, 0),
    };

    public ResourceDictionary ToResourceDictionary()
    {
        var dictionary = new ResourceDictionary
        {
            [SkinKeys.FontFamily] = FontFamily,
            [SkinKeys.NumberFontFamily] = NumberFontFamily,
            [SkinKeys.GlowColor] = GlowColor,
            [SkinKeys.GlowOpacity] = GlowOpacity,
            [SkinKeys.ModeQuietBrush] = Frozen(ModeQuiet),
            [SkinKeys.ModeBalanceBrush] = Frozen(ModeBalance),
            [SkinKeys.ModePerformanceBrush] = Frozen(ModePerformance),
            [SkinKeys.ModeCustomBrush] = Frozen(ModeCustom),
            [SkinKeys.SelectionTextBrush] = Frozen(SelectionText),
            [SkinKeys.SelectionFillOpacity] = SelectionFillOpacity,
        };

        if (HotColor is { } hot)
            dictionary[SkinKeys.HotBrush] = Frozen(hot);

        if (Colors is null)
            return dictionary;

        foreach (var (colorKey, value) in Colors.WpfUiColors())
        {
            dictionary[colorKey] = value;
            dictionary[BrushKeyFor(colorKey)] = Frozen(value);
        }

        // WPF-UI draws control edges with gradient brushes tuned for Fluent; skins use flat strokes.
        dictionary["ControlElevationBorderBrush"] = Frozen(Colors.ControlStroke);
        dictionary["CircleElevationBorderBrush"] = Frozen(Colors.ControlStroke);
        dictionary["AccentControlElevationBorderBrush"] = Frozen(Colors.ControlStroke);

        dictionary["ControlCornerRadius"] = Colors.ControlCornerRadius;
        dictionary["OverlayCornerRadius"] = Colors.ControlCornerRadius;
        dictionary["PopupCornerRadius"] = Colors.PopupCornerRadius;

        return dictionary;
    }

    /// <summary>
    /// WPF-UI names brushes after their color plus "Brush", except the application background.
    /// </summary>
    public static string BrushKeyFor(string colorKey) => colorKey switch
    {
        "ApplicationBackgroundColor" => "ApplicationBackgroundBrush",
        _ => colorKey + "Brush"
    };

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// User settings and system facts a palette depends on.
/// </summary>
/// <param name="NeonGlow">Neon glow strength, 0–100.</param>
/// <param name="Phosphor">Retro phosphor color.</param>
/// <param name="EffectsAllowed">The GPU draws effects (render tier 2, hardware acceleration on).</param>
public sealed record SkinOptions(int NeonGlow, RetroPhosphor Phosphor, bool EffectsAllowed)
{
    public static SkinOptions Default { get; } = new(60, RetroPhosphor.Green, true);
}