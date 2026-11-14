using System;
using System.Linq;
using System.Windows;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Utils.Skins;
using ThemeType = Wpf.Ui.Appearance.ThemeType;
using BackgroundType = Wpf.Ui.Appearance.BackgroundType;
using Accent = Wpf.Ui.Appearance.Accent;

namespace LenovoLegionToolkit.WPF.Utils;

public class ThemeManager
{
    private static readonly RGBColor DefaultAccentColor = new(255, 33, 33);

    private readonly ApplicationSettings _settings;
    private readonly SystemThemeListener _listener;

    private ResourceDictionary? _skinDictionary;

    public event EventHandler? ThemeApplied;

    /// <summary>
    /// The palette applied by the last <see cref="Apply"/> call.
    /// </summary>
    public SkinPalette CurrentPalette { get; private set; } = SkinPalette.Fluent(true);

    /// <summary>
    /// The palette in use, for code that has no reference to the theme manager (notifications, tray popup).
    /// Null until the theme is first applied.
    /// </summary>
    public static SkinPalette? ActivePalette { get; private set; }

    public ThemeManager(SystemThemeListener systemThemeListener, ApplicationSettings settings)
    {
        _listener = systemThemeListener;
        _settings = settings;

        _listener.Changed += (_, _) => Application.Current.Dispatcher.Invoke(Apply);

        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.HighContrast))
                Application.Current.Dispatcher.Invoke(Apply);
        };

        // Glow depends on whether the GPU can draw effects, which Motion tracks.
        Motion.LevelChanged += (_, _) => Apply();
    }

    public void Apply()
    {
        var skin = GetEffectiveSkin();
        var options = GetSkinOptions();
        var isDark = SkinPalette.For(skin, true, options).DarkOnly || IsDarkMode();
        var palette = SkinPalette.For(skin, isDark, options);

        SetTheme(isDark, palette);
        SetColor(palette);
        SetSkinResources(palette);
        SetWindowBackdrops(palette);

        CurrentPalette = palette;
        ActivePalette = palette;

        ThemeApplied?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The skin to draw with. Windows High Contrast always gets Fluent, because WPF-UI's own colors are the
    /// ones that follow the system's high-contrast setup; skins not available in this build also fall back
    /// to Fluent.
    /// </summary>
    public UiSkin GetEffectiveSkin()
    {
        if (SystemParameters.HighContrast)
            return UiSkin.Fluent;

        var skin = _settings.Store.Skin;
        return SkinPalette.Available.Contains(skin) ? skin : UiSkin.Fluent;
    }

    private SkinOptions GetSkinOptions() => new(_settings.Store.NeonGlow, _settings.Store.RetroPhosphor, Motion.EffectsAllowed);

    /// <summary>
    /// The backdrop windows should use: the user's choice, or none for skins that draw a solid background.
    /// </summary>
    public WindowBackdropType GetEffectiveBackdrop() =>
        CurrentPalette.SolidBackground ? WindowBackdropType.None : _settings.Store.BackdropType;

    /// <summary>
    /// Switching between Fluent and a solid-background skin changes the backdrop of windows already open.
    /// </summary>
    private void SetWindowBackdrops(SkinPalette palette)
    {
        var backdrop = GetBackgroundType(palette.SolidBackground ? WindowBackdropType.None : _settings.Store.BackdropType);
        foreach (Window window in Application.Current.Windows)
        {
            if (window is Wpf.Ui.Controls.UiWindow uiWindow && uiWindow.WindowBackdropType != backdrop)
                uiWindow.WindowBackdropType = backdrop;
        }
    }

    public RGBColor GetAccentColor()
    {
        switch (_settings.Store.AccentColorSource)
        {
            case AccentColorSource.Custom:
                return _settings.Store.AccentColor ?? DefaultAccentColor;
            case AccentColorSource.System:
                try
                {
                    return SystemTheme.GetAccentColor();
                }
                catch (Exception ex)
                {
                    Log.Instance.Trace($"Couldn't check system accent color; using default.", ex);

                    return DefaultAccentColor;
                }
            default:
                return DefaultAccentColor;
        }
    }

    private bool IsDarkMode()
    {
        var theme = _settings.Store.Theme;

        switch (theme)
        {
            case Theme.Dark:
                return true;
            case Theme.Light:
                return false;
            case Theme.System:
                try
                {
                    return SystemTheme.IsDarkMode();
                }
                catch (Exception ex)
                {
                    Log.Instance.Trace($"Couldn't check system theme; assuming Dark Mode.", ex);

                    return true;
                }
            default:
                return true;
        }
    }

    private void SetTheme(bool isDark, SkinPalette palette)
    {
        var theme = isDark ? ThemeType.Dark : ThemeType.Light;
        var background = palette.SolidBackground ? BackgroundType.None : GetBackgroundType(_settings.Store.BackdropType);
        Wpf.Ui.Appearance.Theme.Apply(theme, background, false);
    }

    /// <summary>
    /// Swaps the skin's resource dictionary. It goes last in the application's merged dictionaries so its
    /// keys win over WPF-UI's theme dictionary, which <c>Theme.Apply</c> replaces in place.
    /// </summary>
    private void SetSkinResources(SkinPalette palette)
    {
        var merged = Application.Current.Resources.MergedDictionaries;

        if (_skinDictionary is not null)
            merged.Remove(_skinDictionary);

        _skinDictionary = palette.ToResourceDictionary();
        merged.Add(_skinDictionary);
    }

    public static BackgroundType GetBackgroundType(WindowBackdropType backdropType)
    {
        return backdropType switch
        {
            WindowBackdropType.Mica => BackgroundType.Mica,
            WindowBackdropType.Acrylic => BackgroundType.Acrylic,
            WindowBackdropType.Tabbed => BackgroundType.Tabbed,
            _ => BackgroundType.None
        };
    }

    /// <summary>
    /// Accent colors are written straight into the application's own resources by WPF-UI, where they beat
    /// any merged dictionary, so a skin's accent is applied here rather than through its dictionary.
    /// </summary>
    private void SetColor(SkinPalette palette)
    {
        var accentColor = palette.Colors?.Accent ?? GetAccentColor().ToColor();
        Accent.Apply(systemAccent: accentColor,
            primaryAccent: accentColor,
            secondaryAccent: accentColor,
            tertiaryAccent: accentColor);
    }
}
