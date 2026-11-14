using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Microsoft.Xaml.Behaviors.Core;
using Windows.Win32;
using Windows.Win32.System.Threading;
using Wpf.Ui.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.SoftwareDisabler;
using LenovoLegionToolkit.Lib.Station.Services;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Controls.Custom;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Pages;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using LenovoLegionToolkit.WPF.Windows.Utils;
using CustomNavigationItem = LenovoLegionToolkit.WPF.Controls.Custom.NavigationItem;
using Wpf.Ui.Controls.Interfaces;
using Wpf.Ui.Common;

namespace LenovoLegionToolkit.WPF.Windows;

public partial class MainWindow
{
    private readonly ApplicationSettings _applicationSettings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly SpecialKeyListener _specialKeyListener = IoCContainer.Resolve<SpecialKeyListener>();
    private readonly DriverKeyListener _driverKeyListener = IoCContainer.Resolve<DriverKeyListener>();
    private readonly VantageDisabler _vantageDisabler = IoCContainer.Resolve<VantageDisabler>();
    private readonly LegionSpaceDisabler _legionSpaceDisabler = IoCContainer.Resolve<LegionSpaceDisabler>();
    private readonly LegionZoneDisabler _legionZoneDisabler = IoCContainer.Resolve<LegionZoneDisabler>();
    private readonly FnKeysDisabler _fnKeysDisabler = IoCContainer.Resolve<FnKeysDisabler>();
    private readonly INavigationService _extensionNavigationService = IoCContainer.Resolve<INavigationService>();
    private readonly UpdateChecker _updateChecker = IoCContainer.Resolve<UpdateChecker>();

    private const double CompactMinWidth = 550;
    private const double CompactMinHeight = 480;
    private const double CompactDefaultWidth = 900;
    private const double CompactDefaultHeight = 620;

    private TrayHelper? _trayHelper;
    private bool _windowSizeLocked;

    public bool TrayTooltipEnabled { get; init; } = true;
    public bool DisableConflictingSoftwareWarning { get; set; }
    public bool SuppressClosingEventHandler { get; set; }

    public Snackbar Snackbar => _snackbar;

    public MainWindow()
    {
        InitializeComponent();

        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
        IsVisibleChanged += MainWindow_IsVisibleChanged;
        Loaded += MainWindow_Loaded;
        SourceInitialized += MainWindow_SourceInitialized;
        StateChanged += MainWindow_StateChanged;

        ApplyPageTransition();
        Motion.LevelChanged += Motion_LevelChanged;
        IoCContainer.Resolve<ThemeManager>().ThemeApplied += ThemeManager_ThemeApplied;
        _contentGrid.IsVisibleChanged += ContentGrid_IsVisibleChanged;

        var version = Assembly.GetEntryAssembly()?.GetName().Version;
#if DEBUG
        _title.Text += Debugger.IsAttached ? " [DEBUGGER ATTACHED]" : " [DEBUG]";
#else
        if (version is not null && version.IsBeta())
        {
            _title.Text += " [BETA]";
        }
        else
        {
            _title.Text += $" {version}";
        }
#endif
        Focusable = false;
        if (Log.Instance.IsTraceEnabled)
        {
            _title.Text += " [LOGGING ENABLED]";
            _openLogIndicator.Visibility = Visibility.Visible;
        }

        Title = _title.Text;
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e) => RestoreSize();

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _contentGrid.Visibility = Visibility.Hidden;

        if (!AppFlags.Instance.Debug)
        {
            if (!await KeyboardBacklightPage.IsSupportedAsync())
            {
                _navigationStore.Items.Remove(_keyboardItem);
            }

            if (!await LampArrayRGBKeyboardPage.IsSupportedAsync())
            {
                _navigationStore.Items.Remove(_lampArrayKeyboardItem);
            }

            var mi = await Compatibility.GetMachineInformationAsync();
            if (!(mi.LegionSeries == LegionSeries.Legion_Pro_7 && mi.Generation >= 10))
            {
                _navigationStore.Items.Remove(_lampArrayKeyboardItem);
            }
        }

        var actionManager = IoCContainer.Resolve<SpecialKeyActionManager>();
        actionManager.WireUp(_specialKeyListener, () => Dispatcher.Invoke(BringToForeground));
        actionManager.WireUp(_driverKeyListener);

