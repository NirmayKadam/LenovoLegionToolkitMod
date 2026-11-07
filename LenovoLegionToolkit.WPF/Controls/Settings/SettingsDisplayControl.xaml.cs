using System.Threading.Tasks;
using System.Windows;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Windows.Settings;

namespace LenovoLegionToolkit.WPF.Controls.Settings;

public partial class SettingsDisplayControl
{
    private readonly ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();

    private bool _isRefreshing;

    public SettingsDisplayControl()
    {
        InitializeComponent();
    }

    public async Task RefreshAsync()
    {
        _isRefreshing = true;

        _synchronizeBrightnessToAllPowerPlansToggle.IsChecked = _settings.Store.SynchronizeBrightnessToAllPowerPlans;
        _bootLogoCard.Visibility = await BootLogo.IsSupportedAsync() ? Visibility.Visible : Visibility.Collapsed;

        _synchronizeBrightnessToAllPowerPlansToggle.Visibility = Visibility.Visible;

        _isRefreshing = false;
    }

    private void SynchronizeBrightnessToAllPowerPlansToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        var state = _synchronizeBrightnessToAllPowerPlansToggle.IsChecked;
        if (state is null)
            return;

        _settings.Store.SynchronizeBrightnessToAllPowerPlans = state.Value;
        _settings.SynchronizeStore();
    }

    private void BootLogo_Click(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing)
            return;

        var window = new BootLogoWindow { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }
}
