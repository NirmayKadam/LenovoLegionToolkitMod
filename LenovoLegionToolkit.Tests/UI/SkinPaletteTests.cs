using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Utils.Skins;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;
using Xunit;

namespace LenovoLegionToolkit.Tests.UI;

public class SkinPaletteTests
{
    public static IEnumerable<object[]> AvailableSkins() =>
        SkinPalette.Available.SelectMany(skin => new[] { new object[] { skin, true }, new object[] { skin, false } });

    // A palette built here only to exercise the WPF-UI key mapping; the shipped palettes are checked
    // separately by the theory tests below.
    private static SkinColors MappingProbeColors() => new()
    {
        ApplicationBackground = Color.FromRgb(0x10, 0x11, 0x12),
        CardBackground = Color.FromRgb(0x20, 0x21, 0x22),
        CardBackgroundSecondary = Color.FromRgb(0x23, 0x24, 0x25),
        CardStroke = Color.FromArgb(0x80, 0x30, 0x31, 0x32),
        ControlFill = Color.FromRgb(0x40, 0x41, 0x42),
        ControlFillSecondary = Color.FromRgb(0x43, 0x44, 0x45),
        ControlFillTertiary = Color.FromRgb(0x46, 0x47, 0x48),
        ControlFillDisabled = Color.FromRgb(0x49, 0x4A, 0x4B),
        ControlFillInputActive = Color.FromRgb(0x4C, 0x4D, 0x4E),
        ControlStroke = Color.FromRgb(0x50, 0x51, 0x52),
        ControlStrokeSecondary = Color.FromRgb(0x53, 0x54, 0x55),
        ControlStrongFill = Color.FromRgb(0x56, 0x57, 0x58),
        ControlStrongStroke = Color.FromRgb(0x59, 0x5A, 0x5B),
        SubtleFillSecondary = Color.FromRgb(0x60, 0x61, 0x62),
        SubtleFillTertiary = Color.FromRgb(0x63, 0x64, 0x65),
        Divider = Color.FromRgb(0x66, 0x67, 0x68),
        SolidBackgroundBase = Color.FromRgb(0x70, 0x71, 0x72),
        SolidBackgroundSecondary = Color.FromRgb(0x73, 0x74, 0x75),
        SolidBackgroundTertiary = Color.FromRgb(0x76, 0x77, 0x78),
        SolidBackgroundQuarternary = Color.FromRgb(0x79, 0x7A, 0x7B),
        TextPrimary = Color.FromRgb(0xF0, 0xF1, 0xF2),
        TextSecondary = Color.FromRgb(0xE0, 0xE1, 0xE2),
        TextTertiary = Color.FromRgb(0xD0, 0xD1, 0xD2),
        TextDisabled = Color.FromRgb(0x80, 0x81, 0x82),
        Accent = Color.FromRgb(0x22, 0xD3, 0xEE),
        Success = Color.FromRgb(0x00, 0xC0, 0x00),
        Caution = Color.FromRgb(0xC0, 0xC0, 0x00),
        Critical = Color.FromRgb(0xC0, 0x00, 0x00),
        ControlCornerRadius = new CornerRadius(3),
        PopupCornerRadius = new CornerRadius(6),
    };

    private static SkinPalette ProbePalette()
    {
        var fluent = SkinPalette.Fluent(true);
        return new SkinPalette
        {
            Skin = UiSkin.Neon,
            DarkOnly = true,
            SolidBackground = true,
            Colors = MappingProbeColors(),
            FontFamily = fluent.FontFamily,
            NumberFontFamily = fluent.NumberFontFamily,
            GlowColor = fluent.GlowColor,
            GlowOpacity = fluent.GlowOpacity,
            ModeQuiet = fluent.ModeQuiet,
            ModeBalance = fluent.ModeBalance,
            ModePerformance = fluent.ModePerformance,
            ModeCustom = fluent.ModeCustom,
            SelectionFillOpacity = fluent.SelectionFillOpacity,
            SelectionText = fluent.SelectionText,
        };
    }