        AddExtensionNavigationItems();

        _contentGrid.Visibility = Visibility.Visible;

        LoadDeviceInfo();
        UpdateIndicators();
        CheckForUpdates();

        InputBindings.Add(new KeyBinding(new ActionCommand(_navigationStore.NavigateToNext), Key.Tab, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new ActionCommand(_navigationStore.NavigateToPrevious), Key.Tab, ModifierKeys.Control | ModifierKeys.Shift));

        var key = (int)Key.D1;
        foreach (var item in _navigationStore.Items.OfType<CustomNavigationItem>())
            InputBindings.Add(new KeyBinding(new ActionCommand(() => _navigationStore.Navigate(item.PageTag)), (Key)key++, ModifierKeys.Control));

        var trayHelper = new TrayHelper(_navigationStore, BringToForeground, TrayTooltipEnabled);
        await trayHelper.InitializeAsync();
        trayHelper.MakeVisible();
        _trayHelper = trayHelper;

        ApplyCompactLayout();
    }

    private void ApplyCompactLayout()
    {
        if (_applicationSettings.Store.CompactMode)
        {
            _contentGrid.Margin = new Thickness(4, 2, 0, 0);

            _contentGrid.ColumnDefinitions[0].Width = GridLength.Auto;
            _navigationStore.Margin = new Thickness(0, 0, 0, 4);

            _title.FontSize = 11;
            if (_title.Parent is Grid titleGrid)
            {
                titleGrid.Margin = new Thickness(8, 2, 150, 2);
            }

            _openLogIndicator.LayoutTransform = new ScaleTransform(0.8, 0.8);
            _openLogIndicator.Margin = new Thickness(0, 0, 4, 0);

            _deviceInfoIndicator.LayoutTransform = new ScaleTransform(0.8, 0.8);
            _deviceInfoIndicator.Margin = new Thickness(0, 0, 4, 0);

            foreach (var item in _navigationStore.Items.OfType<Wpf.Ui.Controls.NavigationItem>())
            {
                if (item.Content != null)
                {
                    item.ToolTip = item.Content;
                    item.Content = null;
                }
                item.HorizontalAlignment = HorizontalAlignment.Left;
                item.Margin = new Thickness(6, 2, 6, 2);
            }
            foreach (var item in _navigationStore.Footer.OfType<Wpf.Ui.Controls.NavigationItem>())
            {
                if (item.Content != null)
                {
                    item.ToolTip = item.Content;
                    item.Content = null;
                }
                item.HorizontalAlignment = HorizontalAlignment.Left;
                item.Margin = new Thickness(6, 2, 6, 2);
            }

            _vantageIndicator.Padding = new Thickness(4, 2, 4, 2);
            _legionZoneIndicator.Padding = new Thickness(4, 2, 4, 2);
            _legionSpaceIndicator.Padding = new Thickness(4, 2, 4, 2);
            _fnKeysIndicator.Padding = new Thickness(4, 2, 4, 2);
            _updateIndicator.Padding = new Thickness(4, 2, 4, 2);
        }
    }

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!App.IsRestoringSettings)
            SaveSize();

        if (SuppressClosingEventHandler)
            return;

        if (_applicationSettings.Store.MinimizeOnClose)
        {
            Log.Instance.Trace($"Minimizing...");

            WindowState = WindowState.Minimized;
            e.Cancel = true;
        }
        else
        {
            Log.Instance.Trace($"Closing...");

            await App.Current.ShutdownAsync();
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs args)
    {
        _trayHelper?.Dispose();
        _trayHelper = null;

        Motion.LevelChanged -= Motion_LevelChanged;
        IoCContainer.Resolve<ThemeManager>().ThemeApplied -= ThemeManager_ThemeApplied;
        _sweepLoop?.Dispose();
        _sweepLoop = null;
        StopCrtLoops();
    }

    private IDisposable? _sweepLoop;
    private IDisposable? _rollBandLoop;
    private IDisposable? _flickerLoop;
    private DropShadowEffect? _phosphorGlow;
    private UiSkin? _lastSkin;
    private bool _powerOnShown;

    private void ThemeManager_ThemeApplied(object? sender, EventArgs e)
    {
        UpdateSkinDecorations();
        UpdateCrt();
        SetVisual();
    }

    // ---- Retro CRT ----

    /// <summary>
    /// Shows or hides the CRT layer for the Retro skin and applies its settings. Looping effects (rolling band,
    /// flicker) only run at Full motion while the window is on screen; the phosphor glow is one effect on the
    /// content, used only when it's turned on and the GPU draws effects.
    /// </summary>
    private void UpdateCrt()
    {
        var palette = IoCContainer.Resolve<ThemeManager>().CurrentPalette;
        var settings = IoCContainer.Resolve<ApplicationSettings>();
        var retro = palette.Skin == UiSkin.Retro;
        var switchedToRetro = retro && _lastSkin is not null && _lastSkin != UiSkin.Retro;
        _lastSkin = palette.Skin;

        StopCrtLoops();

        if (!retro)
        {
            _crtLayer.Visibility = Visibility.Collapsed;
            SetPhosphorGlow(null);
            return;
        }

        var phosphor = palette.Colors?.TextPrimary ?? Colors.White;

        _crtLayer.Visibility = Visibility.Visible;
        _scanlines.Opacity = Math.Clamp(settings.Store.RetroScanlines, 0, 100) / 100.0 * 0.6;

        _rollBand.Fill = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(Color.FromArgb(0, phosphor.R, phosphor.G, phosphor.B), 0),
                new(Color.FromArgb(0x14, phosphor.R, phosphor.G, phosphor.B), 0.5),
                new(Color.FromArgb(0, phosphor.R, phosphor.G, phosphor.B), 1),
            }, 90);
        _rollBand.Opacity = 1;

        var roll = new DoubleAnimation(-140, SystemParameters.VirtualScreenHeight, TimeSpan.FromSeconds(7)) { RepeatBehavior = RepeatBehavior.Forever };
        Storyboard.SetTarget(roll, _rollBand);
        Storyboard.SetTargetProperty(roll, new PropertyPath("(0).(1)", RenderTransformProperty, TranslateTransform.YProperty));
        _rollBandLoop = Motion.RegisterLoop(_rollBand, new Storyboard { Children = { roll } });

        // A barely visible, irregular flicker (a few percent of the glass layer's strength).
        var flicker = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(3.7), RepeatBehavior = RepeatBehavior.Forever };
        foreach (var (ms, value) in new[] { (0, 1.0), (900, 0.96), (1000, 1.0), (2300, 0.97), (2380, 1.0), (3700, 1.0) })
            flicker.KeyFrames.Add(new LinearDoubleKeyFrame(value, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
        Storyboard.SetTarget(flicker, _crtLayer);
        Storyboard.SetTargetProperty(flicker, new PropertyPath(OpacityProperty));
        _flickerLoop = Motion.RegisterLoop(_crtLayer, new Storyboard { Children = { flicker } });

        SetPhosphorGlow(settings.Store.RetroGlow && Motion.EffectsAllowed ? phosphor : null);

        if (switchedToRetro)
            PlayPowerOn();
    }

    private void StopCrtLoops()
    {
        _rollBandLoop?.Dispose();
        _rollBandLoop = null;
        _flickerLoop?.Dispose();
        _flickerLoop = null;
    }

    private void SetPhosphorGlow(Color? color)
    {
        if (color is null)
        {
            if (_phosphorGlow is not null && ReferenceEquals(_contentGrid.Effect, _phosphorGlow))
                _contentGrid.Effect = null;
            _phosphorGlow = null;
            return;
        }

        _phosphorGlow = new DropShadowEffect
        {
            Color = color.Value,
            BlurRadius = 10,
            ShadowDepth = 0,
            Opacity = 0.35,
            RenderingBias = RenderingBias.Performance,
        };
        _phosphorGlow.Freeze();
        _contentGrid.Effect = _phosphorGlow;
    }

    /// <summary>
    /// The picture opening up from a bright line, like an old monitor warming up. Full motion only.
    /// </summary>
    private void PlayPowerOn()
    {
        if (Motion.Level != AnimationLevel.Full || !_contentGrid.IsVisible)
            return;

        _powerOnShown = true;
        var scale = new ScaleTransform(1, 1);
        _contentGrid.RenderTransformOrigin = new Point(0.5, 0.5);
        _contentGrid.RenderTransform = scale;

        var duration = TimeSpan.FromMilliseconds(650);
        var open = new DoubleAnimationUsingKeyFrames { Duration = duration, FillBehavior = FillBehavior.Stop };
        open.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.01, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        open.KeyFrames.Add(new EasingDoubleKeyFrame(0.01, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
        open.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(duration), new CubicEase { EasingMode = EasingMode.EaseOut }));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, open);

        var fade = new DoubleAnimation(0.2, 1, duration) { FillBehavior = FillBehavior.Stop };
        _contentGrid.BeginAnimation(OpacityProperty, fade);
    }

    private void ContentGrid_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // First time the content appears in Retro (app start), play the warm-up once.
        if (_contentGrid.IsVisible && !_powerOnShown && _lastSkin == UiSkin.Retro)
            PlayPowerOn();
    }

    /// <summary>
    /// Skin-specific window decorations: Neon's sweep line under the title bar.
    /// </summary>
    private void UpdateSkinDecorations()
    {
        var palette = IoCContainer.Resolve<ThemeManager>().CurrentPalette;

        if (palette.Skin != UiSkin.Neon)
        {
            _sweepLoop?.Dispose();
            _sweepLoop = null;
            _sweepLine.Visibility = Visibility.Collapsed;
            return;
        }

        var glow = palette.GlowColor;
        var shift = new TranslateTransform(-0.65, 0);
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5),
            RelativeTransform = shift,
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0, glow.R, glow.G, glow.B), 0.35),
                new GradientStop(glow, 0.5),
                new GradientStop(Color.FromArgb(0, glow.R, glow.G, glow.B), 0.65),
            }
        };
        _sweepLine.Fill = brush;
        _sweepLine.Visibility = Visibility.Visible;

        var sweep = new DoubleAnimation(-0.65, 0.65, TimeSpan.FromSeconds(3.2)) { RepeatBehavior = RepeatBehavior.Forever };
        Storyboard.SetTarget(sweep, _sweepLine);
        Storyboard.SetTargetProperty(sweep, new PropertyPath("(0).(1).(2)", System.Windows.Shapes.Shape.FillProperty, Brush.RelativeTransformProperty, TranslateTransform.XProperty));
        var storyboard = new Storyboard { Children = { sweep } };

        _sweepLoop?.Dispose();
        _sweepLoop = Motion.RegisterLoop(_sweepLine, storyboard);
    }

    private void Motion_LevelChanged(object? sender, EventArgs e) => ApplyPageTransition();

    private void ApplyPageTransition()
    {
        var (type, duration) = Motion.PageTransition(Motion.Level);
        _navigationStore.TransitionType = type;
        _navigationStore.TransitionDuration = duration;
    }

    /// <summary>
    /// Looping effects run only while the window is actually on screen.
    /// </summary>
    private void UpdateMotionWindowState() => Motion.SetWindowShown(IsVisible && WindowState != WindowState.Minimized);

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        UpdateMotionWindowState();

        Log.Instance.Trace($"Window state changed to {WindowState}");

        if (_windowSizeLocked && WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            return;
        }

        switch (WindowState)
        {
            case WindowState.Minimized:
                SetEfficiencyMode(true);
                SendToTray();
                break;
            case WindowState.Normal:
                SetEfficiencyMode(false);
                BringToForeground();
                break;
        }
    }

    private void MainWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        var settings = IoCContainer.Resolve<ApplicationSettings>();
        ApplyWindowLock(settings.Store.LockWindowSize);
        Topmost = settings.Store.AlwaysOnTop;

        UpdateMotionWindowState();

        if (!IsVisible)
            return;

        CheckForUpdates();
        SetVisual();
    }

    public void ApplyWindowLock(bool locked)
    {
        _windowSizeLocked = locked;
        if (locked)
        {
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Manual;
        }
        else
        {
            ResizeMode = ResizeMode.CanResize;
        }

        var chrome = WindowChrome.GetWindowChrome(this);
        if (chrome is not null)
        {
            var newChrome = (WindowChrome)chrome.Clone();
            newChrome.ResizeBorderThickness = locked ? new Thickness(0) : SystemParameters.WindowResizeBorderThickness;
            WindowChrome.SetWindowChrome(this, newChrome);
        }
    }

    private void AddExtensionNavigationItems()
    {
        foreach (var item in _extensionNavigationService.Items.Where(i => !i.IsFooter))
        {
            if (_navigationStore.Items.OfType<CustomNavigationItem>().Any(existing => string.Equals(existing.PageTag, item.PageTag, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            _navigationStore.Items.Add(new CustomNavigationItem
            {
                Content = item.TitleGetter != null ? item.TitleGetter() : item.Title,
                Icon = item.Icon switch
                {
                    ExtensionIcon.Gauge => SymbolRegular.Gauge24,
                    _ => SymbolRegular.Empty,
                },
                PageTag = item.PageTag,
                PageType = item.PageType
            });
        }
    }

    private void OpenLogIndicator_Click(object sender, MouseButtonEventArgs e) => OpenLog();

    private void OpenLogIndicator_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Enter and not Key.Space)
            return;

        OpenLog();
    }

    private void DeviceInfoIndicator_Click(object sender, MouseButtonEventArgs e) => ShowDeviceInfoWindow();

    private void DeviceInfoIndicator_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Enter and not Key.Space)
            return;

        ShowDeviceInfoWindow();
    }

    private void UpdateIndicator_Click(object sender, RoutedEventArgs e) => ShowUpdateWindow();

    private void UpdateIndicator_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Enter and not Key.Space)
            return;

        ShowUpdateWindow();
    }

    private async void LoadDeviceInfo()
    {
        string? modelName;

        if (Compatibility.FakeMachineInformationMode)
        {
            var fakeMi = await Compatibility.GetFakeMachineInformationAsync();

            if (fakeMi.HasValue)
            {
                modelName = fakeMi.Value.Model;
            }
            else
            {
                var realMi = await Compatibility.GetMachineInformationAsync();
                modelName = realMi.Model;
            }
        }
        else
        {
            var mi = await Compatibility.GetMachineInformationAsync();
            modelName = mi.Model;
        }

        if (string.IsNullOrEmpty(modelName))
        {
            return;
        }

        _deviceInfoIndicator.Content = modelName;
        _deviceInfoIndicator.Visibility = Visibility.Visible;
    }

    private void UpdateIndicators()
    {
        if (DisableConflictingSoftwareWarning)
            return;

        _vantageDisabler.OnRefreshed += async (_, e) => await Dispatcher.InvokeAsync(() =>
        {
            _vantageIndicator.Visibility = e.Status == SoftwareStatus.Enabled ? Visibility.Visible : Visibility.Collapsed;
        });

        _legionSpaceDisabler.OnRefreshed += async (_, e) => await Dispatcher.InvokeAsync(() =>
        {
            _legionSpaceIndicator.Visibility = e.Status == SoftwareStatus.Enabled ? Visibility.Visible : Visibility.Collapsed;
        });

        _legionZoneDisabler.OnRefreshed += async (_, e) => await Dispatcher.InvokeAsync(() =>
        {
            _legionZoneIndicator.Visibility = e.Status == SoftwareStatus.Enabled ? Visibility.Visible : Visibility.Collapsed;
        });

        _fnKeysDisabler.OnRefreshed += async (_, e) => await Dispatcher.InvokeAsync(() =>
        {
            _fnKeysIndicator.Visibility = e.Status == SoftwareStatus.Enabled ? Visibility.Visible : Visibility.Collapsed;
        });

        Task.Run(async () =>
        {
            _ = await _vantageDisabler.GetStatusAsync().ConfigureAwait(false);
            _ = await _legionSpaceDisabler.GetStatusAsync().ConfigureAwait(false);
            _ = await _legionZoneDisabler.GetStatusAsync().ConfigureAwait(false);
            _ = await _fnKeysDisabler.GetStatusAsync().ConfigureAwait(false);
        });
    }

    public void CheckForUpdates(bool manualCheck = false)
    {
        UpdateSettings _updateSettings = IoCContainer.Resolve<UpdateSettings>();
        if (_updateSettings.Store.UpdateCheckFrequency != UpdateCheckFrequency.Never || manualCheck)
        {
            Task.Run(() => _updateChecker.CheckAsync(manualCheck))
                        .ContinueWith(async updatesAvailable =>
                        {
                            var result = updatesAvailable.Result;
                            if (result is null)
                            {
                                _updateIndicator.Visibility = Visibility.Collapsed;

                                if (manualCheck && WindowState != WindowState.Minimized)
                                {
                                    switch (_updateChecker.Status)
                                    {
                                        case UpdateCheckStatus.Success:
                                            await SnackbarHelper.ShowAsync(Resource.MainWindow_CheckForUpdates_Success_Title);
                                            break;
                                        case UpdateCheckStatus.RateLimitReached:
                                            await SnackbarHelper.ShowAsync(Resource.MainWindow_CheckForUpdates_Error_Title, Resource.MainWindow_CheckForUpdates_Error_ReachedRateLimit_Message, SnackbarType.Error);
                                            break;
                                        case UpdateCheckStatus.Error:
                                            await SnackbarHelper.ShowAsync(Resource.MainWindow_CheckForUpdates_Error_Title, Resource.MainWindow_CheckForUpdates_Error_Unknown_Message, SnackbarType.Error);
                                            break;
                                    }
                                }
                            }
                            else
                            {
                                var versionNumber = result.ToString();

                                _updateIndicatorText.Text =
                                    string.Format(Resource.MainWindow_UpdateAvailableWithVersion, versionNumber);
                                _updateIndicator.Visibility = Visibility.Visible;

                                if (WindowState == WindowState.Minimized)
                                    MessagingCenter.Publish(new NotificationMessage(NotificationType.UpdateAvailable, versionNumber));
                            }
                        }, TaskScheduler.FromCurrentSynchronizationContext());
        }
    }

    private void RestoreSize()
    {
        if (_applicationSettings.Store.CompactMode)
        {
            MinWidth = CompactMinWidth;
            MinHeight = CompactMinHeight;
        }

        if (!_applicationSettings.Store.WindowSize.HasValue)
        {
            if (_applicationSettings.Store.CompactMode)
            {
                Width = CompactDefaultWidth;
                Height = CompactDefaultHeight;
            }
            return;
        }

        Width = Math.Max(MinWidth, _applicationSettings.Store.WindowSize.Value.Width);
        Height = Math.Max(MinHeight, _applicationSettings.Store.WindowSize.Value.Height);

        ScreenHelper.UpdateScreenInfos();
        var primaryScreen = ScreenHelper.PrimaryScreen;

        if (!primaryScreen.HasValue)
            return;

        var desktopWorkingArea = primaryScreen.Value.WorkArea;

        if (_applicationSettings.Store.RememberWindowPosition && _applicationSettings.Store.WindowPosition.HasValue)
        {
            var pos = _applicationSettings.Store.WindowPosition.Value;
            var windowRect = new Rect(pos.Left, pos.Top, Width, Height);
            var screen = ScreenHelper.Screens.FirstOrDefault(s => s.WorkArea.IntersectsWith(windowRect));
            var targetWorkingArea = screen.WorkArea != default ? screen.WorkArea : desktopWorkingArea;

            Left = Math.Max(targetWorkingArea.Left, Math.Min(pos.Left, targetWorkingArea.Right - Width));
            Top = Math.Max(targetWorkingArea.Top, Math.Min(pos.Top, targetWorkingArea.Bottom - Height));
        }
        else
        {
            Left = (desktopWorkingArea.Width - Width) / 2 + desktopWorkingArea.Left;
            Top = (desktopWorkingArea.Height - Height) / 2 + desktopWorkingArea.Top;
        }
    }

    private void SaveSize()
    {
        _applicationSettings.Store.WindowSize = WindowState != WindowState.Normal
            ? new(RestoreBounds.Width, RestoreBounds.Height)
            : new(Width, Height);
        _applicationSettings.Store.WindowPosition = _applicationSettings.Store.RememberWindowPosition
            ? (WindowState != WindowState.Normal ? new(RestoreBounds.Left, RestoreBounds.Top) : new(Left, Top))
            : null;
        _applicationSettings.SynchronizeStore();
    }

    private void BringToForeground() => WindowExtensions.BringToForeground(this);

    private static void OpenLog()
    {
        try
        {
            if (!Directory.Exists(Folders.AppData))
                return;

            Process.Start("explorer", Log.Instance.LogPath);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to open log.", ex);
        }
    }

    private void ShowDeviceInfoWindow()
    {
        var window = new DeviceInformationWindow { Owner = this };
        window.ShowDialog();
    }

    public void ShowUpdateWindow()
    {
        var window = new UpdateWindow { Owner = this };
        window.ShowDialog();
    }

    public void SendToTray()
    {
        if (!_applicationSettings.Store.MinimizeToTray)
            return;

        SetEfficiencyMode(true);
        Hide();
        ShowInTaskbar = true;
    }

    public void SetMainWindowBackgroundImage(string filePath)
    {
        BitmapImage bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(filePath);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        _backgroundImage.ImageSource = bitmap;
    }

    private static unsafe void SetEfficiencyMode(bool enabled)
    {
        const uint PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION = 0x00000004;

        var priorityClass = enabled
            ? PROCESS_CREATION_FLAGS.IDLE_PRIORITY_CLASS
            : PROCESS_CREATION_FLAGS.NORMAL_PRIORITY_CLASS;
        PInvoke.SetPriorityClass(PInvoke.GetCurrentProcess(), priorityClass);

        var state = new PROCESS_POWER_THROTTLING_STATE
        {
            Version = PInvoke.PROCESS_POWER_THROTTLING_CURRENT_VERSION,
            ControlMask = PInvoke.PROCESS_POWER_THROTTLING_EXECUTION_SPEED | PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION,
            StateMask = enabled ? (PInvoke.PROCESS_POWER_THROTTLING_EXECUTION_SPEED | PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION) : 0,
        };

        PInvoke.SetProcessInformation(PInvoke.GetCurrentProcess(),
            PROCESS_INFORMATION_CLASS.ProcessPowerThrottling,
            &state,
            (uint)sizeof(PROCESS_POWER_THROTTLING_STATE));
    }

    public void SetWindowOpacity(double opacity)
    {
        _backgroundDimOverlay.Opacity = opacity;
    }

    public void SetBackgroundBlur(int radius)
    {
        _backgroundBlurEffect.Radius = radius;
    }

    public void SetBackgroundStretch(BackgroundImageStretchMode stretch)
    {
        _backgroundImage.Stretch = stretch switch
        {
            BackgroundImageStretchMode.Fit => Stretch.Uniform,
            BackgroundImageStretchMode.Crop => Stretch.UniformToFill,
            _ => Stretch.Fill
        };
    }

    public void SetVisual()
    {
        var settings = IoCContainer.Resolve<ApplicationSettings>();
        var result = settings.Store.BackGroundImageFilePath;
        var opacity = settings.Store.Opacity;
        try
        {
            if (IoCContainer.Resolve<ThemeManager>().CurrentPalette.SolidBackground)
            {
                // Neon and Retro draw their own solid background; the image setting is kept for Fluent.
                _backgroundImage.ImageSource = null;
                SetWindowOpacity(1);
                SetBackgroundBlur(0);
            }
            else if (result != string.Empty)
            {
                SetMainWindowBackgroundImage(result);
                SetWindowOpacity(opacity);
                SetBackgroundBlur(settings.Store.BackgroundImageBlur);
                SetBackgroundStretch(settings.Store.BackgroundImageStretch);
            }
            else
            {
                _backgroundImage.ImageSource = null;
                SetWindowOpacity(0);
                SetBackgroundBlur(0);
            }
        }
        catch (Exception ex)
        {
            SnackbarHelper.Show(Resource.Warning, ex.Message, SnackbarType.Error);
            Log.Instance.Trace($"Exception occured when executing SetBackgroundImage().", ex);
        }
    }

    private void NavigationStore_Navigated(INavigation sender, RoutedNavigationEventArgs e)
    {
        SetVisual();
    }

    private void SoftwareIndicator_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        SettingsPage.PendingTabKey = "SoftwareControl";
        _navigationStore.Navigate("settings");
        SettingsPage.Instance?.ApplyPendingTab();
    }
}
