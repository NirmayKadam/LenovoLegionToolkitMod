using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.GameProfiles;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Windows.Settings;

namespace LenovoLegionToolkit.WPF.Controls.Games;

/// <summary>Opens the background services, and shows how many of them Indra keeps stopped right now.</summary>
public partial class BackgroundServicesControl
{
    private readonly BackgroundServicesSettings _settings = IoCContainer.Resolve<BackgroundServicesSettings>();
    private readonly BackgroundServicesController _controller = IoCContainer.Resolve<BackgroundServicesController>();

    public BackgroundServicesControl()
    {
        InitializeComponent();

        // Only while shown, so a closed page doesn't keep this card alive.
        Loaded += (_, _) =>
        {
            _controller.Changed -= Controller_Changed;
            _controller.Changed += Controller_Changed;
        };
        Unloaded += (_, _) => _controller.Changed -= Controller_Changed;
    }

    protected override Task OnRefreshAsync() => UpdateStatusAsync();

    protected override void OnFinishedLoading() { }

    // Raised on any thread after an apply or a put-back.
    private void Controller_Changed(object? sender, EventArgs e) => Dispatcher.Post(UpdateStatusAsync);

    private async void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        new BackgroundServicesWindow { Owner = Window.GetWindow(this) }.ShowDialog();

        try
        {
            await UpdateStatusAsync();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Showing the background services status failed.", ex);
        }
    }

    private async Task UpdateStatusAsync()
    {
        // Reading the services' states asks Windows about each one, so it is done off the UI thread.
        var enabled = _settings.Store.Enabled;
        var stopped = enabled
            ? await Task.Run(() => _controller.GetStatus().Count(s => s.State == BackgroundServiceState.StoppedByLlt))
            : 0;

        var status = GameStatusText.BackgroundServices(enabled, stopped);
        _cardHeader.Success = status ?? string.Empty;
        _cardHeader.Info = status is null ? Resource.GamesCard_ServicesOff : string.Empty;
    }
}
