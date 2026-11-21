using System;
using System.Threading.Tasks;
using System.Windows;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.WPF.Controls.Dashboard;

public partial class TurnOffMonitorsControl
{
    private readonly NativeWindowsMessageListener _nativeWindowsMessageListener = IoCContainer.Resolve<NativeWindowsMessageListener>();

    public TurnOffMonitorsControl() => InitializeComponent();

    private async void TurnOffButton_Click(object sender, RoutedEventArgs e)
    {
        _turnOffButton.IsEnabled = false;
        try
        {
            await _nativeWindowsMessageListener.TurnOffMonitorAsync();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Turning off the monitors failed.", ex);
        }
        finally
        {
            _turnOffButton.IsEnabled = true;
        }
    }

    protected override Task OnRefreshAsync() => Task.CompletedTask;

    protected override void OnFinishedLoading() { }
}
