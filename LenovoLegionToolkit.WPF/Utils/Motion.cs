using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Settings;
using TransitionType = Wpf.Ui.Animations.TransitionType;

namespace LenovoLegionToolkit.WPF.Utils;

/// <summary>
/// Why the app is using less motion than the user's setting asks for.
/// </summary>
public enum MotionLimit
{
    None,
    WindowsAnimationsOff,
    HardwareAccelerationOff,
    NoHardwareRendering,
}

/// <summary>
/// One place that decides how much the UI moves, and small animation helpers that respect it.
/// <list type="bullet">
/// <item><b>Full</b>: slides, hover lift, number count-ups, looping effects.</item>
/// <item><b>Reduced</b>: fades only; nothing moves, counts or loops.</item>
/// <item><b>Off</b>: no animation at all.</item>
/// </list>
/// Full is only used when Windows "Animation effects" is on and the app renders on the GPU; otherwise
/// the level drops to Reduced.
/// </summary>
public static class Motion
{
    private static readonly List<LoopRegistration> Loops = [];
    private static bool _windowShown;
    private static bool _initialized;

    /// <summary>Frames per second every animation in the app runs at (WPF's default is the display's rate).</summary>
    public const int AnimationFrameRate = 30;

    private static readonly object FrameRateLock = new();
    private static bool _frameRateCapped;

