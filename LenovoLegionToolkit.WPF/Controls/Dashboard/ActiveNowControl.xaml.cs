using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.AutoListeners;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.GameProfiles;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Utils;

namespace LenovoLegionToolkit.WPF.Controls.Dashboard;

/// <summary>
/// The dashboard's "Active now" strip: what Indra is doing in the background (a game counted as running, AI mode's
/// boost, game-safe mode, a game profile, a fan curve in use, stopped services, MSI Afterburner, safe start). It only
/// reads state other parts already keep (<see cref="ActiveNowReader"/>); listening to them starts nothing. Refreshed
/// on their events, and every 5 s while visible for MSI Afterburner, which has no event.
/// </summary>
public partial class ActiveNowControl
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly ActiveNowReader _reader = new();
    private readonly GameAutoListener _games = IoCContainer.Resolve<GameAutoListener>();
    private readonly AIController _ai = IoCContainer.Resolve<AIController>();
    private readonly GameSafeMode _gameSafe = IoCContainer.Resolve<GameSafeMode>();
    private readonly GameProfileController _gameProfiles = IoCContainer.Resolve<GameProfileController>();
    private readonly FanProfileController _fanProfiles = IoCContainer.Resolve<FanProfileController>();
    private readonly BackgroundServicesController _services = IoCContainer.Resolve<BackgroundServicesController>();
    private readonly PowerModeListener _powerModeListener = IoCContainer.Resolve<PowerModeListener>();

    private readonly ThrottleLastDispatcher _throttle = new(TimeSpan.FromMilliseconds(300), nameof(ActiveNowControl));
    private readonly DispatcherTimer _poll = new() { Interval = PollInterval };

    private PowerModeState? _mode;

    public ActiveNowControl()
    {
        InitializeComponent();

        _poll.Tick += (_, _) =>
        {
            if (Motion.IsWindowShown)   // not while minimized: nobody sees it, and the read lists every process
                QueueRefresh();
        };
        IsVisibleChanged += ActiveNowControl_IsVisibleChanged;

        this.WhileLoaded(() => _games.GamesChanged += Something_Changed, () => _games.GamesChanged -= Something_Changed);
        this.WhileLoaded(() => _ai.BoostChanged += Something_Changed, () => _ai.BoostChanged -= Something_Changed);
        this.WhileLoaded(() => _gameSafe.ActiveChanged += GameSafe_ActiveChanged, () => _gameSafe.ActiveChanged -= GameSafe_ActiveChanged);
        this.WhileLoaded(() => _gameProfiles.Reported += GameProfiles_Reported, () => _gameProfiles.Reported -= GameProfiles_Reported);
        this.WhileLoaded(() => _fanProfiles.Applied += FanProfiles_Applied, () => _fanProfiles.Applied -= FanProfiles_Applied);
        this.WhileLoaded(() => _services.Changed += Something_Changed, () => _services.Changed -= Something_Changed);
        this.WhileLoaded(() => _powerModeListener.Changed += PowerModeListener_Changed, () => _powerModeListener.Changed -= PowerModeListener_Changed);
    }

    /// <summary>The chips on screen (records, so equal chips compare equal).</summary>
    private IReadOnlyList<ActiveNowChip>? _shown;

    private void ActiveNowControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            _mode = null;   // read again: it may have changed while the dashboard was hidden
            _poll.Start();
            QueueRefresh();
        }
        else
        {
            _poll.Stop();
        }
    }

    private void Something_Changed(object? sender, EventArgs e) => QueueRefresh();
    private void GameSafe_ActiveChanged(object? sender, bool e) => QueueRefresh();
    private void GameProfiles_Reported(object? sender, GameProfileReport e) => QueueRefresh();
    private void FanProfiles_Applied(object? sender, (PowerModeState Mode, FanCurveApplyResult Result) e) => QueueRefresh();

    private void PowerModeListener_Changed(object? sender, PowerModeListener.ChangedEventArgs e)
    {
        _mode = e.State;
        QueueRefresh();
    }

    private async void QueueRefresh() => await _throttle.DispatchLoggingErrorsAsync(async () =>
    {
        // The process list (MSI Afterburner) and the power mode are read off the UI thread.
        var known = _mode;
        var (state, mode) = await Task.Run(() => _reader.ReadAsync(known));
        _mode ??= mode;

        await Dispatcher.InvokeAsync(() =>
        {
            if (!IsLoaded || !IsVisible)
                return;

            var chips = ActiveNow.Build(state);

            // The same chips as shown: nothing to rebuild (it used to rebuild them, and lay out the top of the
            // dashboard again, every 5 s).
            if (_shown is not null && _shown.SequenceEqual(chips))
                return;

            _shown = chips;
            _chips.ItemsSource = chips;
            _root.Visibility = chips.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        });
    });
}
