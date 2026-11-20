using System.Threading.Tasks;
using System.Windows;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.GameProfiles;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Windows.Settings;

namespace LenovoLegionToolkit.WPF.Controls.Games;

/// <summary>Opens the game profiles, and shows the game whose profile is in effect with the power mode it set.</summary>
public partial class GameProfilesControl
{
    private readonly GameProfileController _gameProfiles = IoCContainer.Resolve<GameProfileController>();

    public GameProfilesControl()
    {
        InitializeComponent();

        // Only while shown, so a closed page doesn't keep this card alive.
        Loaded += (_, _) =>
        {
            _gameProfiles.Reported -= GameProfiles_Reported;
            _gameProfiles.Reported += GameProfiles_Reported;
        };
        Unloaded += (_, _) => _gameProfiles.Reported -= GameProfiles_Reported;
    }

    protected override Task OnRefreshAsync()
    {
        UpdateStatus();
        return Task.CompletedTask;
    }

    protected override void OnFinishedLoading() { }

    // Raised on a background thread after a profile was applied or put back.
    private void GameProfiles_Reported(object? sender, GameProfileReport report) => Dispatcher.Post(UpdateStatus);

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        new GameProfilesWindow { Owner = Window.GetWindow(this) }.ShowDialog();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var status = GameStatusText.GameProfile(_gameProfiles.ActiveGameName, _gameProfiles.ActivePowerMode?.GetDisplayName());
        _cardHeader.Success = status ?? string.Empty;
        _cardHeader.Info = status is null ? Resource.GamesCard_NoGame : string.Empty;
    }
}