    /// <summary>
    /// Caps every animation in the app (LLT's and WPF-UI's) at <see cref="AnimationFrameRate"/> frames per second by
    /// changing the default of <see cref="Timeline.DesiredFrameRateProperty"/>. Animations are what keeps WPF
    /// redrawing continuously; a redraw caused directly by input (scrolling, hovering) still happens at once.
    /// Call once at startup, before any window opens; later calls do nothing.
    /// </summary>
    public static void CapAnimationFrameRate()
    {
        lock (FrameRateLock)
        {
            if (_frameRateCapped)
                return;

            Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(Timeline), new FrameworkPropertyMetadata { DefaultValue = AnimationFrameRate });
            _frameRateCapped = true;
        }
    }

    public static AnimationLevel Level { get; private set; } = AnimationLevel.Reduced;

    public static MotionLimit Limit { get; private set; }

    /// <summary>
    /// The GPU renders the app (render tier 2, hardware acceleration on), so effects such as glow are cheap.
    /// </summary>
    public static bool EffectsAllowed { get; private set; }

    public static event EventHandler? LevelChanged;

    /// <summary>
    /// Reads the current state and starts listening for Windows changes. Call once at startup.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized)
            return;
        _initialized = true;

        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation))
                Application.Current.Dispatcher.Invoke(Refresh);
        };
        RenderCapability.TierChanged += (_, _) => Application.Current.Dispatcher.Invoke(Refresh);

        Refresh();
    }

    /// <summary>
    /// Recomputes <see cref="Level"/> from the setting and the system, e.g. after the setting changes.
    /// </summary>
    public static void Refresh()
    {
        var setting = IoCContainer.Resolve<ApplicationSettings>().Store.Animations;
        var windowsAnimations = SystemParameters.ClientAreaAnimation;
        var renderTier = RenderCapability.Tier >> 16;
        // The hardware-acceleration setting only takes effect after a restart, so use what is in effect now.
        var gpuRendering = RenderOptions.ProcessRenderMode != RenderMode.SoftwareOnly;

        var (level, limit) = Resolve(setting, windowsAnimations, renderTier, gpuRendering);
        var effectsAllowed = renderTier >= 2 && gpuRendering;
        var changed = level != Level || limit != Limit || effectsAllowed != EffectsAllowed;
        Level = level;
        Limit = limit;
        EffectsAllowed = effectsAllowed;

        if (!changed)
            return;

        UpdateLoops();
        LevelChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// The level to use. <paramref name="renderTier"/> is WPF's rendering tier (0 = software, 2 = full GPU).
    /// </summary>
    public static (AnimationLevel Level, MotionLimit Limit) Resolve(AnimationLevel setting, bool windowsAnimations, int renderTier, bool gpuRendering)
    {
        if (setting == AnimationLevel.Off)
            return (AnimationLevel.Off, MotionLimit.None);

        if (!windowsAnimations)
            return (AnimationLevel.Reduced, setting == AnimationLevel.Full ? MotionLimit.WindowsAnimationsOff : MotionLimit.None);

        if (!gpuRendering)
            return (AnimationLevel.Reduced, setting == AnimationLevel.Full ? MotionLimit.HardwareAccelerationOff : MotionLimit.None);

        if (renderTier < 2)
            return (AnimationLevel.Reduced, setting == AnimationLevel.Full ? MotionLimit.NoHardwareRendering : MotionLimit.None);

        return (setting, MotionLimit.None);
    }

    /// <summary>
    /// Page transition for the navigation frame at a given level.
    /// </summary>
    public static (TransitionType Type, int DurationMs) PageTransition(AnimationLevel level) => level switch
    {
        AnimationLevel.Full => (TransitionType.FadeInWithSlide, 220),
        AnimationLevel.Reduced => (TransitionType.FadeIn, 150),
        _ => (TransitionType.None, 0),
    };

    // ---- One-shot helpers ----

    private static readonly IEasingFunction EaseOut = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });

    /// <summary>
    /// Fades the element in after <paramref name="delay"/>; at Full it also rises 8 px. The element is held
    /// invisible during the delay, and nothing is left animated afterwards.
    /// </summary>
    public static void FadeSlideIn(UIElement element, TimeSpan delay)
    {
        if (Level == AnimationLevel.Off)
            return;

        var full = Level == AnimationLevel.Full;
        var duration = TimeSpan.FromMilliseconds(full ? 320 : 150);

        element.BeginAnimation(UIElement.OpacityProperty, HoldThenAnimate(0, 1, delay, duration));

        if (full && GetOrCreateTranslate(element) is { } translate)
            translate.BeginAnimation(TranslateTransform.YProperty, HoldThenAnimate(8, 0, delay, duration));
    }

    /// <summary>
    /// Shows a number in a TextBlock. At Full it counts from the value shown now to the new one; otherwise it
    /// is set straight away.
    /// </summary>
    public static void AnimateNumber(TextBlock textBlock, double to, int decimals, string suffix = "")
    {
        SetNumberDecimals(textBlock, decimals);
        SetNumberSuffix(textBlock, suffix);

        if (Level != AnimationLevel.Full || !textBlock.IsLoaded)
        {
            textBlock.BeginAnimation(NumberValueProperty, null);
            SetNumberValue(textBlock, to);
            textBlock.Text = FormatNumber(to, decimals, suffix);
            return;
        }

        var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(600)) { EasingFunction = EaseOut };
        textBlock.BeginAnimation(NumberValueProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>
    /// Fills a bar to <paramref name="fraction"/> (0 to 1) by scaling it horizontally, which avoids a layout
    /// pass per frame. Glides at Full, snaps otherwise.
    /// </summary>
    public static void AnimateFill(FrameworkElement bar, double fraction)
    {
        var target = ClampFraction(fraction);

        if (bar.RenderTransform is not ScaleTransform scale || scale.IsFrozen)
        {
            scale = new ScaleTransform(0, 1);
            bar.RenderTransformOrigin = new Point(0, 0.5);
            bar.RenderTransform = scale;
        }

        if (Level != AnimationLevel.Full || !bar.IsLoaded)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.ScaleX = target;
            return;
        }

        var animation = new DoubleAnimation(target, TimeSpan.FromMilliseconds(400)) { EasingFunction = EaseOut };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>
    /// A single brief dim-and-return, used to draw the eye to a value that changed. Full only.
    /// </summary>
    public static void Pulse(UIElement element)
    {
        if (Level != AnimationLevel.Full)
            return;

        var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(600), FillBehavior = FillBehavior.Stop };
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(0.55, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200)), EaseOut));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(600)), EaseOut));
        element.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    // ---- Pure helpers (unit-tested) ----

    /// <summary>
    /// Rounds half away from zero and never shows "-0".
    /// </summary>
    public static string FormatNumber(double value, int decimals, string suffix = "")
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return "—";

        decimals = Math.Clamp(decimals, 0, 6);
        var rounded = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
        if (rounded == 0)
            rounded = 0; // turns -0 into 0
        return rounded.ToString("F" + decimals, CultureInfo.CurrentCulture) + suffix;
    }

    public static double ClampFraction(double fraction) => double.IsNaN(fraction) ? 0 : Math.Clamp(fraction, 0, 1);

    /// <summary>
    /// Looping effects run only at Full, and only while the main window is on screen.
    /// </summary>
    public static bool ShouldRunLoops(AnimationLevel level, bool windowShown) => level == AnimationLevel.Full && windowShown;

    // ---- Looping effects ----

    /// <summary>
    /// Registers a repeating storyboard owned by <paramref name="owner"/>. It runs only while
    /// <see cref="ShouldRunLoops"/> allows, and is removed when the owner unloads or the returned handle
    /// is disposed.
    /// </summary>
    public static IDisposable RegisterLoop(FrameworkElement owner, Storyboard storyboard)
    {
        var registration = new LoopRegistration(owner, storyboard);
        Loops.Add(registration);
        owner.Unloaded += (_, _) => registration.Dispose();
        registration.Apply(ShouldRunLoops(Level, _windowShown));
        return registration;
    }

    /// <summary>
    /// Called by the main window when it is shown, hidden, minimized or restored.
    /// </summary>
    public static void SetWindowShown(bool shown)
    {
        if (_windowShown == shown)
            return;
        _windowShown = shown;
        UpdateLoops();
    }

    private static void UpdateLoops()
    {
        var run = ShouldRunLoops(Level, _windowShown);
        foreach (var loop in Loops.ToArray())
            loop.Apply(run);
    }

    private sealed class LoopRegistration(FrameworkElement owner, Storyboard storyboard) : IDisposable
    {
        private bool _running;

        public void Dispose()
        {
            Apply(false);
            Loops.Remove(this);
        }

        public void Apply(bool run)
        {
            if (run == _running)
                return;

            if (run)
                storyboard.Begin(owner, true);
            else
                storyboard.Stop(owner);

            _running = run;
        }
    }

    // ---- Attached properties ----

    /// <summary>
    /// Lifts the element 2 px while the pointer is over it (Full only). Set from the card styles.
    /// </summary>
    public static readonly DependencyProperty HoverLiftProperty = DependencyProperty.RegisterAttached(
        "HoverLift", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnHoverLiftChanged));

    public static bool GetHoverLift(DependencyObject d) => (bool)d.GetValue(HoverLiftProperty);
    public static void SetHoverLift(DependencyObject d, bool value) => d.SetValue(HoverLiftProperty, value);

    private static void OnHoverLiftChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
            return;

        element.MouseEnter -= HoverLift_MouseEnter;
        element.MouseLeave -= HoverLift_MouseLeave;

        if (e.NewValue is true)
        {
            element.MouseEnter += HoverLift_MouseEnter;
            element.MouseLeave += HoverLift_MouseLeave;
        }
    }

    private static void HoverLift_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (Level != AnimationLevel.Full || sender is not UIElement { IsEnabled: true } element)
            return;
        if (GetOrCreateTranslate(element) is { } translate)
            translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-2, TimeSpan.FromMilliseconds(150)) { EasingFunction = EaseOut }, HandoffBehavior.SnapshotAndReplace);
    }

    private static void HoverLift_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not UIElement element || element.RenderTransform is not TranslateTransform { IsFrozen: false } translate)
            return;
        // Always settle back, even if the level changed while hovering.
        translate.BeginAnimation(TranslateTransform.YProperty, Level == AnimationLevel.Full
            ? new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)) { EasingFunction = EaseOut }
            : null, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>
    /// The number a TextBlock shows; animating it re-formats the text each frame.
    /// </summary>
    public static readonly DependencyProperty NumberValueProperty = DependencyProperty.RegisterAttached(
        "NumberValue", typeof(double), typeof(Motion), new PropertyMetadata(0.0, OnNumberValueChanged));

    public static double GetNumberValue(DependencyObject d) => (double)d.GetValue(NumberValueProperty);
    public static void SetNumberValue(DependencyObject d, double value) => d.SetValue(NumberValueProperty, value);

    private static readonly DependencyProperty NumberDecimalsProperty = DependencyProperty.RegisterAttached(
        "NumberDecimals", typeof(int), typeof(Motion), new PropertyMetadata(0));

    private static void SetNumberDecimals(DependencyObject d, int value) => d.SetValue(NumberDecimalsProperty, value);

    private static readonly DependencyProperty NumberSuffixProperty = DependencyProperty.RegisterAttached(
        "NumberSuffix", typeof(string), typeof(Motion), new PropertyMetadata(string.Empty));

    private static void SetNumberSuffix(DependencyObject d, string value) => d.SetValue(NumberSuffixProperty, value);

    private static void OnNumberValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock textBlock)
            textBlock.Text = FormatNumber((double)e.NewValue, (int)d.GetValue(NumberDecimalsProperty), (string)d.GetValue(NumberSuffixProperty));
    }

    // ---- Internals ----

    /// <summary>
    /// The element's translate transform, created if it has none. Returns null when the element already uses
    /// some other transform, which is left alone.
    /// </summary>
    private static TranslateTransform? GetOrCreateTranslate(UIElement element)
    {
        switch (element.RenderTransform)
        {
            case TranslateTransform { IsFrozen: false } existing:
                return existing;
            case null:
            case var t when t == Transform.Identity || t.Value.IsIdentity && t.IsFrozen:
                var created = new TranslateTransform();
                element.RenderTransform = created;
                return created;
            default:
                return null;
        }
    }

    /// <summary>
    /// Holds <paramref name="from"/> for <paramref name="delay"/>, then eases to <paramref name="to"/>.
    /// Stops at the end so the property returns to its own value, which is <paramref name="to"/>.
    /// </summary>
    private static DoubleAnimationUsingKeyFrames HoldThenAnimate(double from, double to, TimeSpan delay, TimeSpan duration)
    {
        var animation = new DoubleAnimationUsingKeyFrames { Duration = delay + duration, FillBehavior = FillBehavior.Stop };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(delay)));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(delay + duration), EaseOut));
        return animation;
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
