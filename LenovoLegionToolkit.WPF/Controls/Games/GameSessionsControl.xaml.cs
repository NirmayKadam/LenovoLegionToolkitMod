using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.GameSessions;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using LenovoLegionToolkit.WPF.Windows.Dashboard;

namespace LenovoLegionToolkit.WPF.Controls.Games;

/// <summary>The game session report: the switch, the last session, and its history.</summary>
public partial class GameSessionsControl
{
    private readonly ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly GameSessionSettings _history = IoCContainer.Resolve<GameSessionSettings>();
    private readonly GameSessionRecorder _recorder = IoCContainer.Resolve<GameSessionRecorder>();

    public GameSessionsControl()
    {
        InitializeComponent();

        this.WhileLoaded(() => _recorder.SessionEnded += Recorder_SessionEnded, () => _recorder.SessionEnded -= Recorder_SessionEnded);
    }

    protected override Task OnRefreshAsync()
    {
        _toggle.IsChecked = _settings.Store.GameSessionReport;
        ShowLast();
        return Task.CompletedTask;
    }

    protected override void OnFinishedLoading() { }

    // Raised on a background thread.
    private void Recorder_SessionEnded(object? sender, GameSessionReport e) => Dispatcher.Post(ShowLast);

    private void ShowLast() =>
        _cardHeader.Info = _history.Store.Sessions.FirstOrDefault() is { } last
            ? string.Format(Resource.GameSessions_Last, GameSessionText.Summary(last, _settings.Store.TemperatureUnit))
            : Resource.GameSessions_None;

    private async void Toggle_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _settings.Store.GameSessionReport = _toggle.IsChecked == true;
            _settings.SynchronizeStore();
            await _recorder.ApplySettingAsync();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Changing the game session report failed.", ex);
        }
    }

    private void HistoryButton_Click(object sender, RoutedEventArgs e) =>
        new GameSessionsWindow { Owner = Window.GetWindow(this) }.ShowDialog();
}
