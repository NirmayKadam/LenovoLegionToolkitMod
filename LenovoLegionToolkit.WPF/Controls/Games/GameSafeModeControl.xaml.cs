using System;
using System.Threading.Tasks;
using System.Windows;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.GameProfiles;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Utils;

namespace LenovoLegionToolkit.WPF.Controls.Games;

/// <summary>The game-safe mode switch, with whether it is on right now and why.</summary>
public partial class GameSafeModeControl
{
    private readonly ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly GameSafeMode _gameSafeMode = IoCContainer.Resolve<GameSafeMode>();
    private readonly AntiCheatMonitor _antiCheatMonitor = IoCContainer.Resolve<AntiCheatMonitor>();
    private readonly GameProfileController _gameProfiles = IoCContainer.Resolve<GameProfileController>();

    public GameSafeModeControl()
    {
        InitializeComponent();

        // Only while shown, so a closed page doesn't keep this card alive.
        Loaded += (_, _) =>
        {
            _gameSafeMode.ActiveChanged -= GameSafeMode_ActiveChanged;
            _gameSafeMode.ActiveChanged += GameSafeMode_ActiveChanged;
        };
        Unloaded += (_, _) => _gameSafeMode.ActiveChanged -= GameSafeMode_ActiveChanged;
    }

    protected override Task OnRefreshAsync()
    {
        _toggle.IsChecked = _settings.Store.GameSafeMode;
        UpdateStatus();
        return Task.CompletedTask;
    }

    protected override void OnFinishedLoading() { }

    // Raised on a background thread.
    private void GameSafeMode_ActiveChanged(object? sender, bool active) => Dispatcher.Post(UpdateStatus);

    private async void Toggle_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _settings.Store.GameSafeMode = _toggle.IsChecked == true;
            _settings.SynchronizeStore();
            await _gameSafeMode.RefreshAsync();
            UpdateStatus();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Changing game-safe mode failed.", ex);
        }
    }

    private void UpdateStatus() =>
        _cardHeader.Success = GameStatusText.GameSafeMode(_gameSafeMode.IsActive, _antiCheatMonitor.ActiveServices, _gameProfiles.ActiveGameName);
}
