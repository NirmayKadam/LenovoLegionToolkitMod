using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Utils;
using LenovoLegionToolkit.WPF.Utils.Skins;

namespace LenovoLegionToolkit.WPF.Controls.Dashboard;

/// <summary>
/// One button per power mode the machine supports, with a highlight that slides to the active one.
/// Switching goes through <see cref="PowerModeFeature"/> exactly like the power-mode card, with the same
/// error handling and settings windows (<see cref="PowerModeActions"/>), and both stay in sync through the
/// same listeners.
/// </summary>
public partial class PowerModeSelectorControl
{
    private static readonly IEasingFunction PillEase = CreatePillEase();

    private readonly PowerModeFeature _feature = IoCContainer.Resolve<PowerModeFeature>();
    private readonly PowerModeListener _powerModeListener = IoCContainer.Resolve<PowerModeListener>();
    private readonly ThermalModeListener _thermalModeListener = IoCContainer.Resolve<ThermalModeListener>();
    private readonly PowerStateListener _powerStateListener = IoCContainer.Resolve<PowerStateListener>();
    private readonly ThemeManager _themeManager = IoCContainer.Resolve<ThemeManager>();
    private readonly DispatcherTimer _typingTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };

    private IEnumerator<string>? _typingFrames;
    private IDisposable? _cursorLoop;
    private PowerModeState? _consoleMode;

    private readonly ThrottleLastDispatcher _throttle = new(TimeSpan.FromMilliseconds(500), nameof(PowerModeSelectorControl));

    private PowerModeState[] _states = [];
    private PowerModeState? _current;
    private bool _isRefreshing;
    private bool _isChanging;
    private bool _subscribed;

    public PowerModeSelectorControl()
    {
        InitializeComponent();

        IsVisibleChanged += PowerModeSelectorControl_IsVisibleChanged;
        _typingTimer.Tick += TypingTimer_Tick;
        Loaded += async (_, _) => await RefreshAsync();
        _track.SizeChanged += (_, _) => PositionPill(false);
    }

    private async void PowerModeSelectorControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Subscribe(IsVisible);
        if (IsVisible)
            await RefreshAsync();
    }

    private void Subscribe(bool subscribe)
    {
        if (subscribe == _subscribed)
            return;
        _subscribed = subscribe;

        if (subscribe)
        {
            _powerModeListener.Changed += Listener_Changed;
            _thermalModeListener.Changed += Listener_Changed;
            _powerStateListener.Changed += Listener_Changed;
            _themeManager.ThemeApplied += ThemeManager_ThemeApplied;
            MessagingCenter.Subscribe<FeatureStateMessage<PowerModeState>>(this, QueueRefresh);
            ApplySkin();
        }
        else
        {
            _powerModeListener.Changed -= Listener_Changed;
            _thermalModeListener.Changed -= Listener_Changed;
            _powerStateListener.Changed -= Listener_Changed;
            _themeManager.ThemeApplied -= ThemeManager_ThemeApplied;
            MessagingCenter.Unsubscribe<FeatureStateMessage<PowerModeState>>(this);
            _typingTimer.Stop();
            _cursorLoop?.Dispose();
            _cursorLoop = null;
        }
    }

    private void Listener_Changed<T>(object? sender, T e) => QueueRefresh();

    private async void QueueRefresh() => await _throttle.DispatchAsync(async () =>
    {
        await Dispatcher.InvokeAsync(async () =>
        {
            if (IsLoaded && IsVisible && !_isChanging)
                await RefreshAsync();
        });
    });

    private async Task RefreshAsync()
    {
        if (_isRefreshing)
            return;
        _isRefreshing = true;

        try
        {
            // The control starts collapsed, so it only appears on machines that support power modes.
            if (!await _feature.IsSupportedAsync())
            {
                Visibility = Visibility.Collapsed;
                return;
            }

            var states = await _feature.GetAllStatesAsync();
            var current = await _feature.GetStateAsync();

            if (!states.SequenceEqual(_states))
                BuildSegments(states);

            _current = current;
            foreach (var segment in _segments.Children.OfType<RadioButton>())
                segment.IsChecked = segment.Tag is PowerModeState s && s == current;

            // A change made elsewhere (the card, Fn+Q, an automation) is reported as the current mode.
            if (_consoleMode != current)
            {
                _consoleMode = current;
                ShowConsoleLine(RetroText.ModeLine(current.GetDisplayName()), _consoleText.Text.Length > 0);
            }

            Visibility = states.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateLayout();
            PositionPill(true);
            await UpdateSettingsButtonAsync(current);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Power mode selector refresh failed.", ex);
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void BuildSegments(PowerModeState[] states)
    {
        _states = states;
        _segments.Children.Clear();

        foreach (var state in states)
        {
            var name = state.GetDisplayName();
            var segment = new RadioButton
            {
                Content = SegmentText(state),
                Tag = state,
                GroupName = $"PowerModeSelector{GetHashCode()}",
                Style = (Style)Resources["SegmentStyle"],
            };
            AutomationProperties.SetName(segment, name);
            segment.Checked += Segment_Checked;
            _segments.Children.Add(segment);
        }
    }

    private async void Segment_Checked(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing || sender is not RadioButton { Tag: PowerModeState selected })
            return;

        if (selected == _current)
            return;

        _isChanging = true;
        _segments.IsEnabled = false;
        PositionPill(true, selected);

        var succeeded = false;
        try
        {
            if (await _feature.GetStateAsync() != selected)
                await _feature.SetStateAsync(selected);
            succeeded = true;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to change power mode from the selector.", ex);
            PowerModeActions.ShowSetStateError(ex);
        }
        finally
        {
            _segments.IsEnabled = true;
            _isChanging = false;
        }

        if (succeeded)
            _consoleMode = selected;
        ShowConsoleLine(RetroText.ChangeLine(selected.GetDisplayName(), succeeded), true);

        // Show what the hardware actually reports now, including after a failure.
        await RefreshAsync();
    }

    /// <summary>
    /// Moves the highlight under the active mode (or <paramref name="target"/> while a change is in flight)
    /// and colors it with that mode's skin color.
    /// </summary>
    private void PositionPill(bool animate, PowerModeState? target = null)
    {
        var state = target ?? _current;
        var index = state is { } s ? Array.IndexOf(_states, s) : -1;

        if (StatusStripFormat.PillGeometry(index, _states.Length, _track.ActualWidth) is not { } geometry || state is null)
        {
            _pill.Visibility = Visibility.Hidden;
            return;
        }

        var key = StatusStripFormat.ModeBrushKey(state.Value);
        _pillFill.SetResourceReference(Border.BackgroundProperty, key);
        _pillOutline.SetResourceReference(Border.BorderBrushProperty, key);
        SkinGlow.SetColorKey(_pillOutline, key);
        SkinGlow.SetEnabled(_pillOutline, true);
        _pill.Width = Math.Max(0, geometry.Width - _pill.Margin.Left - _pill.Margin.Right);

        var wasHidden = _pill.Visibility != Visibility.Visible;
        _pill.Visibility = Visibility.Visible;

        if (!animate || wasHidden || Motion.Level != AnimationLevel.Full)
        {
            _pillTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            _pillTranslate.X = geometry.X;
            return;
        }

        var animation = new DoubleAnimation(geometry.X, TimeSpan.FromMilliseconds(260)) { EasingFunction = PillEase };
        _pillTranslate.BeginAnimation(TranslateTransform.XProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    // ---- Skin ----

    private bool IsRetro => _themeManager.CurrentPalette.Skin == UiSkin.Retro;

    private string SegmentText(PowerModeState state) =>
        IsRetro ? RetroText.SegmentLabel(state.GetDisplayName()) : state.GetDisplayName();

    private void ThemeManager_ThemeApplied(object? sender, EventArgs e) => ApplySkin();

    /// <summary>
    /// Retro gets bracketed labels, the console line and its blinking cursor; other skins get none of them.
    /// </summary>
    private void ApplySkin()
    {
        foreach (var segment in _segments.Children.OfType<RadioButton>())
            if (segment.Tag is PowerModeState state)
                segment.Content = SegmentText(state);

        _console.Visibility = IsRetro ? Visibility.Visible : Visibility.Collapsed;

        _cursorLoop?.Dispose();
        _cursorLoop = null;
        if (IsRetro)
        {
            var blink = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(1060), RepeatBehavior = RepeatBehavior.Forever };
            blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(530))));
            Storyboard.SetTarget(blink, _consoleCursor);
            Storyboard.SetTargetProperty(blink, new PropertyPath(OpacityProperty));
            _cursorLoop = Motion.RegisterLoop(_consoleCursor, new Storyboard { Children = { blink } });
        }

        // Re-measure: the labels' width changed.
        UpdateLayout();
        PositionPill(false);
    }

    /// <summary>
    /// Puts a line after the prompt; at Full motion it is typed out a character at a time.
    /// </summary>
    private void ShowConsoleLine(string line, bool type)
    {
        _typingTimer.Stop();
        var text = $"{RetroText.Prompt} {line}";
        AutomationProperties.SetName(_console, text);

        if (!type || Motion.Level != AnimationLevel.Full || !IsRetro)
        {
            _consoleText.Text = text;
            return;
        }

        _typingFrames = RetroText.TypingFrames(line).GetEnumerator();
        _typingTimer.Start();
    }

    private void TypingTimer_Tick(object? sender, EventArgs e)
    {
        if (_typingFrames is null || !_typingFrames.MoveNext())
        {
            _typingTimer.Stop();
            return;
        }
        _consoleText.Text = $"{RetroText.Prompt} {_typingFrames.Current}";
    }

    private async Task UpdateSettingsButtonAsync(PowerModeState state)
    {
        var mi = await Compatibility.GetMachineInformationAsync();
        var adapter = await Power.IsPowerAdapterConnectedAsync();
        _settingsButton.Visibility = PowerModeActions.HasSettings(state, mi.Properties.SupportsAIMode, mi.Properties.SupportsGodMode, adapter)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_current is { } state)
            await PowerModeActions.OpenSettingsAsync(state, this);
    }

    /// <summary>
    /// A quick start that settles gently, close to the mockup's cubic-bezier(.3, .7, .2, 1).
    /// </summary>
    private static IEasingFunction CreatePillEase()
    {
        var ease = new QuarticEase { EasingMode = EasingMode.EaseOut };
        ease.Freeze();
        return ease;
    }
}
