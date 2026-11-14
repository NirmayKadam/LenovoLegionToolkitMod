using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using LenovoLegionToolkit.Lib;

namespace LenovoLegionToolkit.WPF.Utils.Skins;

/// <summary>
/// A soft glow around an element, for skins that use one (Neon). The effect is only attached while the
/// skin's glow is above zero, so other skins and machines without GPU rendering pay nothing for it.
/// Used on a handful of elements only: the active navigation item, the power selector's highlight and the
/// status strip's power tile.
/// </summary>
public static class SkinGlow
{
    private static readonly List<WeakReference<FrameworkElement>> Tracked = [];
    private static bool _listening;

    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(SkinGlow), new PropertyMetadata(false, OnChanged));

    public static bool GetEnabled(DependencyObject d) => (bool)d.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject d, bool value) => d.SetValue(EnabledProperty, value);

    /// <summary>
    /// Resource key of the glow's color (a Color or SolidColorBrush). Defaults to the skin's glow color.
    /// </summary>
    public static readonly DependencyProperty ColorKeyProperty = DependencyProperty.RegisterAttached(
        "ColorKey", typeof(string), typeof(SkinGlow), new PropertyMetadata(SkinKeys.GlowColor, OnChanged));

    public static string GetColorKey(DependencyObject d) => (string)d.GetValue(ColorKeyProperty);
    public static void SetColorKey(DependencyObject d, string value) => d.SetValue(ColorKeyProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
            return;

        if (e.Property == EnabledProperty && e.NewValue is true)
        {
            EnsureListening();
            Tracked.RemoveAll(r => !r.TryGetTarget(out var other) || ReferenceEquals(other, element));
            Tracked.Add(new WeakReference<FrameworkElement>(element));
        }

        Update(element);
    }

    private static void EnsureListening()
    {
        if (_listening)
            return;
        _listening = true;
        IoCContainer.Resolve<ThemeManager>().ThemeApplied += (_, _) => UpdateAll();
    }

    private static void UpdateAll()
    {
        Tracked.RemoveAll(r => !r.TryGetTarget(out var element) || !GetEnabled(element));
        foreach (var reference in Tracked)
            if (reference.TryGetTarget(out var element))
                Update(element);
    }

    private static void Update(FrameworkElement element)
    {
        var opacity = IoCContainer.Resolve<ThemeManager>().CurrentPalette.GlowOpacity;

        if (!GetEnabled(element) || opacity <= 0)
        {
            if (element.Effect is DropShadowEffect existing && Ours.TryGetValue(existing, out _))
                element.Effect = null;
            return;
        }

        var effect = new DropShadowEffect
        {
            Color = ResolveColor(element, GetColorKey(element)),
            Opacity = opacity,
            BlurRadius = 16,
            ShadowDepth = 0,
            RenderingBias = RenderingBias.Performance,
        };
        effect.Freeze();
        Ours.AddOrUpdate(effect, Marker);
        element.Effect = effect;
    }

    // Effects this class created, so it never removes an effect something else put on the element.
    private static readonly ConditionalWeakTable<DropShadowEffect, object> Ours = new();
    private static readonly object Marker = new();

    private static Color ResolveColor(FrameworkElement element, string key) => element.TryFindResource(key) switch
    {
        Color color => color,
        SolidColorBrush brush => brush.Color,
        _ => element.TryFindResource(SkinKeys.GlowColor) is Color fallback ? fallback : Colors.Transparent,
    };
}