    // ---- Contrast math, against published WCAG reference values ----

    [Fact]
    public void Contrast_BlackOnWhite_Is21() => Assert.Equal(21.0, ColorContrast.Ratio(Colors.Black, Colors.White), 3);

    [Fact]
    public void Contrast_SameColor_Is1() => Assert.Equal(1.0, ColorContrast.Ratio(Colors.Teal, Colors.Teal), 3);

    [Fact]
    public void Contrast_IsSymmetric() =>
        Assert.Equal(ColorContrast.Ratio(Colors.Orange, Colors.Navy), ColorContrast.Ratio(Colors.Navy, Colors.Orange), 6);

    [Fact]
    public void Contrast_767676OnWhite_JustPassesAA()
    {
        // #767676 is the commonly cited lightest gray that reaches 4.5:1 on white (4.54:1); #777777 misses it.
        Assert.Equal(4.54, ColorContrast.Ratio(Color.FromRgb(0x76, 0x76, 0x76), Colors.White), 2);
        Assert.True(ColorContrast.Ratio(Color.FromRgb(0x77, 0x77, 0x77), Colors.White) < 4.5);
    }

    [Fact]
    public void Composite_HalfWhiteOverBlack_IsMidGray()
    {
        var c = ColorContrast.Composite(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF), Colors.Black);
        Assert.Equal(Color.FromRgb(128, 128, 128), c);
    }

    [Fact]
    public void Composite_OpaqueForeground_IsUnchanged() =>
        Assert.Equal(Colors.Orange, ColorContrast.Composite(Colors.Orange, Colors.Navy));

    // ---- Every shipped skin ----

    [Theory]
    [MemberData(nameof(AvailableSkins))]
    public void EverySkin_DefinesAppTokens_WithUsableValues(UiSkin skin, bool isDark)
    {
        StaRunner.Run(() =>
        {
            var palette = SkinPalette.For(skin, isDark, SkinOptions.Default);
            var dictionary = palette.ToResourceDictionary();

            Assert.Equal(skin, palette.Skin);
            foreach (var key in SkinKeys.All)
                Assert.True(dictionary.Contains(key), $"{skin}: missing {key}");

            Assert.IsType<FontFamily>(dictionary[SkinKeys.FontFamily]);
            Assert.IsType<FontFamily>(dictionary[SkinKeys.NumberFontFamily]);
            Assert.IsType<Color>(dictionary[SkinKeys.GlowColor]);
            var glow = Assert.IsType<double>(dictionary[SkinKeys.GlowOpacity]);
            Assert.InRange(glow, 0, 1);

            foreach (var key in new[] { SkinKeys.ModeQuietBrush, SkinKeys.ModeBalanceBrush, SkinKeys.ModePerformanceBrush, SkinKeys.ModeCustomBrush })
            {
                var brush = Assert.IsType<SolidColorBrush>(dictionary[key]);
                Assert.True(brush.IsFrozen, $"{skin}: {key} should be frozen");
                Assert.Equal(255, brush.Color.A);
            }
        });
    }

    [Theory]
    [MemberData(nameof(AvailableSkins))]
    public void EverySkin_WithOwnColors_IsReadable_OtherwiseIsFluent(UiSkin skin, bool isDark)
    {
        StaRunner.Run(() =>
        {
            var palette = SkinPalette.For(skin, isDark, SkinOptions.Default);

            if (palette.Colors is null)
            {
                // Only Fluent may leave WPF-UI's own (already accessible) colors in place.
                Assert.Equal(UiSkin.Fluent, skin);
                return;
            }

            AssertReadable(skin.ToString(), palette.Colors);
        });
    }

    [Fact]
    public void Fluent_IsLightAndDark_AndAddsNoWpfUiOverrides()
    {
        StaRunner.Run(() =>
        {
            foreach (var isDark in new[] { true, false })
            {
                var palette = SkinPalette.Fluent(isDark);
                Assert.False(palette.DarkOnly);
                Assert.False(palette.SolidBackground);
                Assert.Null(palette.Colors);

                var keys = palette.ToResourceDictionary().Keys.Cast<string>().ToHashSet();
                Assert.True(keys.SetEquals(SkinKeys.All), "Fluent should only add the app's own tokens: " + string.Join(", ", keys));
            }
        });
    }

    [Fact]
    public void Fluent_ModeColors_MatchTheColorsTheAppAlreadyUsesForNotifications()
    {
        StaRunner.Run(() =>
        {
            var palette = SkinPalette.Fluent(true);
            Assert.Equal(PowerModeState.Quiet.GetDefaultSolidColorBrush().Color, palette.ModeQuiet);
            Assert.Equal(PowerModeState.Balance.GetDefaultSolidColorBrush().Color, palette.ModeBalance);
            Assert.Equal(PowerModeState.Performance.GetDefaultSolidColorBrush().Color, palette.ModePerformance);
            Assert.Equal(PowerModeState.GodMode.GetDefaultSolidColorBrush().Color, palette.ModeCustom);
        });
    }

    [Theory]
    [InlineData(PowerModeState.Quiet)]
    [InlineData(PowerModeState.Balance)]
    [InlineData(PowerModeState.Performance)]
    [InlineData(PowerModeState.GodMode)]
    [InlineData(PowerModeState.Extreme)]
    public void ModeColor_MatchesTheModeBrushTheDashboardUses(PowerModeState state)
    {
        StaRunner.Run(() =>
        {
            var neon = SkinPalette.Neon(SkinOptions.Default);
            var brush = (SolidColorBrush)neon.ToResourceDictionary()[LenovoLegionToolkit.WPF.Controls.Dashboard.StatusStripFormat.ModeBrushKey(state)];
            Assert.Equal(brush.Color, neon.ModeColor(state));
        });
    }

    [Fact]
    public void Fluent_LightTheme_BalanceColorStaysVisible()
    {
        // White on WPF-UI's light background (#FAFAFA) would be invisible.
        var balance = SkinPalette.Fluent(false).ModeBalance;
        Assert.True(ColorContrast.Ratio(balance, Color.FromRgb(0xFA, 0xFA, 0xFA)) >= 4.5);
    }

    [Fact]
    public void For_SkinNotInThisBuild_FallsBackToFluent()
    {
        StaRunner.Run(() =>
        {
            foreach (var skin in global::System.Enum.GetValues<UiSkin>().Where(s => !SkinPalette.Available.Contains(s)))
                Assert.Equal(UiSkin.Fluent, SkinPalette.For(skin, true, SkinOptions.Default).Skin);
        });
    }

    // ---- Neon ----

    [Theory]
    [InlineData(0, true, 0.0)]
    [InlineData(60, true, 0.54)]
    [InlineData(100, true, 0.9)]
    [InlineData(-5, true, 0.0)]     // out-of-range settings are clamped
    [InlineData(150, true, 0.9)]
    [InlineData(100, false, 0.0)]   // no GPU effects: no glow at all
    public void NeonGlowOpacity_ScalesTheSetting(int glow, bool effectsAllowed, double expected) =>
        Assert.Equal(expected, SkinPalette.NeonGlowOpacity(glow, effectsAllowed), 6);

    [Fact]
    public void Neon_GlowFollowsTheOptions()
    {
        StaRunner.Run(() =>
        {
            Assert.Equal(0, SkinPalette.Neon(new SkinOptions(0, RetroPhosphor.Green, true)).GlowOpacity);
            Assert.Equal(0, SkinPalette.Neon(new SkinOptions(100, RetroPhosphor.Green, false)).GlowOpacity);
            Assert.Equal(0.9, SkinPalette.Neon(new SkinOptions(100, RetroPhosphor.Green, true)).GlowOpacity, 6);
        });
    }

    [Fact]
    public void Neon_IsDarkOnly_WithSolidBackground_AndTheMockupColors()
    {
        StaRunner.Run(() =>
        {
            var neon = SkinPalette.For(UiSkin.Neon, false, SkinOptions.Default);
            Assert.Equal(UiSkin.Neon, neon.Skin);
            Assert.True(neon.DarkOnly);
            Assert.True(neon.SolidBackground);
            Assert.Equal(Color.FromRgb(0x0B, 0x0D, 0x12), neon.Colors!.ApplicationBackground);
            Assert.Equal(Color.FromRgb(0x22, 0xD3, 0xEE), neon.Colors.Accent);
            Assert.Equal(Color.FromRgb(0xFF, 0x4D, 0x5E), neon.ModePerformance);
        });
    }

    [Fact]
    public void HotColor_OnlyInSkinsThatUseIt()
    {
        StaRunner.Run(() =>
        {
            var neon = SkinPalette.Neon(SkinOptions.Default).ToResourceDictionary();
            Assert.IsType<SolidColorBrush>(neon[SkinKeys.HotBrush]);
            Assert.False(SkinPalette.Fluent(true).ToResourceDictionary().Contains(SkinKeys.HotBrush));
        });
    }

    [Theory]
    [MemberData(nameof(AvailableSkins))]
    public void SkinsWithOwnColors_ModeAndHotColorsStandOutFromTheBackground(UiSkin skin, bool isDark)
    {
        // Mode colors fill the selector outline, dots and bars; WCAG asks 3:1 for such graphics.
        StaRunner.Run(() =>
        {
            var palette = SkinPalette.For(skin, isDark, SkinOptions.Default);
            if (palette.Colors is null)
                return; // Fluent: covered by the light-theme Balance test and the app's existing colors
            var background = palette.Colors.ApplicationBackground;
            foreach (var color in new[] { palette.ModeQuiet, palette.ModeBalance, palette.ModePerformance, palette.ModeCustom })
                Assert.True(ColorContrast.Ratio(color, background) >= 3, $"{skin}: {color} on {background}");
            if (palette.HotColor is { } hot)
                Assert.True(ColorContrast.Ratio(hot, ColorContrast.Composite(palette.Colors.CardBackground, background)) >= 4.5);
        });
    }
    // ---- Retro ----

    public static IEnumerable<object[]> Phosphors() =>
        global::System.Enum.GetValues<RetroPhosphor>().Select(p => new object[] { p });

    [Theory]
    [MemberData(nameof(Phosphors))]
    public void Retro_EveryPhosphor_IsReadable(RetroPhosphor phosphor)
    {
        StaRunner.Run(() =>
        {
            var retro = SkinPalette.Retro(phosphor);
            AssertReadable($"Retro {phosphor}", retro.Colors!);

            // Text on popups and menus (the most-lit glass) too.
            var menu = retro.Colors.SolidBackgroundQuarternary;
            Assert.True(ColorContrast.Ratio(retro.Colors.TextSecondary, menu) >= 4.5, $"{phosphor}: dim text on menus");
        });
    }

    [Theory]
    [MemberData(nameof(Phosphors))]
    public void Retro_InverseSelection_IsReadable(RetroPhosphor phosphor)
    {
        // Selected items are filled with the phosphor and lettered in the screen color.
        StaRunner.Run(() =>
        {
            var retro = SkinPalette.Retro(phosphor);
            Assert.Equal(1, retro.SelectionFillOpacity);
            foreach (var fill in new[] { retro.ModeQuiet, retro.ModeBalance, retro.ModePerformance, retro.ModeCustom })
                Assert.True(ColorContrast.Ratio(retro.SelectionText, fill) >= 4.5, $"{phosphor}: selection text on {fill}");
        });
    }

    [Theory]
    [MemberData(nameof(Phosphors))]
    public void Retro_IsSquare_DarkOnly_Solid_WithoutPerElementGlow(RetroPhosphor phosphor)
    {
        StaRunner.Run(() =>
        {
            var retro = SkinPalette.For(UiSkin.Retro, false, SkinOptions.Default with { Phosphor = phosphor });
            Assert.Equal(UiSkin.Retro, retro.Skin);
            Assert.True(retro.DarkOnly);
            Assert.True(retro.SolidBackground);
            Assert.Equal(new CornerRadius(0), retro.Colors!.ControlCornerRadius);
            Assert.Equal(new CornerRadius(0), retro.Colors.PopupCornerRadius);
            Assert.Equal(0, retro.GlowOpacity);
        });
    }

    [Fact]
    public void Retro_PhosphorsDiffer_AndMatchTheMockup()
    {
        StaRunner.Run(() =>
        {
            Assert.Equal(Color.FromRgb(0x3D, 0xFF, 0x7A), SkinPalette.Retro(RetroPhosphor.Green).Colors!.TextPrimary);
            Assert.Equal(Color.FromRgb(0xFF, 0xB5, 0x2E), SkinPalette.Retro(RetroPhosphor.Amber).Colors!.TextPrimary);
            Assert.Equal(Color.FromRgb(0xE6, 0xF2, 0xEA), SkinPalette.Retro(RetroPhosphor.White).Colors!.TextPrimary);
            Assert.Equal(Color.FromRgb(0x03, 0x12, 0x07), SkinPalette.Retro(RetroPhosphor.Green).Colors!.ApplicationBackground);
        });
    }

    [Fact]
    public void Mix_BlendsLinearly()
    {
        Assert.Equal(Colors.Black, SkinPalette.Mix(Colors.Black, Colors.White, 0));
        Assert.Equal(Colors.White, SkinPalette.Mix(Colors.Black, Colors.White, 1));
        Assert.Equal(Color.FromRgb(128, 128, 128), SkinPalette.Mix(Colors.Black, Colors.White, 0.5));
    }

    // ---- The WPF-UI key mapping ----

    [Fact]
    public void Mapping_CoversEveryRequiredWpfUiKey()
    {
        StaRunner.Run(() =>
        {
            var dictionary = ProbePalette().ToResourceDictionary();
            foreach (var key in SkinKeys.RequiredWpfUiOverrides)
                Assert.True(dictionary.Contains(key), $"missing override for {key}");
        });
    }

    [Fact]
    public void Mapping_OnlyUsesKeysThatWpfUiDefines()
    {
        // A misspelt key would silently do nothing, so every override must exist in WPF-UI's own resources.
        StaRunner.Run(() =>
        {
            var wpfUiKeys = new HashSet<string>();
            Collect(new ThemesDictionary { Theme = ThemeType.Dark }, wpfUiKeys);
            Collect(new ControlsDictionary(), wpfUiKeys);
            Assert.Contains("TextFillColorPrimaryBrush", wpfUiKeys);
            Assert.Contains("ControlCornerRadius", wpfUiKeys);

            var dictionary = ProbePalette().ToResourceDictionary();
            var unknown = dictionary.Keys.Cast<string>()
                .Where(k => !SkinKeys.All.Contains(k))
                .Where(k => !wpfUiKeys.Contains(k))
                .ToList();

            Assert.True(unknown.Count == 0, "Keys WPF-UI doesn't define: " + string.Join(", ", unknown));
        });
    }

    [Fact]
    public void Mapping_WritesColorAndMatchingBrush_WithTheSameValue()
    {
        StaRunner.Run(() =>
        {
            var colors = MappingProbeColors();
            var dictionary = ProbePalette().ToResourceDictionary();

            foreach (var (colorKey, value) in colors.WpfUiColors())
            {
                Assert.Equal(value, Assert.IsType<Color>(dictionary[colorKey]));
                var brush = Assert.IsType<SolidColorBrush>(dictionary[SkinPalette.BrushKeyFor(colorKey)]);
                Assert.Equal(value, brush.Color);
                Assert.True(brush.IsFrozen);
            }

            Assert.Equal(colors.TextPrimary, ((SolidColorBrush)dictionary["TextFillColorPrimaryBrush"]).Color);
            Assert.Equal(colors.ApplicationBackground, ((SolidColorBrush)dictionary["ApplicationBackgroundBrush"]).Color);
            Assert.Equal(new CornerRadius(3), dictionary["ControlCornerRadius"]);
            Assert.Equal(new CornerRadius(6), dictionary["PopupCornerRadius"]);
        });
    }

    [Fact]
    public void Mapping_SolidCardStroke_IsTheTranslucentStrokeBlendedOverTheBackground()
    {
        StaRunner.Run(() =>
        {
            var colors = MappingProbeColors();
            var solid = (Color)ProbePalette().ToResourceDictionary()["CardStrokeColorDefaultSolid"];
            Assert.Equal(255, solid.A);
            Assert.Equal(ColorContrast.Composite(colors.CardStroke, colors.ApplicationBackground), solid);
        });
    }

    [Fact]
    public void SkinDictionaryMergedLast_WinsOverWpfUiTheme_AndSurvivesThemeSwitch()
    {
        // ThemeManager relies on this: WPF-UI's Theme.Apply swaps its theme dictionary in place, so a skin
        // dictionary merged after it keeps winning across Light/Dark changes. Uses the real WPF-UI calls.
        StaRunner.Run(() =>
        {
            var app = Application.Current ?? new Application();
            // As in App.xaml.cs: the main window exists (not yet shown) when the theme is first applied.
            app.MainWindow ??= new Window();
            var merged = app.Resources.MergedDictionaries;
            merged.Add(new ThemesDictionary { Theme = ThemeType.Dark });

            var probe = ProbePalette();
            var skin = probe.ToResourceDictionary();
            merged.Add(skin);

            Color Resolve(string key) => ((SolidColorBrush)app.FindResource(key)).Color;

            // Not overridden by the skin, so it shows which WPF-UI theme is active.
            Assert.Equal(Color.FromRgb(0x9D, 0x9D, 0x9D), Resolve("SystemFillColorSolidNeutralBrush"));
            Assert.Equal(probe.Colors!.TextPrimary, Resolve("TextFillColorPrimaryBrush"));

            Wpf.Ui.Appearance.Theme.Apply(ThemeType.Light, BackgroundType.None, false);

            Assert.Equal(Color.FromRgb(0x8A, 0x8A, 0x8A), Resolve("SystemFillColorSolidNeutralBrush"));
            Assert.Same(skin, merged[merged.Count - 1]);
            Assert.Equal(probe.Colors.TextPrimary, Resolve("TextFillColorPrimaryBrush"));
            Assert.Equal(probe.Colors.ApplicationBackground, Resolve("ApplicationBackgroundBrush"));
        });
    }
    private static void Collect(ResourceDictionary dictionary, HashSet<string> keys)
    {
        foreach (var key in dictionary.Keys)
            if (key is string s)
                keys.Add(s);
        foreach (var merged in dictionary.MergedDictionaries)
            Collect(merged, keys);
    }

    /// <summary>
    /// Text must reach 4.5:1 (WCAG AA) on the window background and on cards, which may be translucent
    /// and are therefore checked as drawn over the background.
    /// </summary>
    internal static void AssertReadable(string name, SkinColors colors)
    {
        var background = colors.ApplicationBackground;
        Assert.True(background.A == 255, $"{name}: the application background must be opaque");
        var card = ColorContrast.Composite(colors.CardBackground, background);

        foreach (var (label, text) in new[] { ("primary", colors.TextPrimary), ("secondary", colors.TextSecondary), ("tertiary", colors.TextTertiary) })
        {
            var onBackground = ColorContrast.Ratio(ColorContrast.Composite(text, background), background);
            var onCard = ColorContrast.Ratio(ColorContrast.Composite(text, card), card);
            Assert.True(onBackground >= 4.5, $"{name}: {label} text on background is {onBackground:0.00}:1");
            Assert.True(onCard >= 4.5, $"{name}: {label} text on cards is {onCard:0.00}:1");
        }
    }
}
